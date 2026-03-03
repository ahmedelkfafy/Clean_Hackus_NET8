using System;

namespace Clean_Hackus_NET8.Models;

public class Proxy
{
    public string Host { get; set; }
    public int Port { get; set; }
    public bool UseAuthentication { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }

    public Proxy() { }

    public Proxy(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public Proxy(string host, int port, string username, string password) : this(host, port)
    {
        UseAuthentication = true;
        Username = username;
        Password = password;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Proxy proxy)
        {
            return Host == proxy.Host &&
                   Port == proxy.Port &&
                   Username == proxy.Username &&
                   Password == proxy.Password;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Host, Port);
    }
}
