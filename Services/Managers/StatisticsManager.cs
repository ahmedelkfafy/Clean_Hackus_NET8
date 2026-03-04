using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Clean_Hackus_NET8.Models.Enums;

namespace Clean_Hackus_NET8.Services.Managers;

public class StatisticsManager : INotifyPropertyChanged
{
    private static readonly StatisticsManager _instance = new();
    public static StatisticsManager Instance => _instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler == null) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => handler(this, new PropertyChangedEventArgs(name)));
        }
        else
        {
            handler(this, new PropertyChangedEventArgs(name));
        }
    }

    private int _loadedStrings;
    private int _checkedStrings;
    private int _loadedProxy;
    
    private int _goodMailsCount;
    private int _foundMailsCount;
    private int _badMailsCount;
    private int _twoFactorMailsCount;
    private int _multipasswordMailsCount;
    private int _blockedMailsCount;
    private int _captchaMailsCount;
    private int _errorMailsCount;
    private int _noHostMailsCount;

    public int LoadedStrings { get => _loadedStrings; set { _loadedStrings = value; Notify(); } }
    public int CheckedStrings => _checkedStrings;
    public int LoadedProxy { get => _loadedProxy; set { _loadedProxy = value; Notify(); } }

    public int GoodMailsCount => _goodMailsCount;
    public int FoundMailsCount => _foundMailsCount;
    public int BadMailsCount => _badMailsCount;
    public int TwoFactorMailsCount => _twoFactorMailsCount;
    public int MultipasswordMailsCount => _multipasswordMailsCount;
    public int BlockedMailsCount => _blockedMailsCount;
    public int CaptchaMailsCount => _captchaMailsCount;
    public int ErrorMailsCount => _errorMailsCount;
    public int NoHostMailsCount => _noHostMailsCount;

    public ConcurrentBag<string> BadDetails { get; } = new();
    public ConcurrentBag<string> BlockedDetails { get; } = new();
    public ConcurrentBag<string> ErrorDetails { get; } = new();

    private StatisticsManager() { }

    public void IncrementChecked()
    {
        Interlocked.Increment(ref _checkedStrings);
        Notify(nameof(CheckedStrings));
    }

    public void IncrementGood()
    {
        Interlocked.Increment(ref _goodMailsCount);
        Notify(nameof(GoodMailsCount));
        IncrementChecked();
    }

    public void IncrementFound()
    {
        Interlocked.Increment(ref _foundMailsCount);
        Notify(nameof(FoundMailsCount));
    }

    public void IncrementBad()
    {
        Interlocked.Increment(ref _badMailsCount);
        Notify(nameof(BadMailsCount));
        IncrementChecked();
    }

    public void IncrementTwoFactor()
    {
        Interlocked.Increment(ref _twoFactorMailsCount);
        Notify(nameof(TwoFactorMailsCount));
        IncrementChecked();
    }

    public void IncrementMultipassword()
    {
        Interlocked.Increment(ref _multipasswordMailsCount);
        Notify(nameof(MultipasswordMailsCount));
        IncrementChecked();
    }

    public void IncrementBlocked()
    {
        Interlocked.Increment(ref _blockedMailsCount);
        Notify(nameof(BlockedMailsCount));
        IncrementChecked();
    }

    public void IncrementError()
    {
        Interlocked.Increment(ref _errorMailsCount);
        Notify(nameof(ErrorMailsCount));
        IncrementChecked();
    }

    public void IncrementNoHost()
    {
        Interlocked.Increment(ref _noHostMailsCount);
        Notify(nameof(NoHostMailsCount));
        IncrementChecked();
    }

    public void IncrementCaptcha()
    {
        Interlocked.Increment(ref _captchaMailsCount);
        Notify(nameof(CaptchaMailsCount));
        IncrementChecked();
    }

    public void Increment(OperationResult result)
    {
        switch (result)
        {
            case OperationResult.Ok: IncrementGood(); break;
            case OperationResult.Bad: IncrementBad(); break;
            case OperationResult.Error: IncrementError(); break;
            case OperationResult.Blocked: IncrementBlocked(); break;
            case OperationResult.TwoFactor: IncrementTwoFactor(); break;
            case OperationResult.Multipassword: IncrementMultipassword(); break;
            case OperationResult.Captcha: IncrementCaptcha(); break;
            case OperationResult.HostNotFound: IncrementNoHost(); break;
        }
    }

    public void AddBadDetails(string address, string message) => BadDetails.Add($"{address}: {message}");
    public void AddBlockedDetails(string address, string message) => BlockedDetails.Add($"{address}: {message}");
    public void AddErrorDetails(string address, string message) => ErrorDetails.Add($"{address}: {message}");

    public void ClearResults()
    {
        _goodMailsCount = 0;
        _foundMailsCount = 0;
        _badMailsCount = 0;
        _twoFactorMailsCount = 0;
        _multipasswordMailsCount = 0;
        _blockedMailsCount = 0;
        _errorMailsCount = 0;
        _noHostMailsCount = 0;
        _captchaMailsCount = 0;
        _checkedStrings = 0;

        BadDetails.Clear();
        BlockedDetails.Clear();
        ErrorDetails.Clear();

        // Notify all properties cleared
        Notify(nameof(GoodMailsCount));
        Notify(nameof(FoundMailsCount));
        Notify(nameof(BadMailsCount));
        Notify(nameof(TwoFactorMailsCount));
        Notify(nameof(MultipasswordMailsCount));
        Notify(nameof(BlockedMailsCount));
        Notify(nameof(ErrorMailsCount));
        Notify(nameof(NoHostMailsCount));
        Notify(nameof(CaptchaMailsCount));
        Notify(nameof(CheckedStrings));
    }
}
