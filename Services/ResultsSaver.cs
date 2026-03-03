using System;
using System.IO;
using System.Threading;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Thread-safe file writer for checker results.
/// Creates timestamped result folders with separate files per result type.
/// </summary>
public class ResultsSaver
{
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _resultsDir = string.Empty;

    private static readonly ResultsSaver _instance = new();
    public static ResultsSaver Instance => _instance;

    public string ResultsDirectory => _resultsDir;

    private ResultsSaver() { }

    /// <summary>
    /// Initialize results directory with a timestamp.
    /// </summary>
    public void Initialize()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _resultsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results", timestamp);
        Directory.CreateDirectory(_resultsDir);
    }

    /// <summary>
    /// Append a line to a result file (thread-safe).
    /// </summary>
    public async System.Threading.Tasks.Task SaveAsync(string fileName, string content)
    {
        await _writeLock.WaitAsync();
        try
        {
            var filePath = Path.Combine(_resultsDir, fileName);
            await File.AppendAllTextAsync(filePath, content + Environment.NewLine);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public System.Threading.Tasks.Task SaveGoodAsync(string line) => SaveAsync("Good.txt", line);
    public System.Threading.Tasks.Task SaveBadAsync(string line) => SaveAsync("Bad.txt", line);
    public System.Threading.Tasks.Task SaveErrorAsync(string line) => SaveAsync("Error.txt", line);
    public System.Threading.Tasks.Task SaveNoHostAsync(string line) => SaveAsync("NoHost.txt", line);
}
