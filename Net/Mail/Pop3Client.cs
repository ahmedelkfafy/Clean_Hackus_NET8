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

public class Pop3Client : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private const int TIMEOUT_MS = 10000;

    private TcpClient? _tcpClient;
    private Stream? _stream;

    public Pop3Client(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    // ─── Raw IO with timeout (like original) ──────────────────────────

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
                return sb.ToString().TrimEnd('\r');
            sb.Append(c);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private string SendCommandGetResponse(string command)
    {
        SendCommand(command);
        return ReadLine() ?? "";
    }

    private void SendCommandCheckOK(string command)
    {
        var response = SendCommandGetResponse(command);
        if (!response.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
            throw new Exception(response);
    }

    // ─── Connect ──────────────────────────────────────────────────────

    public Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default)
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = TIMEOUT_MS;
            _tcpClient.SendTimeout = TIMEOUT_MS;

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
            if (welcome?.StartsWith("+OK") != true)
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
            SendCommandCheckOK($"USER {_mailbox.Address}");
            SendCommandCheckOK($"PASS {_mailbox.Password}");
            return Task.FromResult(OperationResult.Ok);
        }
        catch (Exception ex)
        {
            var msg = ex.Message.ToLower();
            if (msg.Contains("err") || msg.Contains("invalid") || msg.Contains("denied") || msg.Contains("authentication"))
                return Task.FromResult(OperationResult.Bad);
            return Task.FromResult(OperationResult.Error);
        }
    }

    // ─── STAT (message count) ─────────────────────────────────────────

    public int GetMessageCount()
    {
        try
        {
            var response = SendCommandGetResponse("STAT");
            if (response.StartsWith("+OK"))
            {
                var parts = response.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var count))
                    return count;
            }
        }
        catch { }
        return 0;
    }

    // ─── RETR (fetch message) ─────────────────────────────────────────

    public string RetrieveMessage(int msgNumber)
    {
        try
        {
            SendCommand($"RETR {msgNumber}");
            var firstLine = ReadLine();
            if (firstLine?.StartsWith("+OK") != true) return "";

            var sb = new StringBuilder();
            string? line;
            while ((line = ReadLine()) != null)
            {
                if (line == ".") break;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    /// <summary>Parse raw email headers.</summary>
    public static (string Subject, string From, string Date, string Body) ParseRawMessage(string raw)
    {
        var subject = ExtractHeader(raw, "Subject:");
        var from = ExtractHeader(raw, "From:");
        var date = ExtractHeader(raw, "Date:");

        var bodyIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (bodyIdx < 0) bodyIdx = raw.IndexOf("\n\n", StringComparison.Ordinal);
        var body = bodyIdx > 0 ? raw[(bodyIdx + (raw[bodyIdx] == '\r' ? 4 : 2))..] : "";

        return (subject, from, date, body);
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

    // ─── Async wrappers ───────────────────────────────────────────────

    public Task<int> GetMessageCountAsync(CancellationToken ct = default) => Task.FromResult(GetMessageCount());
    public Task<string> RetrieveMessageAsync(int msgNumber, CancellationToken ct = default) => Task.FromResult(RetrieveMessage(msgNumber));

    // ─── IMailHandler interface ───────────────────────────────────────

    public Task SearchMessagesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Ok); // POP3 has no folders

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        try { SendCommand("QUIT"); } catch { }
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
