using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Clean_Hackus_NET8.Net.Mail;

public class ImapClient : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private const int TIMEOUT_MS = 10000;

    private MailKit.Net.Imap.ImapClient? _client;

    public ImapClient(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    // ─── Connect ──────────────────────────────────────────────────────

    public async Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default)
    {
        try
        {
            _client = new MailKit.Net.Imap.ImapClient();
            _client.Timeout = TIMEOUT_MS;
            _client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            var secureOption = _server.Socket == Models.Enums.SocketType.SSL
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            await _client.ConnectAsync(_server.Hostname, _server.Port, secureOption, cts.Token);
            return OperationResult.Ok;
        }
        catch (OperationCanceledException) { return OperationResult.Error; }
        catch { return OperationResult.Error; }
    }

    // ─── Login ────────────────────────────────────────────────────────

    public async Task<OperationResult> LoginAsync(CancellationToken ct = default)
    {
        if (_client == null || !_client.IsConnected) return OperationResult.Error;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            await _client.AuthenticateAsync(_mailbox.Address, _mailbox.Password, cts.Token);
            return OperationResult.Ok;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return OperationResult.Bad;
        }
        catch (OperationCanceledException) { return OperationResult.Error; }
        catch { return OperationResult.Error; }
    }

    // ─── Select Folder ────────────────────────────────────────────────

    public async Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default)
    {
        if (_client == null || !_client.IsAuthenticated) return OperationResult.Error;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            var mailFolder = folder.Name.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? _client.Inbox
                : await _client.GetFolderAsync(folder.Name, cts.Token);

            await mailFolder.OpenAsync(FolderAccess.ReadOnly, cts.Token);
            return OperationResult.Ok;
        }
        catch { return OperationResult.Error; }
    }

    // ─── LIST folders ─────────────────────────────────────────────────

    public async Task<List<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        if (_client == null || !_client.IsAuthenticated) return ["INBOX"];

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            var personal = _client.GetFolder(_client.PersonalNamespaces[0]);
            var subfolders = await personal.GetSubfoldersAsync(true, cts.Token);

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

    // ─── SEARCH ───────────────────────────────────────────────────────

    public async Task<List<int>> SearchAsync(string criteria = "ALL", CancellationToken ct = default)
    {
        if (_client == null || !_client.IsAuthenticated) return [];

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS * 3); // search can be slower

            var folder = _client.Inbox;
            if (!folder.IsOpen)
                await folder.OpenAsync(FolderAccess.ReadOnly, cts.Token);

            SearchQuery query;
            if (criteria.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = SearchQuery.All;
            }
            else
            {
                // Parse our custom criteria format into MailKit SearchQuery
                query = BuildSearchQuery(criteria);
            }

            var uids = await folder.SearchAsync(query, cts.Token);
            return uids.Select(u => u.Id).Cast<int>().ToList();
        }
        catch { return []; }
    }

    private static SearchQuery BuildSearchQuery(string criteria)
    {
        // Support formats: "SUBJECT \"word\"", "FROM \"word\"", "BODY \"word\""
        // and OR combinations
        var parts = criteria.Split(new[] { " OR " }, StringSplitOptions.RemoveEmptyEntries);

        SearchQuery? combined = null;
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            SearchQuery? q = null;

            if (trimmed.StartsWith("SUBJECT ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.SubjectContains(ExtractQuoted(trimmed));
            else if (trimmed.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.FromContains(ExtractQuoted(trimmed));
            else if (trimmed.StartsWith("BODY ", StringComparison.OrdinalIgnoreCase))
                q = SearchQuery.BodyContains(ExtractQuoted(trimmed));

            if (q != null)
                combined = combined == null ? q : combined.Or(q);
        }

        return combined ?? SearchQuery.All;
    }

    private static string ExtractQuoted(string s)
    {
        var start = s.IndexOf('"');
        var end = s.LastIndexOf('"');
        if (start >= 0 && end > start)
            return s[(start + 1)..end];
        // No quotes — take everything after the first space
        var spaceIdx = s.IndexOf(' ');
        return spaceIdx >= 0 ? s[(spaceIdx + 1)..].Trim() : s;
    }

    // ─── FETCH message ────────────────────────────────────────────────

    public async Task<(string Subject, string From, string Date, string Body)> FetchMessageAsync(int msgIndex, CancellationToken ct = default)
    {
        if (_client == null || !_client.IsAuthenticated) return ("", "", "", "");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            var folder = _client.Inbox;
            if (!folder.IsOpen)
                await folder.OpenAsync(FolderAccess.ReadOnly, cts.Token);

            var uid = new UniqueId((uint)msgIndex);
            var message = await folder.GetMessageAsync(uid, cts.Token);

            var subject = message.Subject ?? "(No Subject)";
            var from = message.From?.ToString() ?? "";
            var date = message.Date.ToString("yyyy-MM-dd HH:mm");
            var body = message.HtmlBody ?? message.TextBody ?? "";

            return (subject, from, date, body);
        }
        catch { return ("", "", "", ""); }
    }

    // ─── NOOP ─────────────────────────────────────────────────────────

    public async Task NoopAsync(CancellationToken ct = default)
    {
        try
        {
            if (_client?.IsConnected == true)
                await _client.NoOpAsync(ct);
        }
        catch { }
    }

    // ─── IMailHandler interface ───────────────────────────────────────

    public Task SearchMessagesAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            if (_client?.IsConnected == true)
                await _client.DisconnectAsync(true, ct);
        }
        catch { }
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }
}
