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
}
