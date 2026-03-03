using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Services;
using Clean_Hackus_NET8.Services.Managers;
using Clean_Hackus_NET8.UI.Models;
using Microsoft.Win32;

namespace Clean_Hackus_NET8.UI.ViewModels;

public class MainViewModel : BindableObject
{
    public StatisticsManager Statistics => StatisticsManager.Instance;
    public ThreadsManager Threads => ThreadsManager.Instance;
    public ProxyManager Proxies => ProxyManager.Instance;

    private ConcurrentQueue<Mailbox>? _comboQueue;
    private int _totalCombo;

    // ─── Bindable Properties ──────────────────────────────────────────

    private int _threadCount = 100;
    public int ThreadCount
    {
        get => _threadCount;
        set { _threadCount = value; OnPropertyChanged(); }
    }

    private string _statusText = "Ready. Load a combo and servers to begin.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private int _comboLoaded;
    public int ComboLoaded
    {
        get => _comboLoaded;
        set { _comboLoaded = value; OnPropertyChanged(); }
    }

    private int _serversLoaded;
    public int ServersLoaded
    {
        get => _serversLoaded;
        set { _serversLoaded = value; OnPropertyChanged(); }
    }

    private int _proxiesLoaded;
    public int ProxiesLoaded
    {
        get => _proxiesLoaded;
        set { _proxiesLoaded = value; OnPropertyChanged(); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    // ─── Commands ─────────────────────────────────────────────────────

    public ICommand LoadComboCommand { get; }
    public ICommand LoadServersCommand { get; }
    public ICommand LoadProxiesCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenViewerCommand { get; }

    public MainViewModel()
    {
        LoadComboCommand = new RelayCommand(_ => ExecuteLoadCombo());
        LoadServersCommand = new RelayCommand(_ => ExecuteLoadServers());
        LoadProxiesCommand = new RelayCommand(_ => ExecuteLoadProxies());
        StartCommand = new RelayCommand(_ => ExecuteStart(), _ => Threads.State == Clean_Hackus_NET8.Models.Enums.CheckerState.Stopped && _comboQueue != null);
        StopCommand = new RelayCommand(_ => ExecuteStop(), _ => Threads.State == Clean_Hackus_NET8.Models.Enums.CheckerState.Running);
        OpenViewerCommand = new RelayCommand(_ => ExecuteOpenViewer());
    }

    // ─── Load Combo ───────────────────────────────────────────────────

    private void ExecuteLoadCombo()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load Combo File"
        };

        if (dialog.ShowDialog() == true)
        {
            _comboQueue = ComboLoader.LoadFromFile(dialog.FileName);
            _totalCombo = _comboQueue.Count;
            ComboLoaded = _totalCombo;
            Statistics.LoadedStrings = _totalCombo;
            StatusText = $"Loaded {_totalCombo:N0} combos from {Path.GetFileName(dialog.FileName)}";
        }
    }

    // ─── Load Servers (.db) ───────────────────────────────────────────

    private void ExecuteLoadServers()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Database files (*.db)|*.db|All files (*.*)|*.*",
            Title = "Load IMAP Server Database"
        };

        if (dialog.ShowDialog() == true)
        {
            var count = ServerDatabase.Instance.LoadImapDatabase(dialog.FileName);

            // Initialize POP3 cache alongside the IMAP db
            var pop3CachePath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName) ?? AppDomain.CurrentDomain.BaseDirectory,
                "pop3_cache.db");
            ServerDatabase.Instance.InitPop3Cache(pop3CachePath);

            ServersLoaded = count;
            StatusText = $"Loaded {count:N0} IMAP servers. POP3 will auto-discover.";
        }
    }

    // ─── Load Proxies ─────────────────────────────────────────────────

    private void ExecuteLoadProxies()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load Proxy List"
        };

        if (dialog.ShowDialog() == true)
        {
            var count = Proxies.LoadFromFile(dialog.FileName);
            ProxiesLoaded = count;
            StatusText = $"Loaded {count:N0} proxies.";
        }
    }

    // ─── Start Checker ────────────────────────────────────────────────

    private async void ExecuteStart()
    {
        if (_comboQueue == null || _comboQueue.IsEmpty)
        {
            StatusText = "No combo loaded. Please load a combo file first.";
            return;
        }

        ResultsSaver.Instance.Initialize();
        StatusText = $"Running... Threads: {ThreadCount}";

        var engine = new CheckerEngine();
        var progressTimer = new System.Timers.Timer(500);
        progressTimer.Elapsed += (_, _) =>
        {
            if (_totalCombo > 0)
                Progress = (double)Statistics.CheckedStrings / _totalCombo * 100;
        };
        progressTimer.Start();

        await Threads.RunAsync(async token =>
        {
            while (_comboQueue.TryDequeue(out var mailbox))
            {
                token.ThrowIfCancellationRequested();
                await engine.CheckMailboxAsync(mailbox, token);
            }
        }, ThreadCount);

        progressTimer.Stop();
        Progress = 100;
        StatusText = $"Done! Good: {Statistics.GoodMailsCount} | Bad: {Statistics.BadMailsCount} | Error: {Statistics.ErrorMailsCount} | NoHost: {Statistics.NoHostMailsCount}";
    }

    // ─── Stop ─────────────────────────────────────────────────────────

    private void ExecuteStop()
    {
        Threads.Stop();
        StatusText = "Stopping...";
    }

    // ─── Open Viewer ──────────────────────────────────────────────────

    private void ExecuteOpenViewer()
    {
        var viewer = new Components.Viewer.ViewerWindow();
        viewer.Show();
    }
}
