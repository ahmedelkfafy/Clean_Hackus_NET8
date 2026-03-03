using System;
using System.Windows;

namespace Clean_Hackus_NET8;

public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
