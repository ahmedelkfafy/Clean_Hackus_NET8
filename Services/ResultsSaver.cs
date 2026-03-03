using System;
using System.IO;
using System.Threading;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Thread-safe file writer for checker results.
/// Uses lock (not SemaphoreSlim) for synchronous thread-safe writes.
/// </summary>
public class ResultsSaver
{
    private static readonly object _writeLock = new();
    private string _resultsDir = string.Empty;

    private static readonly ResultsSaver _instance = new();
    public static ResultsSaver Instance => _instance;

    public string ResultsDirectory => _resultsDir;

    private ResultsSaver() { }

    public void Initialize()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _resultsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results", timestamp);
        Directory.CreateDirectory(_resultsDir);
    }

    /// <summary>Append line to file — thread-safe, synchronous.</summary>
    public void Save(string fileName, string content)
    {
        lock (_writeLock)
        {
            try
            {
                var filePath = Path.Combine(_resultsDir, fileName);
                File.AppendAllText(filePath, content + Environment.NewLine);
            }
            catch { }
        }
    }

    public void SaveGood(string line) => Save("Good.txt", line);
    public void SaveBad(string line) => Save("Bad.txt", line);
    public void SaveError(string line) => Save("Error.txt", line);
    public void SaveNoHost(string line) => Save("NoHost.txt", line);
    public void SaveFound(string line) => Save("Found.txt", line);
    public void SaveBlocked(string line) => Save("Blocked.txt", line);
}
