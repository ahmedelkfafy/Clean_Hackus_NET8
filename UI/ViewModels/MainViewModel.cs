using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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
    public KeywordSettings Keywords => KeywordSettings.Instance;

    private ConcurrentQueue<Mailbox>? _comboQueue;
    private int _totalCombo;

    // ─── Bindable Properties ──────────────────────────────────────────

    private int _threadCount = 100;
    public int ThreadCount
    {
        get => _threadCount;
        set { _threadCount = value; OnPropertyChanged(); }
    }

    private string _statusText = "Ready. Load combo to begin.";
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
        set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressDash)); }
    }

    public double ProgressPercent => _comboLoaded > 0 ? (double)Statistics.CheckedStrings / _comboLoaded * 100 : 0;
    public double ProgressDash => _comboLoaded > 0 ? 100 - (double)Statistics.CheckedStrings / _comboLoaded * 100 : 100;

    private int _cpm;
    public int CPM
    {
        get => _cpm;
        set { _cpm = value; OnPropertyChanged(); }
    }

    private string _pauseResumeText = "PAUSE";
    public string PauseResumeText
    {
        get => _pauseResumeText;
        set { _pauseResumeText = value; OnPropertyChanged(); }
    }

    // Settings
    private bool _useProxy;
    public bool UseProxy
    {
        get => _useProxy;
        set { _useProxy = value; Proxies.Enabled = value; OnPropertyChanged(); }
    }

    private bool _keywordEnabled;
    public bool KeywordEnabled
    {
        get => _keywordEnabled;
        set { _keywordEnabled = value; Keywords.Enabled = value; OnPropertyChanged(); }
    }

    private string _keywordSender = "";
    public string KeywordSender
    {
        get => _keywordSender;
        set
        {
            _keywordSender = value;
            OnPropertyChanged();
            UpdateKeywords(Keywords.SenderKeywords, value);
        }
    }

    private string _keywordSubject = "";
    public string KeywordSubject
    {
        get => _keywordSubject;
        set
        {
            _keywordSubject = value;
            OnPropertyChanged();
            UpdateKeywords(Keywords.SubjectKeywords, value);
        }
    }

    private string _keywordBody = "";
    public string KeywordBody
    {
        get => _keywordBody;
        set
        {
            _keywordBody = value;
            OnPropertyChanged();
            UpdateKeywords(Keywords.BodyKeywords, value);
        }
    }

    private int _selectedProxyTypeIndex;
    public int SelectedProxyTypeIndex
    {
        get => _selectedProxyTypeIndex;
        set
        {
            _selectedProxyTypeIndex = value;
            Proxies.DefaultType = value switch { 1 => ProxyType.SOCKS4, 2 => ProxyType.SOCKS5, _ => ProxyType.HTTP };
            OnPropertyChanged();
        }
    }

    private static void UpdateKeywords(ObservableCollection<string> collection, string csv)
    {
        collection.Clear();
        foreach (var kw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            collection.Add(kw);
    }

    // ─── Commands ─────────────────────────────────────────────────────

    public ICommand LoadComboCommand { get; }
    public ICommand LoadServersCommand { get; }
    public ICommand LoadProxiesCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PauseResumeCommand { get; }
    public ICommand OpenViewerCommand { get; }

    private static string SettingsPath => Path.Combine(App.DataFolder, "settings.json");

    public MainViewModel()
    {
        LoadComboCommand = new RelayCommand(_ => ExecuteLoadCombo());
        LoadServersCommand = new RelayCommand(_ => ExecuteLoadServers());
        LoadProxiesCommand = new RelayCommand(_ => ExecuteLoadProxies());
        StartCommand = new RelayCommand(_ => ExecuteStart());
        StopCommand = new RelayCommand(_ => ExecuteStop());
        PauseResumeCommand = new RelayCommand(_ => ExecutePauseResume());
        OpenViewerCommand = new RelayCommand(_ => ExecuteOpenViewer());

        // Show auto-loaded server count
        ServersLoaded = ServerDatabase.Instance.ImapServerCount;
        if (ServersLoaded > 0) StatusText = $"Ready. {ServersLoaded} IMAP servers auto-loaded from Data folder.";

        // Load saved settings
        LoadSettings();
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
            try
            {
                _comboQueue = ComboLoader.LoadFromFile(dialog.FileName);
                _totalCombo = _comboQueue.Count;
                ComboLoaded = _totalCombo;
                Statistics.LoadedStrings = _totalCombo;
                StatusText = $"Loaded {_totalCombo:N0} combos.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
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
            try
            {
                var count = ServerDatabase.Instance.LoadImapDatabase(dialog.FileName);
                ServersLoaded = ServerDatabase.Instance.ImapServerCount;
                StatusText = $"Loaded {count:N0} IMAP servers from {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
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
            try
            {
                var count = Proxies.LoadFromFile(dialog.FileName);
                ProxiesLoaded = count;
                UseProxy = count > 0;
                StatusText = $"Loaded {count:N0} proxies ({Proxies.DefaultType}).";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
        }
    }

    // ─── Start ────────────────────────────────────────────────────────

    private void ExecuteStart()
    {
        if (_comboQueue == null || _comboQueue.IsEmpty)
        {
            StatusText = "Load combo first!";
            return;
        }

        ResultsSaver.Instance.Initialize();
        SaveSettings(); // Persist current settings
        Progress = 0;
        CPM = 0;
        StatusText = $"Running... {ThreadCount} threads | {(KeywordEnabled ? "IMAP+Keyword" : "POP3→IMAP")}";

        var progressTimer = new System.Timers.Timer(500);
        int lastChecked = 0;
        DateTime lastTime = DateTime.UtcNow;
        progressTimer.Elapsed += (_, _) =>
        {
            if (_totalCombo > 0)
                Progress = (double)Statistics.CheckedStrings / _totalCombo * 100;

            // CPM calculation
            var now = DateTime.UtcNow;
            var elapsed = (now - lastTime).TotalMinutes;
            if (elapsed > 0)
            {
                var diff = Statistics.CheckedStrings - lastChecked;
                CPM = (int)(diff / elapsed);
                lastChecked = Statistics.CheckedStrings;
                lastTime = now;
            }
        };
        progressTimer.Start();

        // When all threads finish (self-unsubscribing to prevent leak)
        Action? finishHandler = null;
        finishHandler = () =>
        {
            progressTimer.Stop();
            Progress = 100;
            Threads.OnAllThreadsFinished -= finishHandler;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusText = $"Done! Good:{Statistics.GoodMailsCount} Bad:{Statistics.BadMailsCount} Error:{Statistics.ErrorMailsCount} NoHost:{Statistics.NoHostMailsCount}";
            });
        };
        Threads.OnAllThreadsFinished += finishHandler;

        // N real threads, each runs this synchronous worker
        int threadCount = Math.Min(ThreadCount, _totalCombo);
        Threads.Start(() =>
        {
            var engine = new CheckerEngine();

            while (!Threads.StopRequested && _comboQueue.TryDequeue(out var mailbox))
            {
                Threads.WaitPause(); // honor Pause

                try
                {
                    // Run sync MailKit — no async deadlock risk
                    engine.CheckMailbox(mailbox);
                }
                catch (ThreadInterruptedException) { break; }
                catch { Statistics.IncrementError(); }
            }
        }, threadCount);
    }

    // ─── Stop ─────────────────────────────────────────────────────────

    private void ExecuteStop()
    {
        Threads.Stop();
        PauseResumeText = "PAUSE";
        StatusText = "Stopping...";
    }

    // ─── Pause / Resume ───────────────────────────────────────────────

    private void ExecutePauseResume()
    {
        if (Threads.State == Models.Enums.CheckerState.Running)
        {
            Threads.Pause();
            PauseResumeText = "RESUME";
            StatusText = "Paused.";
        }
        else if (Threads.State == Models.Enums.CheckerState.Paused)
        {
            Threads.Resume();
            PauseResumeText = "PAUSE";
            StatusText = $"Running... {ThreadCount} threads | {(KeywordEnabled ? "IMAP+Keyword" : "POP3→IMAP")}";
        }
    }

    // ─── Open Viewer ──────────────────────────────────────────────────

    private void ExecuteOpenViewer()
    {
        try
        {
            var viewer = new Components.Viewer.ViewerWindow();
            viewer.Show();
        }
        catch (Exception ex)
        {
            StatusText = $"Viewer error: {ex.Message}";
        }
    }

    // ─── Settings Persistence ─────────────────────────────────────────

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<SettingsData>(json);
                if (s != null)
                {
                    ThreadCount = s.ThreadCount > 0 ? s.ThreadCount : 100;
                    UseProxy = s.UseProxy;
                    SelectedProxyTypeIndex = s.ProxyTypeIndex;
                    KeywordEnabled = s.KeywordEnabled;
                    KeywordSender = s.KeywordSender ?? "";
                    KeywordSubject = s.KeywordSubject ?? "";
                    KeywordBody = s.KeywordBody ?? "";
                }
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            var s = new SettingsData
            {
                ThreadCount = ThreadCount,
                UseProxy = UseProxy,
                ProxyTypeIndex = SelectedProxyTypeIndex,
                KeywordEnabled = KeywordEnabled,
                KeywordSender = KeywordSender,
                KeywordSubject = KeywordSubject,
                KeywordBody = KeywordBody
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private class SettingsData
    {
        public int ThreadCount { get; set; } = 100;
        public bool UseProxy { get; set; }
        public int ProxyTypeIndex { get; set; }
        public bool KeywordEnabled { get; set; }
        public string KeywordSender { get; set; } = "";
        public string KeywordSubject { get; set; } = "";
        public string KeywordBody { get; set; } = "";
    }
}
