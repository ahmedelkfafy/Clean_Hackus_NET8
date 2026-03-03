using System;
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
            // Note: In a full proxy implementation, the TCP client would connect through the Proxy.
            // For simplicity in this core engine skeleton, we connect directly.
            _tcpClient = new TcpClient();
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

    public Task SearchMessagesAsync(CancellationToken cancellationToken = default)
    {
        // Core POP3 search logic implementation
        return Task.CompletedTask;
    }

    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        // POP3 doesn't typically support IMAP-style folders, so we just return Ok.
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
