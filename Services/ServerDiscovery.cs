using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DnsClient;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Auto-discovers POP3 and IMAP server configurations for domains.
/// Tries methods in order: ISPDB → AutoDiscover → AutoConfig → Well-Known → Heuristics → MX Records.
/// </summary>
public class ServerDiscovery
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly LookupClient _dns = new();

    // ─── POP3 Discovery ───────────────────────────────────────────────

    /// <summary>
    /// Discover POP3 server for a domain. Returns null if all methods fail.
    /// </summary>
    public static async Task<Server?> DiscoverPop3Async(string domain, CancellationToken ct = default)
    {
        // 0. Check local cache first (fastest)
        var cached = ServerDatabase.Instance.GetPop3Servers(domain);
        if (cached != null && cached.Count > 0)
            return cached[0];

        // 1. ISPDB (Mozilla Thunderbird)
        var server = await TryIspdbAsync(domain, "pop3", ProtocolType.POP3, ct);
        if (server != null) { ServerDatabase.Instance.SavePop3ToCache(server); return server; }

        // 2. AutoDiscover
        server = await TryAutoDiscoverAsync(domain, "POP3", ProtocolType.POP3, ct);
        if (server != null) { ServerDatabase.Instance.SavePop3ToCache(server); return server; }

        // 3. AutoConfig
        server = await TryAutoConfigAsync(domain, "pop3", ProtocolType.POP3, ct);
        if (server != null) { ServerDatabase.Instance.SavePop3ToCache(server); return server; }

        // 4. Well-Known URI
        server = await TryWellKnownAsync(domain, "pop3", ProtocolType.POP3, ct);
        if (server != null) { ServerDatabase.Instance.SavePop3ToCache(server); return server; }

        // 5. Heuristics
        server = await TryHeuristicsAsync(domain, ProtocolType.POP3, ct);
        if (server != null) { ServerDatabase.Instance.SavePop3ToCache(server); return server; }

        // 6. MX Records
        server = await TryMxRecordsAsync(domain, ProtocolType.POP3, ct);
        if (server != null) ServerDatabase.Instance.SavePop3ToCache(server);
        return server;
    }

    // ─── IMAP Discovery ───────────────────────────────────────────────

    /// <summary>
    /// Discover IMAP server for a domain. Returns null if all methods fail.
    /// </summary>
    public static async Task<Server?> DiscoverImapAsync(string domain, CancellationToken ct = default)
    {
        // 0. Check DB cache first
        var cached = ServerDatabase.Instance.GetImapServers(domain);
        if (cached != null && cached.Count > 0)
            return cached[0];

        // 1. ISPDB (Mozilla Thunderbird)
        var server = await TryIspdbAsync(domain, "imap", ProtocolType.IMAP, ct);
        if (server != null) return server;

        // 2. AutoDiscover
        server = await TryAutoDiscoverAsync(domain, "IMAP", ProtocolType.IMAP, ct);
        if (server != null) return server;

        // 3. AutoConfig
        server = await TryAutoConfigAsync(domain, "imap", ProtocolType.IMAP, ct);
        if (server != null) return server;

        // 4. Well-Known URI
        server = await TryWellKnownAsync(domain, "imap", ProtocolType.IMAP, ct);
        if (server != null) return server;

        // 5. Heuristics
        server = await TryHeuristicsAsync(domain, ProtocolType.IMAP, ct);
        if (server != null) return server;

        // 6. MX Records
        server = await TryMxRecordsAsync(domain, ProtocolType.IMAP, ct);
        return server;
    }

    // ─── Shared discovery methods ─────────────────────────────────────

    private static async Task<Server?> TryIspdbAsync(string domain, string protocolFilter, ProtocolType proto, CancellationToken ct)
    {
        try
        {
            var url = $"https://autoconfig.thunderbird.net/v1.1/{domain}";
            var xml = await _http.GetStringAsync(url, ct);
            return ParseAutoConfigXml(xml, domain, protocolFilter, proto);
        }
        catch { return null; }
    }

    private static async Task<Server?> TryAutoDiscoverAsync(string domain, string protocolFilter, ProtocolType proto, CancellationToken ct)
    {
        var urls = new[]
        {
            $"https://autodiscover.{domain}/autodiscover/autodiscover.xml",
            $"https://{domain}/autodiscover/autodiscover.xml"
        };

        foreach (var url in urls)
        {
            try
            {
                var xml = await _http.GetStringAsync(url, ct);
                return ParseAutoDiscoverXml(xml, domain, protocolFilter, proto);
            }
            catch { }
        }
        return null;
    }

    private static async Task<Server?> TryAutoConfigAsync(string domain, string protocolFilter, ProtocolType proto, CancellationToken ct)
    {
        try
        {
            var url = $"https://autoconfig.{domain}/mail/config-v1.1.xml";
            var xml = await _http.GetStringAsync(url, ct);
            return ParseAutoConfigXml(xml, domain, protocolFilter, proto);
        }
        catch { return null; }
    }

    private static async Task<Server?> TryWellKnownAsync(string domain, string protocolFilter, ProtocolType proto, CancellationToken ct)
    {
        try
        {
            var url = $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml";
            var xml = await _http.GetStringAsync(url, ct);
            return ParseAutoConfigXml(xml, domain, protocolFilter, proto);
        }
        catch { return null; }
    }

    private static async Task<Server?> TryHeuristicsAsync(string domain, ProtocolType proto, CancellationToken ct)
    {
        var prefixes = proto == ProtocolType.POP3
            ? new[] { "pop3", "pop", "mail" }
            : new[] { "imap", "mail" };
        var portConfigs = proto == ProtocolType.POP3
            ? new[] { (995, SocketType.SSL), (110, SocketType.Plain) }
            : new[] { (993, SocketType.SSL), (143, SocketType.Plain) };

        foreach (var prefix in prefixes)
        {
            var hostname = $"{prefix}.{domain}";
            foreach (var (port, socket) in portConfigs)
            {
                if (await TestConnectionAsync(hostname, port, ct))
                {
                    return new Server
                    {
                        Domain = domain,
                        Hostname = hostname,
                        Port = port,
                        Protocol = proto,
                        Socket = socket
                    };
                }
            }
        }
        return null;
    }

    private static async Task<Server?> TryMxRecordsAsync(string domain, ProtocolType proto, CancellationToken ct)
    {
        try
        {
            var result = await _dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            var mxRecords = result.Answers.MxRecords().OrderBy(mx => mx.Preference);

            var prefix = proto == ProtocolType.POP3 ? "pop" : "imap";
            var portConfigs = proto == ProtocolType.POP3
                ? new[] { (995, SocketType.SSL), (110, SocketType.Plain) }
                : new[] { (993, SocketType.SSL), (143, SocketType.Plain) };

            foreach (var mx in mxRecords)
            {
                var mxHost = mx.Exchange.Value.TrimEnd('.');

                // Try the MX host directly first
                foreach (var (port, socket) in portConfigs)
                {
                    if (await TestConnectionAsync(mxHost, port, ct))
                    {
                        return new Server
                        {
                            Domain = domain,
                            Hostname = mxHost,
                            Port = port,
                            Protocol = proto,
                            Socket = socket
                        };
                    }
                }

                // Try {prefix}.{base domain from MX}
                var parts = mxHost.Split('.');
                if (parts.Length >= 2)
                {
                    var baseDomain = string.Join('.', parts[^2..]);
                    var derivedHost = $"{prefix}.{baseDomain}";
                    foreach (var (port, socket) in portConfigs)
                    {
                        if (await TestConnectionAsync(derivedHost, port, ct))
                        {
                            return new Server
                            {
                                Domain = domain,
                                Hostname = derivedHost,
                                Port = port,
                                Protocol = proto,
                                Socket = socket
                            };
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // ─── XML Parsers ──────────────────────────────────────────────────

    private static Server? ParseAutoConfigXml(string xml, string domain, string protocolType, ProtocolType proto)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var servers = doc.Descendants(ns + "incomingServer")
                .Where(s => s.Attribute("type")?.Value?.Equals(protocolType, StringComparison.OrdinalIgnoreCase) == true);

            foreach (var srv in servers)
            {
                var hostname = srv.Element(ns + "hostname")?.Value;
                var portStr = srv.Element(ns + "port")?.Value;
                var socketType = srv.Element(ns + "socketType")?.Value;

                if (!string.IsNullOrEmpty(hostname) && int.TryParse(portStr, out int port))
                {
                    var isSSL = socketType?.Contains("SSL", StringComparison.OrdinalIgnoreCase) == true
                             || socketType?.Contains("TLS", StringComparison.OrdinalIgnoreCase) == true;

                    return new Server
                    {
                        Domain = domain,
                        Hostname = hostname,
                        Port = port,
                        Protocol = proto,
                        Socket = isSSL ? SocketType.SSL : SocketType.Plain
                    };
                }
            }
        }
        catch { }
        return null;
    }

    private static Server? ParseAutoDiscoverXml(string xml, string domain, string protocolFilter, ProtocolType proto)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var protocols = doc.Descendants().Where(e => e.Name.LocalName == "Protocol");

            foreach (var p in protocols)
            {
                var type = p.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value;
                if (type?.Equals(protocolFilter, StringComparison.OrdinalIgnoreCase) != true) continue;

                var server = p.Elements().FirstOrDefault(e => e.Name.LocalName == "Server")?.Value;
                var portStr = p.Elements().FirstOrDefault(e => e.Name.LocalName == "Port")?.Value;
                var ssl = p.Elements().FirstOrDefault(e => e.Name.LocalName == "SSL")?.Value;

                if (!string.IsNullOrEmpty(server) && int.TryParse(portStr, out int port))
                {
                    return new Server
                    {
                        Domain = domain,
                        Hostname = server,
                        Port = port,
                        Protocol = proto,
                        Socket = ssl?.Equals("on", StringComparison.OrdinalIgnoreCase) == true
                            ? SocketType.SSL : SocketType.Plain
                    };
                }
            }
        }
        catch { }
        return null;
    }

    // ─── Connection test ──────────────────────────────────────────────

    private static async Task<bool> TestConnectionAsync(string hostname, int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(3000);
            await tcp.ConnectAsync(hostname, port, cts.Token);
            return true;
        }
        catch { return false; }
    }
}
