using System.Windows;
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
}
