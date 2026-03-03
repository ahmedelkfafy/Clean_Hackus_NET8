using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;

namespace Clean_Hackus_NET8.Net.Mail;

public class Pop3Client : IMailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private const int TIMEOUT_MS = 10000;

    private MailKit.Net.Pop3.Pop3Client? _client;

    public Pop3Client(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    // ─── Connect ──────────────────────────────────────────────────────

    public async Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default)
    {
        try
        {
            _client = new MailKit.Net.Pop3.Pop3Client();
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

    // ─── Get Message Count ────────────────────────────────────────────

    public int GetMessageCount()
    {
        try { return _client?.Count ?? 0; }
        catch { return 0; }
    }

    public Task<int> GetMessageCountAsync(CancellationToken ct = default)
        => Task.FromResult(GetMessageCount());

    // ─── Get Message ──────────────────────────────────────────────────

    public async Task<(string Subject, string From, string Date, string Body)> GetMessageAsync(int index, CancellationToken ct = default)
    {
        if (_client == null || !_client.IsAuthenticated) return ("", "", "", "");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TIMEOUT_MS);

            var message = await _client.GetMessageAsync(index, cts.Token);

            var subject = message.Subject ?? "(No Subject)";
            var from = message.From?.ToString() ?? "";
            var date = message.Date.ToString("yyyy-MM-dd HH:mm");
            var body = message.HtmlBody ?? message.TextBody ?? "";

            return (subject, from, date, body);
        }
        catch { return ("", "", "", ""); }
    }

    // ─── IMailHandler interface ───────────────────────────────────────

    public Task SearchMessagesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Ok); // POP3 has no folders

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
