using System;
using System.Collections.Concurrent;
using System.IO;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Thread-safe file writer for checker results.
/// Uses buffered StreamWriter per file to avoid lock contention and I/O thrashing.
/// </summary>
public class ResultsSaver
{
    private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();
    private string _resultsDir = string.Empty;

    private static readonly ResultsSaver _instance = new();
    public static ResultsSaver Instance => _instance;

    public string ResultsDirectory => _resultsDir;

    private ResultsSaver() { }

    public void Initialize()
    {
        FlushAll(); // Close previous writers if any
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _resultsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results", timestamp);
        Directory.CreateDirectory(_resultsDir);
    }

    /// <summary>Append line to file — thread-safe, buffered.</summary>
    public void Save(string fileName, string content)
    {
        var fileLock = _locks.GetOrAdd(fileName, _ => new object());
        lock (fileLock)
        {
            try
            {
                var writer = _writers.GetOrAdd(fileName, fn =>
                {
                    var path = Path.Combine(_resultsDir, fn);
                    return new StreamWriter(path, append: true);
                });
                writer.WriteLine(content);
            }
            catch { }
        }
    }

    /// <summary>Flush and close all open writers. Call when checker stops.</summary>
    public void FlushAll()
    {
        foreach (var kvp in _writers)
        {
            try
            {
                kvp.Value.Flush();
                kvp.Value.Dispose();
            }
            catch { }
        }
        _writers.Clear();
        _locks.Clear();
    }

    public void SaveGood(string line) => Save("Good.txt", line);
    public void SaveBad(string line) => Save("Bad.txt", line);
    public void SaveError(string line) => Save("Error.txt", line);
    public void SaveNoHost(string line) => Save("NoHost.txt", line);
    public void SaveFound(string line) => Save("Found.txt", line);
    public void SaveBlocked(string line) => Save("Blocked.txt", line);
}
