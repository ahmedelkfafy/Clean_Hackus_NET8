using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using SocketType = Clean_Hackus_NET8.Models.Enums.SocketType;
using MailKit.Security;
using MimeKit;

namespace Clean_Hackus_NET8.Net.Mail;

/// <summary>
/// POP3 client — MailKit SYNC API only.
/// Blocked detection, HostNotFound, proper error classification.
/// </summary>
public class Pop3Client : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private const int TIMEOUT_MS = 15000;

    private MailKit.Net.Pop3.Pop3Client? _client;

    private static readonly string[] BlockedKeywords = [
        "temporarily blocked", "temporary block", "too many", "rate limit",
        "try again later", "account disabled", "account locked", "suspended",
        "login denied", "overquota"
    ];

    public Pop3Client(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    public OperationResult Connect()
    {
        try
        {
            _client = new MailKit.Net.Pop3.Pop3Client();
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
            if (BlockedKeywords.Any(k => msg.Contains(k)))
                return OperationResult.Blocked;
            return OperationResult.Bad;
        }
        catch (SocketException) { return OperationResult.Error; }
        catch (IOException) { return OperationResult.Error; }
        catch (TimeoutException) { return OperationResult.Error; }
        catch { return OperationResult.Error; }
    }

    public int GetMessageCount()
    {
        try { return _client?.Count ?? 0; } catch { return 0; }
    }

    public MimeMessage? GetMessage(int index)
    {
        if (_client == null || !_client.IsAuthenticated) return null;
        try { return _client.GetMessage(index); } catch { return null; }
    }

    public void Disconnect()
    {
        try { if (_client?.IsConnected == true) _client.Disconnect(true); } catch { }
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    // ─── Async wrappers (IMailHandler) ────────────────────────────────

    public Task<OperationResult> ConnectAsync() => Task.FromResult(Connect());
    public Task<OperationResult> LoginAsync() => Task.FromResult(Login());
    public Task<OperationResult> SelectFolderAsync(string folderName) => Task.FromResult(OperationResult.Ok);
    public Task SearchMessagesAsync() => Task.CompletedTask;
    public Task DisconnectAsync() { Disconnect(); return Task.CompletedTask; }
}
