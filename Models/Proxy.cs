using System;

namespace Clean_Hackus_NET8.Models;

public enum ProxyType
{
    HTTP,
    SOCKS4,
    SOCKS5
}

public class Proxy
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public ProxyType Type { get; set; } = ProxyType.HTTP;
    public bool UseAuthentication { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public Proxy() { }

    public Proxy(string host, int port, ProxyType type = ProxyType.HTTP)
    {
        Host = host;
        Port = port;
        Type = type;
    }

    public Proxy(string host, int port, string username, string password, ProxyType type = ProxyType.HTTP)
        : this(host, port, type)
    {
        UseAuthentication = true;
        Username = username;
        Password = password;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Proxy proxy)
            return Host == proxy.Host && Port == proxy.Port && Username == proxy.Username && Password == proxy.Password;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Host, Port);
    public override string ToString() => $"{Type}://{Host}:{Port}";
}
