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
/// Auto-detects table and column names.
/// </summary>
public class ServerDatabase
{
    private static readonly ServerDatabase _instance = new();
    public static ServerDatabase Instance => _instance;

    private readonly ConcurrentDictionary<string, List<Server>> _imapServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Server>> _pop3Servers = new(StringComparer.OrdinalIgnoreCase);

    private string? _pop3CachePath;

    private ServerDatabase() { }

    public int ImapServerCount => _imapServers.Count;
    public int Pop3ServerCount => _pop3Servers.Count;

    /// <summary>
    /// Loads the user's IMAP server .db file (SQLite).
    /// Auto-detects table and column names.
    /// </summary>
    public int LoadImapDatabase(string dbPath)
    {
        int count = 0;

        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            connection.Open();

            // Step 1: Find the first table in the database
            var tableName = FindFirstTable(connection);
            if (tableName == null)
                throw new Exception($"No tables found in database: {Path.GetFileName(dbPath)}");

            // Step 2: Get column names from that table
            var columns = GetColumnNames(connection, tableName);

            // Step 3: Map columns intelligently
            var domainCol = FindColumn(columns, "domain", "host_domain", "mail_domain", "email_domain");
            var serverCol = FindColumn(columns, "server", "hostname", "host", "imap_server", "address", "ip");
            var portCol = FindColumn(columns, "port", "imap_port", "server_port");
            var socketCol = FindColumn(columns, "socket", "ssl", "socket_type", "security", "encryption", "tls");

            if (domainCol == null || serverCol == null || portCol == null)
                throw new Exception($"Could not find required columns (domain, server, port) in table '{tableName}'. Found columns: {string.Join(", ", columns)}");

            // Build query — socket column is optional
            var query = socketCol != null
                ? $"SELECT \"{domainCol}\", \"{serverCol}\", \"{portCol}\", \"{socketCol}\" FROM \"{tableName}\""
                : $"SELECT \"{domainCol}\", \"{serverCol}\", \"{portCol}\" FROM \"{tableName}\"";

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                try
                {
                    var domain = reader.GetString(0).TrimStart('-').Trim().ToLowerInvariant();
                    var hostname = reader.GetString(1).Trim();
                    var port = reader.GetInt32(2);

                    // Determine socket type
                    var socket = SocketType.SSL; // Default to SSL
                    if (socketCol != null)
                    {
                        var socketVal = reader.GetValue(3);
                        socket = ParseSocketType(socketVal);
                    }
                    else
                    {
                        // Infer from port
                        socket = (port == 993 || port == 995) ? SocketType.SSL : SocketType.Plain;
                    }

                    if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(hostname)) continue;

                    var server = new Server
                    {
                        Domain = domain,
                        Hostname = hostname,
                        Port = port,
                        Protocol = ProtocolType.IMAP,
                        Socket = socket
                    };

                    _imapServers.AddOrUpdate(domain,
                        _ => [server],
                        (_, list) => { list.Add(server); return list; });
                    count++;
                }
                catch { /* skip bad rows */ }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading server database:\n{Path.GetFileName(dbPath)}\n\n{ex.Message}",
                "Server DB Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return count;
    }

    private static SocketType ParseSocketType(object value)
    {
        if (value is int intVal)
            return intVal == 0 ? SocketType.SSL : SocketType.Plain;

        if (value is long longVal)
            return longVal == 0 ? SocketType.SSL : SocketType.Plain;

        var str = value?.ToString()?.Trim() ?? "";
        if (str.Equals("SSL", StringComparison.OrdinalIgnoreCase) || str == "0")
            return SocketType.SSL;

        return SocketType.Plain;
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
        while (reader.Read())
        {
            columns.Add(reader.GetString(1)); // column name is at index 1
        }
        return columns;
    }

    private static string? FindColumn(List<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = columns.Find(c => c.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        // Fuzzy match: check if any column CONTAINS the first candidate
        foreach (var candidate in candidates)
        {
            var match = columns.Find(c => c.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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
        catch { }
    }

    public void SavePop3ToCache(Server server)
    {
        _pop3Servers.AddOrUpdate(server.Domain,
            _ => [server],
            (_, list) => { list.Add(server); return list; });

        if (_pop3CachePath == null) return;

        try
        {
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
        catch { }
    }

    public List<Server>? GetImapServers(string domain)
        => _imapServers.TryGetValue(domain, out var servers) ? servers : null;

    public List<Server>? GetPop3Servers(string domain)
        => _pop3Servers.TryGetValue(domain, out var servers) ? servers : null;

    public bool HasAnyServer(string domain)
        => _imapServers.ContainsKey(domain) || _pop3Servers.ContainsKey(domain);
}
