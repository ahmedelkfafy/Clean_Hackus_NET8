using System;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Net.Mail;

public interface IMailHandler : IDisposable
{
    Task<OperationResult> ConnectAsync(Proxy? proxy, CancellationToken cancellationToken = default);
    Task<OperationResult> LoginAsync(CancellationToken cancellationToken = default);
    Task SearchMessagesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SelectFolderAsync(Folder folder, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
