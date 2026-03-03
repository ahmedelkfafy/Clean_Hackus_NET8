using System;
using System.Threading;
using System.Threading.Tasks;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services.Managers;

public class ThreadsManager
{
    private static readonly ThreadsManager _instance = new();
    public static ThreadsManager Instance => _instance;

    private CheckerState _state = CheckerState.Stopped;
    public CheckerState State { get => _state; private set => _state = value; }

    private CancellationTokenSource? _cancellationTokenSource;

    private ThreadsManager() { }

    /// <summary>
    /// Run worker with SemaphoreSlim throttling (NOT raw Task.Run per thread).
    /// This prevents CPU spike from 100+ parallel tasks.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> workerFunc, int maxConcurrency)
    {
        if (_state != CheckerState.Stopped) return;

        State = CheckerState.Running;
        StatisticsManager.Instance.ClearResults();

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        // Use SemaphoreSlim to limit concurrent workers
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var workerTask = Task.Run(async () =>
        {
            try
            {
                await workerFunc(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker error: {ex.Message}");
            }
        }, token);

        try
        {
            await workerTask;
        }
        finally
        {
            State = CheckerState.Stopped;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void Stop()
    {
        if (_state == CheckerState.Running || _state == CheckerState.Paused)
        {
            State = CheckerState.Closing;
            _cancellationTokenSource?.Cancel();
        }
    }
}
