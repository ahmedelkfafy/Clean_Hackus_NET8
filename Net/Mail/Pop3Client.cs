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

    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public Pop3Client(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

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
                await sslStream.AuthenticateAsClientAsync(_server.Hostname);
                _stream = sslStream;
            }

            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

            var welcome = await _reader.ReadLineAsync(cancellationToken);
            if (welcome?.StartsWith("+OK") != true)
                return OperationResult.Error;

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
            await _writer.WriteLineAsync($"USER {_mailbox.Address}");
            var userResponse = await _reader.ReadLineAsync(cancellationToken);
            if (userResponse?.StartsWith("+OK") != true) return OperationResult.Bad;

            await _writer.WriteLineAsync($"PASS {_mailbox.Password}");
            var passResponse = await _reader.ReadLineAsync(cancellationToken);
            if (passResponse?.StartsWith("+OK") != true) return OperationResult.Bad;

            return OperationResult.Ok;
        }
        catch (Exception)
        {
            return OperationResult.Error;
        }
    }

    // ─── POP3 STAT (get message count) ────────────────────────────────

    /// <summary>Get total message count in mailbox.</summary>
    public async Task<int> GetMessageCountAsync(CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return 0;
        try
        {
            await _writer.WriteLineAsync("STAT");
            var response = await _reader.ReadLineAsync(ct);
            // +OK 15 12345  (count bytes)
            if (response?.StartsWith("+OK") == true)
            {
                var parts = response.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var count))
                    return count;
            }
        }
        catch { }
        return 0;
    }

    // ─── POP3 RETR (fetch message) ────────────────────────────────────

    /// <summary>Fetch a message by its sequence number (1-based). Returns raw email text.</summary>
    public async Task<string> RetrieveMessageAsync(int msgNumber, CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return "";

        try
        {
            await _writer.WriteLineAsync($"RETR {msgNumber}");
            var firstLine = await _reader.ReadLineAsync(ct);
            if (firstLine?.StartsWith("+OK") != true) return "";

            var sb = new StringBuilder();
            string? line;
            while ((line = await _reader.ReadLineAsync(ct)) != null)
            {
                if (line == ".") break; // POP3 end-of-message marker
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    /// <summary>Parse raw email headers from a retrieved message.</summary>
    public static (string Subject, string From, string Date, string Body) ParseRawMessage(string raw)
    {
        var subject = ExtractHeader(raw, "Subject:");
        var from = ExtractHeader(raw, "From:");
        var date = ExtractHeader(raw, "Date:");

        // Body is after the first blank line
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

    // ─── POP3 NOOP (keep-alive) ───────────────────────────────────────

    /// <summary>Send NOOP to keep connection alive.</summary>
    public async Task NoopAsync(CancellationToken ct = default)
    {
        if (_writer == null || _reader == null) return;
        try
        {
            await _writer.WriteLineAsync("NOOP");
            await _reader.ReadLineAsync(ct); // consume +OK
        }
        catch { }
    }

    public Task SearchMessagesAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        // POP3 doesn't support folders
        return Task.FromResult(OperationResult.Ok);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_writer != null)
        {
            try { await _writer.WriteLineAsync("QUIT"); } catch { }
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
