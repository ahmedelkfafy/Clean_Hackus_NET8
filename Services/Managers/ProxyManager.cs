using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Clean_Hackus_NET8.Models;

namespace Clean_Hackus_NET8.Services.Managers;

public class ProxyManager
{
    private static readonly ProxyManager _instance = new();
    public static ProxyManager Instance => _instance;

    private readonly object _locker = new();
    private List<Proxy> _proxies = [];
    private int _currentIndex = 0;

    /// <summary>Whether to use proxies for connections.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default proxy type when loading from file.</summary>
    public ProxyType DefaultType { get; set; } = ProxyType.HTTP;

    private ProxyManager() { }

    /// <summary>Round-robin proxy rotation (thread-safe). Returns null if proxies disabled or empty.</summary>
    public Proxy? GetNextProxy()
    {
        if (!Enabled) return null;

        lock (_locker)
        {
            if (_proxies.Count == 0) return null;
            var index = Interlocked.Increment(ref _currentIndex) % _proxies.Count;
            return _proxies[index];
        }
    }

    public Proxy? GetProxy()
    {
        if (!Enabled) return null;
        lock (_locker)
        {
            if (_proxies.Count == 0) return null;
            return _proxies[Random.Shared.Next(_proxies.Count)];
        }
    }

    /// <summary>
    /// Load proxies from file. Supports formats:
    /// host:port | host:port:user:pass | type://host:port | type://host:port:user:pass
    /// </summary>
    public int LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var parsed = new List<Proxy>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var proxy = ParseProxyLine(line);
            if (proxy != null)
                parsed.Add(proxy);
        }

        lock (_locker)
        {
            _proxies = parsed;
            _currentIndex = 0;
            StatisticsManager.Instance.LoadedProxy = _proxies.Count;
        }

        return parsed.Count;
    }

    private Proxy? ParseProxyLine(string line)
    {
        var type = DefaultType;

        // Check for type:// prefix
        if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.HTTP;
            line = line[7..];
        }
        else if (line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.HTTP;
            line = line[8..];
        }
        else if (line.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.SOCKS4;
            line = line[9..];
        }
        else if (line.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.SOCKS5;
            line = line[9..];
        }

        var parts = line.Split(':');
        if (parts.Length < 2) return null;

        var host = parts[0].Trim();
        if (!int.TryParse(parts[1].Trim(), out int port)) return null;

        if (parts.Length >= 4)
            return new Proxy(host, port, parts[2].Trim(), parts[3].Trim(), type);

        return new Proxy(host, port, type);
    }

    public void UploadProxies(IEnumerable<Proxy> proxies)
    {
        lock (_locker)
        {
            _proxies = proxies.ToList();
            _currentIndex = 0;
            StatisticsManager.Instance.LoadedProxy = _proxies.Count;
        }
    }

    public int Count() => _proxies.Count;
    public bool Any() => _proxies.Count > 0;
}
