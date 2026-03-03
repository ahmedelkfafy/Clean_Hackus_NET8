using System;
using System.Collections.Generic;
using System.Threading;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services.Managers;

/// <summary>
/// Thread manager using real Thread objects (like original Hackus).
/// No Task.Run, no SemaphoreSlim — just N foreground worker threads.
/// </summary>
public class ThreadsManager
{
    private static readonly ThreadsManager _instance = new();
    public static ThreadsManager Instance => _instance;

    private CheckerState _state = CheckerState.Stopped;
    public CheckerState State { get => _state; private set => _state = value; }

    private readonly List<Thread> _threads = [];
    private readonly object _locker = new();
    private readonly ManualResetEvent _waitHandle = new(true);   // for Pause/Resume
    private volatile bool _stopRequested;

    public int ActiveThreads { get { lock (_locker) { return _threads.Count; } } }
    public ManualResetEvent WaitHandle => _waitHandle;

    private ThreadsManager() { }

    /// <summary>
    /// Start N real threads. Each runs workerAction synchronously.
    /// workerAction should: while(!StopRequested) dequeue → process
    /// </summary>
    public void Start(Action workerAction, int threadCount)
    {
        if (_state != CheckerState.Stopped) return;

        State = CheckerState.Running;
        StatisticsManager.Instance.ClearResults();
        _stopRequested = false;
        _waitHandle.Set();

        lock (_locker) { _threads.Clear(); }

        for (int i = 0; i < threadCount; i++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    workerAction();
                }
                catch (ThreadInterruptedException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Thread error: {ex.Message}");
                }
                finally
                {
                    OnThreadFinished(Thread.CurrentThread);
                }
            })
            {
                IsBackground = true,
                Name = $"Checker-{i}"
            };

            lock (_locker) { _threads.Add(thread); }
            thread.Start();
        }
    }

    public bool StopRequested => _stopRequested;

    /// <summary>WaitPause: call inside worker loop to honor Pause state.</summary>
    public void WaitPause()
    {
        _waitHandle.WaitOne();
    }

    public void Pause()
    {
        if (_state == CheckerState.Running)
        {
            _waitHandle.Reset();
            State = CheckerState.Paused;
        }
    }

    public void Resume()
    {
        if (_state == CheckerState.Paused)
        {
            _waitHandle.Set();
            State = CheckerState.Running;
        }
    }

    public void Stop()
    {
        if (_state == CheckerState.Stopped || _state == CheckerState.Closing) return;

        State = CheckerState.Closing;
        _stopRequested = true;
        _waitHandle.Set(); // unblock paused threads

        // Interrupt all threads
        lock (_locker)
        {
            foreach (var t in _threads)
            {
                try { t.Interrupt(); } catch { }
            }
        }
    }

    private void OnThreadFinished(Thread thread)
    {
        bool allDone;
        lock (_locker)
        {
            _threads.Remove(thread);
            allDone = _threads.Count == 0;
        }

        if (allDone)
        {
            State = CheckerState.Stopped;
            _stopRequested = false;
            OnAllThreadsFinished?.Invoke();
        }
    }

    /// <summary>Fires when all worker threads have finished.</summary>
    public event Action? OnAllThreadsFinished;
}
