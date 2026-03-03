using System;
using System.IO;
using System.Linq;
using System.Windows;
using Clean_Hackus_NET8.Services;

namespace Clean_Hackus_NET8;

public partial class App : Application
{
    public static string DataFolder { get; private set; } = "";

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(DataFolder);

        // Init POP3 cache
        var pop3CachePath = Path.Combine(DataFolder, "pop3_cache.db");
        ServerDatabase.Instance.InitPop3Cache(pop3CachePath);

        // Auto-load IMAP .db files (background, non-blocking)
        var dbFiles = Directory.GetFiles(DataFolder, "*.db")
            .Where(f => !Path.GetFileName(f).Equals("pop3_cache.db", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int totalServers = 0;
        foreach (var dbFile in dbFiles)
            totalServers += ServerDatabase.Instance.LoadImapDatabase(dbFile);

        // Go directly to MainView (skip auth)
        var mainView = new UI.Views.MainView();
        if (totalServers > 0)
            mainView.Title = $"Hackus — {totalServers} IMAP servers loaded";
        mainView.Show();
    }
}
