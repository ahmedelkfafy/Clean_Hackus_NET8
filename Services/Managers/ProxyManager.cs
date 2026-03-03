using System;
using System.Collections.Generic;
using System.Linq;
using Clean_Hackus_NET8.Models;

namespace Clean_Hackus_NET8.Services.Managers;

public class ProxyManager
{
    private static readonly ProxyManager _instance = new();
    public static ProxyManager Instance => _instance;

    private readonly object _locker = new();
    private readonly Random _random = new();
    private List<Proxy> _proxies = new();

    private ProxyManager() { }

    public Proxy? GetProxy()
    {
        lock (_locker)
        {
            if (_proxies == null || _proxies.Count == 0)
            {
                return null;
            }

            return _proxies.ElementAtOrDefault(_random.Next(_proxies.Count));
        }
    }

    public void UploadProxies(IEnumerable<Proxy> proxies)
    {
        if (proxies != null && proxies.Any())
        {
            lock (_locker)
            {
                _proxies = proxies.ToList();
                StatisticsManager.Instance.LoadedProxy = _proxies.Count;
            }
        }
    }

    public int Count() => _proxies?.Count ?? 0;

    public bool Any() => _proxies?.Any() == true;
}
