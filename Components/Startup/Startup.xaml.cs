using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Clean_Hackus_NET8.Components.Startup;

public partial class Startup : FluentWindow
{
    public Startup()
    {
        InitializeComponent();
        
        var viewModel = new StartupViewModel();
        DataContext = viewModel;

        // Subscribe to close the Startup window once Auth succeeds.
        viewModel.RequestClose += () =>
        {
            Close();
        };
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is StartupViewModel vm)
            vm.Password = ((PasswordBox)sender).Password;
    }
}
