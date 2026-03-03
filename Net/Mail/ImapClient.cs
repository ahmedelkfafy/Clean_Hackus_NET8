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

    public Task SearchMessagesAsync(CancellationToken cancellationToken = default)
    {
        // Core IMAP search logic implementation
        return Task.CompletedTask;
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
