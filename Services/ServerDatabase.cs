using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Reads IMAP/POP3 server configurations from SQLite .db files.
/// Uses lazy queries instead of loading entire DB into memory.
/// </summary>
public class ServerDatabase
{
    private static readonly ServerDatabase _instance = new();
    public static ServerDatabase Instance => _instance;

    private readonly ConcurrentDictionary<string, List<Server>> _imapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Server>> _pop3Servers = new(StringComparer.OrdinalIgnoreCase);

    private string? _pop3CachePath;
    private string? _imapDbPath;
    private string? _imapTableName;
    private string? _colDomain, _colServer, _colPort, _colSocket;
    private int _totalImapCount;

    private ServerDatabase() { }

    public int ImapServerCount => _totalImapCount;
    public int Pop3ServerCount => _pop3Servers.Count;

    /// <summary>
    /// Loads the user's IMAP server .db file. Only reads schema + count, not all rows.
    /// </summary>
    public int LoadImapDatabase(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            connection.Open();

            var tableName = FindFirstTable(connection);
            if (tableName == null) return 0;

            var columns = GetColumnNames(connection, tableName);
            var domainCol = FindColumn(columns, "domain", "host_domain", "mail_domain", "email_domain");
            var serverCol = FindColumn(columns, "server", "hostname", "host", "imap_server", "address", "ip");
            var portCol = FindColumn(columns, "port", "imap_port", "server_port");
            var socketCol = FindColumn(columns, "socket", "ssl", "socket_type", "security", "encryption", "tls");

            if (domainCol == null || serverCol == null || portCol == null) return 0;

            // Store schema info for lazy queries
            _imapDbPath = dbPath;
            _imapTableName = tableName;
            _colDomain = domainCol;
            _colServer = serverCol;
            _colPort = portCol;
            _colSocket = socketCol;

            // Get count only
            using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
            _totalImapCount += Convert.ToInt32(countCmd.ExecuteScalar());

            return _totalImapCount;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading: {Path.GetFileName(dbPath)}\n{ex.Message}",
                "DB Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 0;
        }
    }

    /// <summary>Get IMAP servers for a domain. Queries DB lazily and caches results.</summary>
    public List<Server>? GetImapServers(string domain)
    {
        if (_imapCache.TryGetValue(domain, out var cached))
            return cached.Count > 0 ? cached : null;

        if (_imapDbPath == null || _imapTableName == null) return null;

        // Query DB for this specific domain
        var servers = new List<Server>();
        try
        {
            using var conn = new SqliteConnection($"Data Source={_imapDbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = _colSocket != null
                ? $"SELECT \"{_colServer}\", \"{_colPort}\", \"{_colSocket}\" FROM \"{_imapTableName}\" WHERE LOWER(\"{_colDomain}\") = $domain"
                : $"SELECT \"{_colServer}\", \"{_colPort}\" FROM \"{_imapTableName}\" WHERE LOWER(\"{_colDomain}\") = $domain";
            cmd.Parameters.AddWithValue("$domain", domain.ToLowerInvariant());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                try
                {
                    var hostname = reader.GetString(0).Trim();
                    var port = reader.GetInt32(1);
                    var socket = _colSocket != null
                        ? ParseSocketType(reader.GetValue(2))
                        : (port == 993 || port == 995 ? SocketType.SSL : SocketType.Plain);

                    if (!string.IsNullOrEmpty(hostname))
                    {
                        servers.Add(new Server
                        {
                            Domain = domain,
                            Hostname = hostname,
                            Port = port,
                            Protocol = ProtocolType.IMAP,
                            Socket = socket
                        });
                    }
                }
                catch { }
            }
        }
        catch { }

        _imapCache[domain] = servers;
        return servers.Count > 0 ? servers : null;
    }

    private static SocketType ParseSocketType(object value)
    {
        if (value is int intVal) return intVal == 0 ? SocketType.SSL : SocketType.Plain;
        if (value is long longVal) return longVal == 0 ? SocketType.SSL : SocketType.Plain;
        var str = value?.ToString()?.Trim() ?? "";
        return str.Equals("SSL", StringComparison.OrdinalIgnoreCase) || str == "0"
            ? SocketType.SSL : SocketType.Plain;
    }

    private static string? FindFirstTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name LIMIT 1";
        return cmd.ExecuteScalar()?.ToString();
    }

    private static List<string> GetColumnNames(SqliteConnection conn, string tableName)
    {
        var columns = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static string? FindColumn(List<string> columns, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var match = columns.Find(x => x.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        foreach (var c in candidates)
        {
            var match = columns.Find(x => x.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    // ─── POP3 Cache ───────────────────────────────────────────────────

    public void InitPop3Cache(string cachePath)
    {
        _pop3CachePath = cachePath;
        if (!File.Exists(cachePath)) return;

        try
        {
            using var connection = new SqliteConnection($"Data Source={cachePath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Domain, Server, Port, Socket FROM servers";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var domain = reader.GetString(0).Trim();
                var server = new Server
                {
                    Domain = domain,
                    Hostname = reader.GetString(1).Trim(),
                    Port = reader.GetInt32(2),
                    Protocol = ProtocolType.POP3,
                    Socket = reader.GetInt32(3) == 0 ? SocketType.SSL : SocketType.Plain
                };
                _pop3Servers.AddOrUpdate(domain, _ => [server], (_, list) => { list.Add(server); return list; });
            }
        }
        catch { }
    }

    public void SavePop3ToCache(Server server)
    {
        _pop3Servers.AddOrUpdate(server.Domain, _ => [server], (_, list) => { list.Add(server); return list; });
        if (_pop3CachePath == null) return;

        try
        {
            using var conn = new SqliteConnection($"Data Source={_pop3CachePath}");
            conn.Open();
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = "CREATE TABLE IF NOT EXISTS servers (Domain TEXT NOT NULL, Server TEXT NOT NULL, Port INTEGER NOT NULL, Socket INTEGER NOT NULL)";
            createCmd.ExecuteNonQuery();
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO servers (Domain, Server, Port, Socket) VALUES ($d, $s, $p, $sk)";
            insertCmd.Parameters.AddWithValue("$d", server.Domain);
            insertCmd.Parameters.AddWithValue("$s", server.Hostname);
            insertCmd.Parameters.AddWithValue("$p", server.Port);
            insertCmd.Parameters.AddWithValue("$sk", server.Socket == SocketType.SSL ? 0 : 1);
            insertCmd.ExecuteNonQuery();
        }
        catch { }
    }

    public List<Server>? GetPop3Servers(string domain)
        => _pop3Servers.TryGetValue(domain, out var servers) ? servers : null;

    public bool HasAnyServer(string domain)
        => _imapCache.ContainsKey(domain) || _pop3Servers.ContainsKey(domain);
}
