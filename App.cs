using System;
using System.IO;
using System.Linq;
using System.Windows;
using Clean_Hackus_NET8.Services;

namespace Clean_Hackus_NET8;

public partial class App : Application
{
    /// <summary>Folder where IMAP .db files are placed. POP3 cache is also created here.</summary>
    public static string DataFolder { get; private set; } = "";

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Create Data folder next to the exe
        DataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(DataFolder);

        // Auto-load all .db files from Data folder (IMAP servers)
        var dbFiles = Directory.GetFiles(DataFolder, "*.db")
            .Where(f => !Path.GetFileName(f).Equals("pop3_cache.db", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int totalServers = 0;
        foreach (var dbFile in dbFiles)
        {
            try
            {
                totalServers += ServerDatabase.Instance.LoadImapDatabase(dbFile);
            }
            catch { }
        }

        // Init POP3 cache in the same Data folder
        var pop3CachePath = Path.Combine(DataFolder, "pop3_cache.db");
        ServerDatabase.Instance.InitPop3Cache(pop3CachePath);

        var startupWindow = new Components.Startup.Startup();
        startupWindow.Show();
    }
}
