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

    private ProxyManager() { }

    /// <summary>Round-robin proxy rotation (thread-safe).</summary>
    public Proxy? GetNextProxy()
    {
        lock (_locker)
        {
            if (_proxies.Count == 0) return null;
            var index = Interlocked.Increment(ref _currentIndex) % _proxies.Count;
            return _proxies[index];
        }
    }

    /// <summary>Random proxy selection.</summary>
    public Proxy? GetProxy()
    {
        lock (_locker)
        {
            if (_proxies.Count == 0) return null;
            return _proxies[Random.Shared.Next(_proxies.Count)];
        }
    }

    /// <summary>Load proxies from a file. Supports host:port and host:port:user:pass formats.</summary>
    public int LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var parsed = new List<Proxy>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(':');
            if (parts.Length < 2) continue;

            var host = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out int port)) continue;

            var proxy = new Proxy(host, port);

            if (parts.Length >= 4)
            {
                proxy = new Proxy(host, port, parts[2].Trim(), parts[3].Trim());
            }

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
