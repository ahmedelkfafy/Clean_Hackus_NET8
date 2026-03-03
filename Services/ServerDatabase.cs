using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Reads IMAP/POP3 server configurations from SQLite .db files.
/// DB schema: Domain (TEXT), Server (TEXT), Port (INT), Socket (INT where 0=SSL, 1=Plain)
/// </summary>
public class ServerDatabase
{
    private static readonly ServerDatabase _instance = new();
    public static ServerDatabase Instance => _instance;

    /// <summary>Domain → list of servers (IMAP from user DB, POP3 from discovered cache)</summary>
    private readonly ConcurrentDictionary<string, List<Server>> _imapServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Server>> _pop3Servers = new(StringComparer.OrdinalIgnoreCase);

    private string? _pop3CachePath;

    private ServerDatabase() { }

    public int ImapServerCount => _imapServers.Count;
    public int Pop3ServerCount => _pop3Servers.Count;

    /// <summary>
    /// Loads the user's IMAP server .db file (SQLite).
    /// </summary>
    public int LoadImapDatabase(string dbPath)
    {
        int count = 0;

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Domain, Server, Port, Socket FROM servers";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var domain = reader.GetString(0).TrimStart('-').Trim();
            var hostname = reader.GetString(1).Trim();
            var port = reader.GetInt32(2);
            var socketVal = reader.GetInt32(3);

            var server = new Server
            {
                Domain = domain,
                Hostname = hostname,
                Port = port,
                Protocol = ProtocolType.IMAP,
                Socket = socketVal == 0 ? SocketType.SSL : SocketType.Plain
            };

            _imapServers.AddOrUpdate(domain,
                _ => [server],
                (_, list) => { list.Add(server); return list; });
            count++;
        }

        return count;
    }

    /// <summary>
    /// Initialize and load cached POP3 discoveries from a local SQLite file.
    /// </summary>
    public void InitPop3Cache(string cachePath)
    {
        _pop3CachePath = cachePath;

        if (!File.Exists(cachePath)) return;

        using var connection = new SqliteConnection($"Data Source={cachePath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Domain, Server, Port, Socket FROM servers";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var domain = reader.GetString(0).Trim();
            var hostname = reader.GetString(1).Trim();
            var port = reader.GetInt32(2);
            var socketVal = reader.GetInt32(3);

            var server = new Server
            {
                Domain = domain,
                Hostname = hostname,
                Port = port,
                Protocol = ProtocolType.POP3,
                Socket = socketVal == 0 ? SocketType.SSL : SocketType.Plain
            };

            _pop3Servers.AddOrUpdate(domain,
                _ => [server],
                (_, list) => { list.Add(server); return list; });
        }
    }

    /// <summary>
    /// Saves a discovered POP3 server to the local cache DB.
    /// </summary>
    public void SavePop3ToCache(Server server)
    {
        _pop3Servers.AddOrUpdate(server.Domain,
            _ => [server],
            (_, list) => { list.Add(server); return list; });

        if (_pop3CachePath == null) return;

        using var connection = new SqliteConnection($"Data Source={_pop3CachePath}");
        connection.Open();

        using var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS servers (
                Domain TEXT NOT NULL,
                Server TEXT NOT NULL,
                Port INTEGER NOT NULL,
                Socket INTEGER NOT NULL
            )
            """;
        createCmd.ExecuteNonQuery();

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO servers (Domain, Server, Port, Socket) VALUES ($domain, $server, $port, $socket)";
        insertCmd.Parameters.AddWithValue("$domain", server.Domain);
        insertCmd.Parameters.AddWithValue("$server", server.Hostname);
        insertCmd.Parameters.AddWithValue("$port", server.Port);
        insertCmd.Parameters.AddWithValue("$socket", server.Socket == SocketType.SSL ? 0 : 1);
        insertCmd.ExecuteNonQuery();
    }

    /// <summary>Get IMAP servers for a domain.</summary>
    public List<Server>? GetImapServers(string domain)
        => _imapServers.TryGetValue(domain, out var servers) ? servers : null;

    /// <summary>Get POP3 servers for a domain.</summary>
    public List<Server>? GetPop3Servers(string domain)
        => _pop3Servers.TryGetValue(domain, out var servers) ? servers : null;

    /// <summary>Check if we have any server config (IMAP or POP3) for a domain.</summary>
    public bool HasAnyServer(string domain)
        => _imapServers.ContainsKey(domain) || _pop3Servers.ContainsKey(domain);
}
