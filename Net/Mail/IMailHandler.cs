using System;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Net.Mail;

/// <summary>
/// Common interface for mail protocol handlers (IMAP / POP3).
/// </summary>
public interface IMailHandler : IDisposable
{
    OperationResult Connect();
    OperationResult Login();
    void Disconnect();

    // Async methods used by MailHandler
    Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken ct = default);
    Task<OperationResult> LoginAsync(CancellationToken ct = default);
    Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken ct = default);
    Task SearchMessagesAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
