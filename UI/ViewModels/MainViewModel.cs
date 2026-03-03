using System.Windows.Input;
using Clean_Hackus_NET8.Services.Managers;
using Clean_Hackus_NET8.UI.Models;

namespace Clean_Hackus_NET8.UI.ViewModels;

public class MainViewModel : BindableObject
{
    public StatisticsManager Statistics => StatisticsManager.Instance;
    public ThreadsManager Threads => ThreadsManager.Instance;
    public ProxyManager Proxies => ProxyManager.Instance;

    private int _threadCount = 100;
    public int ThreadCount
    {
        get => _threadCount;
        set { _threadCount = value; OnPropertyChanged(); }
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(_ => StartChecker(), _ => Threads.State == Models.Enums.CheckerState.Stopped);
        StopCommand = new RelayCommand(_ => StopChecker(), _ => Threads.State == Models.Enums.CheckerState.Running);
    }

    private async void StartChecker()
    {
        // Example mock connection for the UI binding test.
        // It triggers ThreadsManager utilizing the new .NET 8 Task architecture.
        await Threads.RunAsync(async token =>
        {
            // Worker logic simulating email checking connected to StatisticsManager
            await System.Threading.Tasks.Task.Delay(100, token);
            Statistics.IncrementGood();
        }, ThreadCount);
    }

    private void StopChecker()
    {
        Threads.Stop();
    }
}
