using System;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using Clean_Hackus_NET8.Services.Managers;

namespace Clean_Hackus_NET8.Net.Mail;

public class MailHandler
{
    private readonly Mailbox _mailbox;
    private readonly Server _server;
    private IMailHandler? _mailClient;

    public MailHandler(Mailbox mailbox, Server server)
    {
        _mailbox = mailbox;
        _server = server;
    }

    public async Task<OperationResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (_server.Protocol == ProtocolType.IMAP)
        {
            _mailClient = new ImapClient(_mailbox, _server);
        }
        else if (_server.Protocol == ProtocolType.POP3)
        {
            _mailClient = new Pop3Client(_mailbox, _server);
        }
        else
        {
            return OperationResult.Error;
        }

        try
        {
            var proxy = ProxyManager.Instance.GetProxy();
            var connectResult = await _mailClient.ConnectAsync(proxy, cancellationToken);
            
            if (connectResult != OperationResult.Ok)
            {
                return connectResult;
            }

            var loginResult = await _mailClient.LoginAsync(cancellationToken);
            if (loginResult != OperationResult.Ok)
            {
                return loginResult;
            }

            // Assume Settings check and Folder selection happens here if needed.
            await _mailClient.SelectFolderAsync(new Folder("INBOX"), cancellationToken);
            await _mailClient.SearchMessagesAsync(cancellationToken);

            StatisticsManager.Instance.Increment(OperationResult.Ok);
            return OperationResult.Ok;
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Error;
        }
        catch (Exception)
        {
            StatisticsManager.Instance.Increment(OperationResult.Error);
            return OperationResult.Error;
        }
        finally
        {
            if (_mailClient != null)
            {
                await _mailClient.DisconnectAsync(CancellationToken.None);
            }
        }
    }
}
