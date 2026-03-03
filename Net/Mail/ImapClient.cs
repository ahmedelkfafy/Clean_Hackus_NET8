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
    private const int TIMEOUT_MS = 10000;

    private TcpClient? _tcpClient;
    private Stream? _stream;
    private int _tagId = 0;

    public ImapClient(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    private string GetTag() => $"A{Interlocked.Increment(ref _tagId):000}";

    // ─── Raw IO (like original): write bytes + read line with timeout ──

    private void SendCommand(string command)
    {
        if (_stream == null) throw new IOException("Not connected");
        var bytes = Encoding.UTF8.GetBytes(command + "\r\n");
        _stream.Write(bytes, 0, bytes.Length);
        _stream.Flush();
    }

    private string? ReadLine(int timeoutMs = TIMEOUT_MS)
    {
        if (_stream == null) return null;

        var sb = new StringBuilder();
        var buffer = new byte[1];
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (_stream is NetworkStream ns && !ns.DataAvailable)
            {
                Thread.Sleep(10);
                continue;
            }

            int bytesRead;
            try { bytesRead = _stream.Read(buffer, 0, 1); }
            catch (IOException) { break; }

            if (bytesRead == 0) break;

            char c = (char)buffer[0];
            if (c == '\n')
            {
                var line = sb.ToString().TrimEnd('\r');
                return line;
            }
            sb.Append(c);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private string SendCommandGetResponse(string command)
    {
        SendCommand(command);
        return ReadLine() ?? "";
    }

    // ─── Connect ──────────────────────────────────────────────────────

    public Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default)
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = TIMEOUT_MS;
            _tcpClient.SendTimeout = TIMEOUT_MS;

            // Synchronous connect with timeout (like original)
            var connectTask = _tcpClient.ConnectAsync(_server.Hostname, _server.Port);
            if (!connectTask.Wait(TIMEOUT_MS, ct))
            {
                _tcpClient.Dispose();
                return Task.FromResult(OperationResult.Error);
            }

            _stream = _tcpClient.GetStream();

            if (_server.Socket == Models.Enums.SocketType.SSL)
            {
                var sslStream = new SslStream(_stream, false, (_, _, _, _) => true);
                sslStream.AuthenticateAsClient(_server.Hostname);
                _stream = sslStream;
            }

            var welcome = ReadLine();
            if (welcome?.Contains("OK") != true)
                return Task.FromResult(OperationResult.Error);

            return Task.FromResult(OperationResult.Ok);
        }
        catch
        {
            return Task.FromResult(OperationResult.Error);
        }
    }

    // ─── Login ────────────────────────────────────────────────────────

    public Task<OperationResult> LoginAsync(CancellationToken ct = default)
    {
        try
        {
            var tag = GetTag();
            SendCommand($"{tag} LOGIN \"{_mailbox.Address}\" \"{_mailbox.Password}\"");

            // Read all response lines until we get our tag response
            string? response;
            while ((response = ReadLine()) != null)
            {
                if (response.StartsWith(tag))
                {
                    if (response.Contains(" OK "))
                        return Task.FromResult(OperationResult.Ok);
                    if (response.Contains(" NO ") || response.Contains(" BAD "))
                        return Task.FromResult(OperationResult.Bad);
                    break;
                }
            }

            return Task.FromResult(OperationResult.Error);
        }
        catch
        {
            return Task.FromResult(OperationResult.Error);
        }
    }

    // ─── Select Folder ────────────────────────────────────────────────

    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default)
    {
        try
        {
            var tag = GetTag();
            SendCommand($"{tag} SELECT \"{folder.Name}\"");

            string? response;
            while ((response = ReadLine()) != null)
            {
                if (response.StartsWith(tag))
                    return Task.FromResult(response.Contains(" OK ") ? OperationResult.Ok : OperationResult.Error);
            }
            return Task.FromResult(OperationResult.Error);
        }
        catch
        {
            return Task.FromResult(OperationResult.Error);
        }
    }

    // ─── LIST folders ─────────────────────────────────────────────────

    public List<string> ListFolders()
    {
        try
        {
            var folders = new List<string>();
            var tag = GetTag();
            SendCommand($"{tag} LIST \"\" \"*\"");

            string? line;
            while ((line = ReadLine()) != null)
            {
                if (line.StartsWith(tag)) break;

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

    // ─── SEARCH ───────────────────────────────────────────────────────

    public List<int> Search(string criteria = "ALL")
    {
        try
        {
            var uids = new List<int>();
            var tag = GetTag();
            SendCommand($"{tag} SEARCH {criteria}");

            string? line;
            while ((line = ReadLine()) != null)
            {
                if (line.StartsWith("* SEARCH"))
                {
                    var parts = line["* SEARCH".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                        if (int.TryParse(p, out var uid)) uids.Add(uid);
                }
                if (line.StartsWith(tag)) break;
            }

            return uids;
        }
        catch { return []; }
    }

    // ─── FETCH message ────────────────────────────────────────────────

    public (string Subject, string From, string Date, string Body) FetchMessage(int msgId)
    {
        try
        {
            var tag = GetTag();
            SendCommand($"{tag} FETCH {msgId} (BODY[HEADER.FIELDS (SUBJECT FROM DATE)] BODY[TEXT])");

            var sb = new StringBuilder();
            string? line;
            while ((line = ReadLine()) != null)
            {
                sb.AppendLine(line);
                if (line.StartsWith(tag)) break;
            }

            var raw = sb.ToString();
            var subject = ExtractHeader(raw, "Subject:");
            var from = ExtractHeader(raw, "From:");
            var date = ExtractHeader(raw, "Date:");

            var bodyStart = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (bodyStart < 0) bodyStart = raw.IndexOf("\n\n", StringComparison.Ordinal);
            var body = bodyStart > 0 ? raw[(bodyStart + 4)..] : "";

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

    // ─── Async wrappers for interface ─────────────────────────────────

    public Task<List<string>> ListFoldersAsync(CancellationToken ct = default) => Task.FromResult(ListFolders());
    public Task<List<int>> SearchAsync(string criteria = "ALL", CancellationToken ct = default) => Task.FromResult(Search(criteria));
    public Task<(string Subject, string From, string Date, string Body)> FetchMessageAsync(int msgId, CancellationToken ct = default) => Task.FromResult(FetchMessage(msgId));

    // ─── NOOP ─────────────────────────────────────────────────────────

    public void Noop()
    {
        try
        {
            var tag = GetTag();
            SendCommand($"{tag} NOOP");
            string? line;
            while ((line = ReadLine(3000)) != null)
            {
                if (line.StartsWith(tag)) break;
            }
        }
        catch { }
    }

    // ─── IMailHandler interface ───────────────────────────────────────

    public Task SearchMessagesAsync(CancellationToken ct = default)
    {
        var settings = KeywordSettings.Instance;
        if (settings.Enabled && settings.HasKeywords)
        {
            SelectFolderAsync(new Folder { Name = "INBOX" }, ct);
            var query = settings.BuildImapSearchQuery();
            Search(query);
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        try { SendCommand($"{GetTag()} LOGOUT"); } catch { }
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }
}
