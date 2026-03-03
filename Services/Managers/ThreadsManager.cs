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

    public async Task RunAsync(Func<CancellationToken, Task> workerFunc, int threadsCount)
    {
        if (_state == CheckerState.Stopped)
        {
            State = CheckerState.Running;
            StatisticsManager.Instance.ClearResults();
            
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // Start multiple worker tasks optimized for .NET 8 ThreadPool
            var tasks = new Task[threadsCount];
            for (int i = 0; i < threadsCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        await workerFunc(token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Task was cancelled, expected
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Worker Error: {ex.Message}");
                    }
                }, token);
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            finally
            {
                State = CheckerState.Stopped;
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
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
