using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using SocketType = Clean_Hackus_NET8.Models.Enums.SocketType;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Clean_Hackus_NET8.Net.Mail;

/// <summary>
/// IMAP client — MailKit SYNC API only.
/// Matches old Hackus patterns: Blocked detection, HostNotFound, proper error classification.
/// </summary>
public class ImapClient : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private const int TIMEOUT_MS = 15000;

    private MailKit.Net.Imap.ImapClient? _client;
    private IMailFolder? _currentFolder;

    // Blocked keywords (from old Hackus MailException.IsBlocked / IsServerDisabled)
    private static readonly string[] BlockedKeywords = [
        "temporarily blocked", "temporary block", "too many", "rate limit",
        "try again later", "account disabled", "account locked", "suspended",
        "try later", "login denied", "overquota", "quota exceeded"
    ];

    private static readonly string[] ServerDisabledKeywords = [
        "imap access disabled", "imap is disabled", "service not available",
        "login disabled", "web login required", "please log in via"
    ];

    public ImapClient(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    // ─── Connect (SYNC) ───────────────────────────────────────────────

    public OperationResult Connect()
    {
        try
        {
            _client = new MailKit.Net.Imap.ImapClient();
            _client.Timeout = TIMEOUT_MS;
            _client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            var secureOption = _server.Socket == SocketType.SSL
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;

            _client.Connect(_server.Hostname, _server.Port, secureOption);
            return OperationResult.Ok;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
        {
            return OperationResult.HostNotFound;
        }
        catch (SocketException) { return OperationResult.Error; }
        catch (IOException) { return OperationResult.Error; }
        catch (TimeoutException) { return OperationResult.Error; }
        catch { return OperationResult.Error; }
    }

    // ─── Login (SYNC) — with Blocked/ServerDisabled detection ─────────

    public OperationResult Login()
    {
        if (_client == null || !_client.IsConnected) return OperationResult.Error;

        try
        {
            _client.Authenticate(_mailbox.Address, _mailbox.Password);
            return OperationResult.Ok;
        }
        catch (AuthenticationException ex)
        {
            var msg = ex.Message?.ToLowerInvariant() ?? "";

            // Check if blocked (like old MailException.IsBlocked)
            if (BlockedKeywords.Any(k => msg.Contains(k)))
                return OperationResult.Blocked;

            // Check if server disabled IMAP
            if (ServerDisabledKeywords.Any(k => msg.Contains(k)))
                return OperationResult.Blocked;

            return OperationResult.Bad;
        }
        catch (SocketException) { return OperationResult.Error; }
        catch (IOException) { return OperationResult.Error; }
        catch (TimeoutException) { return OperationResult.Error; }
        catch { return OperationResult.Error; }
    }

    // ─── Check INBOX Access (like old: CheckFolderAccess) ─────────────

    public OperationResult CheckInboxAccess()
    {
        try
        {
            var inbox = _client!.Inbox;
            inbox.Open(FolderAccess.ReadOnly);
            _currentFolder = inbox;
            return OperationResult.Ok;
        }
        catch { return OperationResult.Error; }
    }

    // ─── Select Folder (SYNC) ─────────────────────────────────────────

    public OperationResult SelectFolder(string folderName)
    {
        if (_client == null || !_client.IsAuthenticated) return OperationResult.Error;

        try
        {
            IMailFolder folder;
            if (folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            {
                folder = _client.Inbox;
            }
            else
            {
                try { folder = _client.GetFolder(folderName); }
                catch
                {
                    var personal = _client.GetFolder(_client.PersonalNamespaces[0]);
                    folder = personal.GetSubfolder(folderName);
                }
            }

            folder.Open(FolderAccess.ReadOnly);
            _currentFolder = folder;
            return OperationResult.Ok;
        }
        catch { return OperationResult.Error; }
    }

    // ─── List Folders (SYNC) ──────────────────────────────────────────

    public List<string> ListFolders()
    {
        if (_client == null || !_client.IsAuthenticated) return ["INBOX"];

        try
        {
            var personal = _client.GetFolder(_client.PersonalNamespaces[0]);
            var subfolders = personal.GetSubfolders(true);

            var result = new List<string> { "INBOX" };
            foreach (var f in subfolders)
            {
                if (!f.Name.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                    result.Add(f.FullName);
            }
            return result;
        }
        catch { return ["INBOX"]; }
    }

    // ─── Search (SYNC) ────────────────────────────────────────────────

    public List<UniqueId> Search(string criteria = "ALL")
    {
        if (_client == null || !_client.IsAuthenticated) return [];

        try
        {
            var folder = _currentFolder ?? _client.Inbox;
            if (!folder.IsOpen)
                folder.Open(FolderAccess.ReadOnly);

            SearchQuery query;
            if (criteria.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                query = SearchQuery.All;
            else
                query = BuildSearchQuery(criteria);

            var uids = folder.Search(query);
            return uids.ToList();
        }
        catch { return []; }
    }

    private static SearchQuery BuildSearchQuery(string criteria)
    {
        var parts = criteria.Split([" OR "], StringSplitOptions.RemoveEmptyEntries);
        SearchQuery? combined = null;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            SearchQuery? q = null;

            if (trimmed.StartsWith("SUBJECT ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.SubjectContains(ExtractValue(trimmed));
            else if (trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.FromContains(ExtractValue(trimmed));
            else if (trimmed.StartsWith("BODY ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.BodyContains(ExtractValue(trimmed));

            if (q != null)
                combined = combined == null ? q : combined.Or(q);
        }

        return combined ?? SearchQuery.All;
    }

    private static string ExtractValue(string s)
    {
        var start = s.IndexOf('"');
        var end = s.LastIndexOf('"');
        if (start >= 0 && end > start) return s[(start + 1)..end];
        var idx = s.IndexOf(' ');
        return idx >= 0 ? s[(idx + 1)..].Trim() : s;
    }

    // ─── Fetch Message (SYNC) — full MIME ─────────────────────────────

    public MimeMessage? FetchMessage(UniqueId uid)
    {
        if (_client == null || !_client.IsAuthenticated) return null;

        try
        {
            var folder = _currentFolder ?? _client.Inbox;
            if (!folder.IsOpen)
                folder.Open(FolderAccess.ReadOnly);

            return folder.GetMessage(uid);
        }
        catch { return null; }
    }

    // ─── Disconnect ───────────────────────────────────────────────────

    public void Disconnect()
    {
        try { if (_client?.IsConnected == true) _client.Disconnect(true); } catch { }
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        _currentFolder = null;
    }

    // ─── Async wrappers (IMailHandler) ────────────────────────────────

    public Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default) => Task.FromResult(Connect());
    public Task<OperationResult> LoginAsync(CancellationToken ct = default) => Task.FromResult(Login());
    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default) => Task.FromResult(SelectFolder(folder.Name));
    public Task SearchMessagesAsync(CancellationToken ct = default)
    {
        var kw = KeywordSettings.Instance;
        if (kw.Enabled && kw.HasKeywords)
        {
            var query = kw.BuildImapSearchQuery();
            Search(query);
        }
        return Task.CompletedTask;
    }
    public Task DisconnectAsync(CancellationToken ct = default) { Disconnect(); return Task.CompletedTask; }
}
