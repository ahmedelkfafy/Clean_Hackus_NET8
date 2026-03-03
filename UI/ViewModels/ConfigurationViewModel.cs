using System.Windows.Input;
using Clean_Hackus_NET8.UI.Models;
using Clean_Hackus_NET8.Services.Managers;

namespace Clean_Hackus_NET8.UI.ViewModels;

public class ConfigurationViewModel : BindableObject
{
    private int _threadsLimit = 150;
    public int ThreadsLimit
    {
        get => _threadsLimit;
        set { _threadsLimit = value; OnPropertyChanged(); }
    }

    private int _connectionTimeout = 15000;
    public int ConnectionTimeout
    {
        get => _connectionTimeout;
        set { _connectionTimeout = value; OnPropertyChanged(); }
    }

    private bool _allowRebrute = false;
    public bool AllowRebrute
    {
        get => _allowRebrute;
        set { _allowRebrute = value; OnPropertyChanged(); }
    }

    private bool _downloadAttachments = true;
    public bool DownloadAttachments
    {
        get => _downloadAttachments;
        set { _downloadAttachments = value; OnPropertyChanged(); }
    }

    public ICommand SaveConfigurationCommand { get; }

    public ConfigurationViewModel()
    {
        // Hydrate from SettingsManager if it exists
        // ThreadsLimit = SettingsManager.Instance.Threads;

        SaveConfigurationCommand = new RelayCommand(_ => ExecuteSave());
    }

    private void ExecuteSave()
    {
        // Example logic replacing HandyControl Growl Success popup mapping to UI WPF snackbar.
        // Instead of triggering UI layer directly, we pass via Bindings or EventAggregators.
        
        // SettingsManager.Instance.Threads = ThreadsLimit;
        // SettingsManager.Instance.Save();
    }
}
