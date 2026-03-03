using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Net.Mail;

public class ImapClient : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;

    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private int _tagId = 0;

    public ImapClient(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    private string GetTag() => $"A{Interlocked.Increment(ref _tagId):000}";

    public async Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken cancellationToken = default)
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = 15000;
            _tcpClient.SendTimeout = 15000;
            await _tcpClient.ConnectAsync(_server.Hostname, _server.Port, cancellationToken);

            _stream = _tcpClient.GetStream();

            if (_server.Socket == Models.Enums.SocketType.SSL)
            {
                var sslStream = new SslStream(_stream, false, (sender, cert, chain, err) => true);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _server.Hostname,
                    RemoteCertificateValidationCallback = (sender, cert, chain, err) => true
                }, cancellationToken);
                _stream = sslStream;
            }

            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

            var welcome = await _reader.ReadLineAsync(cancellationToken);
            if (welcome?.Contains("* OK") != true) return OperationResult.Error;

            return OperationResult.Ok;
        }
        catch (Exception)
        {
            return OperationResult.Error;
        }
    }

    public async Task<OperationResult> LoginAsync(CancellationToken cancellationToken = default)
    {
        if (_writer == null || _reader == null) return OperationResult.Error;

        try
        {
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} LOGIN \"{_mailbox.Address}\" \"{_mailbox.Password}\"");

            string? response;
            while ((response = await _reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (response.StartsWith(tag))
                {
                    if (response.Contains(" OK ")) return OperationResult.Ok;
                    if (response.Contains(" NO ")) return OperationResult.Bad;
                    break;
                }
            }

            return OperationResult.Error;
        }
        catch (Exception)
        {
            return OperationResult.Error;
        }
    }

    public async Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        if (_writer == null || _reader == null) return OperationResult.Error;

        try
        {
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} SELECT \"{folder.Name}\"");

            string? response;
            while ((response = await _reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (response.StartsWith(tag))
                {
                    return response.Contains(" OK ") ? OperationResult.Ok : OperationResult.Error;
                }
            }
            return OperationResult.Error;
        }
        catch (Exception)
        {
            return OperationResult.Error;
        }
    }

    // ─── Real IMAP LIST ───────────────────────────────────────────────

    /// <summary>List all mailbox folders via IMAP LIST command.</summary>
    public async Task<List<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return ["INBOX"];

        try
        {
            var folders = new List<string>();
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} LIST \"\" \"*\"");

            string? line;
            while ((line = await _reader.ReadLineAsync(ct)) != null)
            {
                if (line.StartsWith(tag)) break;

                // Parse: * LIST (\Flags) "/" "FolderName"
                if (line.StartsWith("* LIST"))
                {
                    var lastQuote = line.LastIndexOf('"');
                    if (lastQuote > 0)
                    {
                        var secondLastQuote = line.LastIndexOf('"', lastQuote - 1);
                        if (secondLastQuote >= 0)
                        {
                            var folderName = line[(secondLastQuote + 1)..lastQuote];
                            if (!string.IsNullOrWhiteSpace(folderName))
                                folders.Add(folderName);
                        }
                    }
                }
            }

            return folders.Count > 0 ? folders : ["INBOX"];
        }
        catch { return ["INBOX"]; }
    }

    // ─── Real IMAP SEARCH ─────────────────────────────────────────────

    /// <summary>Search messages in the selected folder. Returns message UIDs.</summary>
    public async Task<List<int>> SearchAsync(string criteria = "ALL", CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return [];

        try
        {
            var uids = new List<int>();
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} SEARCH {criteria}");

            string? line;
            while ((line = await _reader.ReadLineAsync(ct)) != null)
            {
                if (line.StartsWith("* SEARCH"))
                {
                    var parts = line["* SEARCH".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p, out var uid))
                            uids.Add(uid);
                    }
                }
                if (line.StartsWith(tag)) break;
            }

            return uids;
        }
        catch { return []; }
    }

    // ─── Real IMAP FETCH headers + body ───────────────────────────────

    /// <summary>Fetch envelope (Subject, From, Date) and body for a message.</summary>
    public async Task<(string Subject, string From, string Date, string Body)> FetchMessageAsync(int msgId, CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return ("", "", "", "");

        try
        {
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} FETCH {msgId} (BODY[HEADER.FIELDS (SUBJECT FROM DATE)] BODY[TEXT])");

            var sb = new StringBuilder();
            string? line;
            while ((line = await _reader.ReadLineAsync(ct)) != null)
            {
                sb.AppendLine(line);
                if (line.StartsWith(tag)) break;
            }

            var raw = sb.ToString();
            var subject = ExtractHeader(raw, "Subject:");
            var from = ExtractHeader(raw, "From:");
            var date = ExtractHeader(raw, "Date:");

            // Extract body text (everything after the blank line in BODY[TEXT])
            var bodyStart = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var body = bodyStart > 0 ? raw[(bodyStart + 4)..] : "";

            // Clean up IMAP artifacts from body
            var tagLine = body.LastIndexOf($"{tag} OK", StringComparison.Ordinal);
            if (tagLine > 0) body = body[..tagLine];
            body = body.Replace(")\r\n", "").Trim();

            return (subject, from, date, body);
        }
        catch { return ("", "", "", ""); }
    }

    private static string ExtractHeader(string raw, string header)
    {
        var idx = raw.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var endIdx = raw.IndexOf('\r', idx);
        if (endIdx < 0) endIdx = raw.IndexOf('\n', idx);
        if (endIdx < 0) return raw[idx..];
        return raw[(idx + header.Length)..endIdx].Trim();
    }

    // ─── IMAP NOOP (keep-alive) ───────────────────────────────────────

    /// <summary>Send NOOP to keep connection alive.</summary>
    public async Task NoopAsync(CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return;
        try
        {
            var tag = GetTag();
            await _writer.WriteLineAsync($"{tag} NOOP");
            string? line;
            while ((line = await _reader.ReadLineAsync(ct)) != null)
            {
                if (line.StartsWith(tag)) break;
            }
        }
        catch { }
    }

    // ─── Existing SearchMessagesAsync (interface) ─────────────────────

    public async Task SearchMessagesAsync(CancellationToken cancellationToken = default)
    {
        // Delegate to keyword search
        var settings = KeywordSettings.Instance;
        if (settings.Enabled && settings.HasKeywords)
        {
            var selectResult = await SelectFolderAsync(new Folder { Name = "INBOX" }, cancellationToken);
            if (selectResult == OperationResult.Ok)
            {
                var query = settings.BuildImapSearchQuery();
                await SearchAsync(query, cancellationToken);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_writer != null)
        {
            try { await _writer.WriteLineAsync($"{GetTag()} LOGOUT"); } catch { }
        }
        Dispose();
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}
