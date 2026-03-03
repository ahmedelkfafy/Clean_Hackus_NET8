using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Clean_Hackus_NET8.UI.Models;
using Clean_Hackus_NET8.UI.Views;

namespace Clean_Hackus_NET8.Components.Startup;

public class StartupViewModel : BindableObject
{
    private string _username = "";
    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set 
        { 
            _errorMessage = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(HasError)); 
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private string _status = "Waiting for credentials...";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private bool _isAuthorizing;
    public bool IsAuthorizing
    {
        get => _isAuthorizing;
        set { _isAuthorizing = value; OnPropertyChanged(); }
    }

    public string CurrentVersion => "NET 8 Core Version 3.0";

    public ICommand LoginCommand { get; }

    public event Action? RequestClose;

    public StartupViewModel()
    {
        LoginCommand = new RelayCommand(_ => Authenticate(), _ => !IsAuthorizing && !string.IsNullOrWhiteSpace(Username));
    }

    private async void Authenticate()
    {
        IsAuthorizing = true;
        Status = "Authorizing...";
        ErrorMessage = "";

        // Simulated API Auth Task in .NET 8 Native Implementation
        await Task.Delay(1000);

        if (Username.Length > 2)
        {
            Status = "Success! Loading modules...";
            await Task.Delay(500);

            // Trigger window close and start main interface
            var mainWindow = new MainView();
            mainWindow.Show();
            RequestClose?.Invoke();
        }
        else
        {
            Status = "Authentication failed.";
            ErrorMessage = "Invalid license credentials.";
        }

        IsAuthorizing = false;
    }
}
