using System;
using System.Text.RegularExpressions;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Models;

public class Server
{
    public string Domain { get; set; }
    public string Hostname { get; set; }
    public int Port { get; set; } = 993;
    public ProtocolType Protocol { get; set; }
    public SocketType Socket { get; set; }

    public Server() { }

    public Server(string domain, string hostname, int port, ProtocolType protocol, SocketType socket)
    {
        Domain = domain;
        Hostname = hostname;
        Port = port;
        Protocol = protocol;
        Socket = socket;
    }

    public static Server? GetFromString(string line)
    {
        // Expected format: Domain;Hostname;IMAP|POP3;Port;SSL|Plain
        var match = Regex.Match(line, @"^([^;]+);([^;]+);(IMAP|POP3);(\d+);(SSL|Plain)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups[4].Value, out int port))
        {
            return new Server
            {
                Domain = match.Groups[1].Value,
                Hostname = match.Groups[2].Value,
                Port = port,
                Protocol = match.Groups[3].Value.Equals("IMAP", StringComparison.OrdinalIgnoreCase) ? ProtocolType.IMAP : ProtocolType.POP3,
                Socket = match.Groups[5].Value.Equals("SSL", StringComparison.OrdinalIgnoreCase) ? SocketType.SSL : SocketType.Plain
            };
        }

        return null;
    }

    public Server Clone()
    {
        return new Server
        {
            Domain = Domain,
            Hostname = Hostname,
            Port = Port,
            Protocol = Protocol,
            Socket = Socket
        };
    }

    public override bool Equals(object? obj)
    {
        if (obj is Server server)
        {
            return Domain == server.Domain &&
                   Hostname == server.Hostname &&
                   Port == server.Port &&
                   Protocol == server.Protocol &&
                   Socket == server.Socket;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Domain, Hostname, Port, Protocol, Socket);
    }
}
