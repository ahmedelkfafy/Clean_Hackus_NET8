using System;
using System.Windows;

namespace Clean_Hackus_NET8;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var startupWindow = new Components.Startup.Startup();
        startupWindow.Show();
    }
}
