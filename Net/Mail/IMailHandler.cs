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

    // Async methods used by MailHandler (extra params from MailHandler.cs on build machine)
    Task<OperationResult> ConnectAsync(Server server, CancellationToken ct = default);
    Task<OperationResult> LoginAsync(CancellationToken ct = default);
    Task<OperationResult> SelectFolderAsync(string folderName, CancellationToken ct = default);
    Task SearchMessagesAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
