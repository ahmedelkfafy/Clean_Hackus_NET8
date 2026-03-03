using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Clean_Hackus_NET8.Models;
using Clean_Hackus_NET8.Models.Enums;
using Clean_Hackus_NET8.Services;
using Clean_Hackus_NET8.UI.Models;
using MailKit;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MimeKit;

namespace Clean_Hackus_NET8.Components.Viewer;

public class EmailItem
{
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public string Date { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string TextBody { get; set; } = "";
    public UniqueId Uid { get; set; }
    public bool HasHtml => !string.IsNullOrEmpty(HtmlBody);
}

public class ViewerViewModel : BindableObject
{
    private readonly WebView2 _webView;

    public ObservableCollection<Mailbox> Accounts { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<EmailItem> Messages { get; } = [];

    private Mailbox? _selectedAccount;
    public Mailbox? SelectedAccount
    {
        get => _selectedAccount;
        set { _selectedAccount = value; OnPropertyChanged(); if (value != null) ThreadPool.QueueUserWorkItem(_ => LoadFolders(value)); }
    }

    private string? _selectedFolder;
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set { _selectedFolder = value; OnPropertyChanged(); if (value != null && _selectedAccount != null) ThreadPool.QueueUserWorkItem(_ => LoadMessages(_selectedAccount, value)); }
    }

    private EmailItem? _selectedMessage;
    public EmailItem? SelectedMessage
    {
        get => _selectedMessage;
        set { _selectedMessage = value; OnPropertyChanged(); if (value != null) DisplayMessage(value); }
    }

    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); }
    }

    private string _quickConnectInput = "";
    public string QuickConnectInput
    {
        get => _quickConnectInput;
        set
        {
            _quickConnectInput = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value) && value.Contains(':') && value.Contains('@'))
                ExecuteQuickConnect();
        }
    }

    private string _statusText = "Paste email:password to connect";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ICommand LoadHitsCommand { get; }
    public ICommand SearchCommand { get; }

    public ViewerViewModel(WebView2 webView)
    {
        _webView = webView;
        LoadHitsCommand = new RelayCommand(_ => ExecuteLoadHits());
        SearchCommand = new RelayCommand(_ => ThreadPool.QueueUserWorkItem(_ => DoSearch()));
        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NavigateToString("<html><body style='background:#1e1e2e;color:#cdd6f4;font-family:Segoe UI;padding:30px'><h2 style='color:#64FFDA'>Mail Viewer</h2><p>Paste <b>email:password</b> above to connect.</p></body></html>");
        }
        catch { }
    }

    // ─── Quick Connect ────────────────────────────────────────────────

    private void ExecuteQuickConnect()
    {
        var input = QuickConnectInput.Trim();
        var sepIdx = input.IndexOf(':');
        if (sepIdx <= 0) return;
        var email = input[..sepIdx].Trim();
        var password = input[(sepIdx + 1)..].Trim();
        if (!email.Contains('@') || string.IsNullOrEmpty(password)) return;

        var domain = email[(email.IndexOf('@') + 1)..].ToLowerInvariant();
        var mailbox = new Mailbox(email, password, domain);
        if (Accounts.All(a => a.Address != email))
            Accounts.Add(mailbox);
        SelectedAccount = mailbox;
    }

    // ─── Load Hits File ───────────────────────────────────────────────

    private void ExecuteLoadHits()
    {
        var dlg = new OpenFileDialog { Filter = "Text (*.txt)|*.txt|All (*.*)|*.*", Title = "Load Good/Hits" };
        if (dlg.ShowDialog() != true) return;

        Accounts.Clear();
        foreach (var raw in File.ReadAllLines(dlg.FileName))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var main = line.Split('|')[0].Trim();
            var sep = main.IndexOf(':');
            if (sep <= 0) continue;
            var email = main[..sep].Trim();
            var pass = main[(sep + 1)..].Trim();
            if (!email.Contains('@')) continue;
            Accounts.Add(new Mailbox(email, pass, email[(email.IndexOf('@') + 1)..]));
        }
        StatusText = $"Loaded {Accounts.Count} accounts";
    }

    // ─── Load Folders (SYNC in ThreadPool) ────────────────────────────

    private void LoadFolders(Mailbox mailbox)
    {
        Dispatch(() => { Folders.Clear(); Messages.Clear(); StatusText = $"Connecting {mailbox.Address}..."; });

        // IMAP first
        foreach (var server in GetImapServers(mailbox.Domain))
        {
            using var client = new Net.Mail.ImapClient(mailbox, server);
            try
            {
                if (client.Connect() != OperationResult.Ok) continue;
                var login = client.Login();
                if (login == OperationResult.Bad)
                {
                    Dispatch(() => StatusText = $"❌ Wrong password: {mailbox.Address}");
                    return;
                }
                if (login != OperationResult.Ok) continue;

                var folders = client.ListFolders();
                Dispatch(() =>
                {
                    foreach (var f in folders) Folders.Add(f);
                    StatusText = $"✅ IMAP {server.Hostname} — {folders.Count} folders";
                });
                client.Disconnect();
                return;
            }
            catch { }
        }

        // POP3 fallback
        foreach (var server in GetPop3Servers(mailbox.Domain))
        {
            using var client = new Net.Mail.Pop3Client(mailbox, server);
            try
            {
                if (client.Connect() != OperationResult.Ok) continue;
                var login = client.Login();
                if (login == OperationResult.Bad)
                {
                    Dispatch(() => StatusText = $"❌ Wrong password: {mailbox.Address}");
                    return;
                }
                if (login != OperationResult.Ok) continue;

                var count = client.GetMessageCount();
                Dispatch(() =>
                {
                    Folders.Add($"Inbox (POP3 — {count})");
                    StatusText = $"✅ POP3 {server.Hostname} — {count} messages";
                });
                client.Disconnect();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = $"❌ Cannot connect to {mailbox.Address}");
    }

    // ─── Load Messages (SYNC) ─────────────────────────────────────────

    private void LoadMessages(Mailbox mailbox, string folderName)
    {
        Dispatch(() => { Messages.Clear(); StatusText = $"Loading {folderName}..."; });

        if (folderName.Contains("POP3"))
        {
            LoadMessagesPop3(mailbox);
            return;
        }

        foreach (var server in GetImapServers(mailbox.Domain))
        {
            using var client = new Net.Mail.ImapClient(mailbox, server);
            try
            {
                if (client.Connect() != OperationResult.Ok) continue;
                if (client.Login() != OperationResult.Ok) continue;
                if (client.SelectFolder(folderName) != OperationResult.Ok) continue;

                var uids = client.Search("ALL");
                var last20 = uids.Count > 20 ? uids.Skip(uids.Count - 20).ToList() : uids;

                foreach (var uid in last20.AsEnumerable().Reverse())
                {
                    var msg = client.FetchMessage(uid);
                    if (msg == null) continue;

                    Dispatch(() => Messages.Add(MimeToItem(msg, uid)));
                }

                Dispatch(() => StatusText = $"✅ {Messages.Count} messages in {folderName}");
                client.Disconnect();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Failed to load messages");
    }

    private void LoadMessagesPop3(Mailbox mailbox)
    {
        foreach (var server in GetPop3Servers(mailbox.Domain))
        {
            using var client = new Net.Mail.Pop3Client(mailbox, server);
            try
            {
                if (client.Connect() != OperationResult.Ok) continue;
                if (client.Login() != OperationResult.Ok) continue;

                var count = client.GetMessageCount();
                for (int i = count - 1; i >= Math.Max(0, count - 10); i--)
                {
                    var msg = client.GetMessage(i);
                    if (msg == null) continue;
                    Dispatch(() => Messages.Add(MimeToItem(msg, UniqueId.Invalid)));
                }

                Dispatch(() => StatusText = $"✅ {Messages.Count} messages via POP3");
                client.Disconnect();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Failed via POP3");
    }

    // ─── Search (SYNC) ────────────────────────────────────────────────

    private void DoSearch()
    {
        if (_selectedAccount == null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        Dispatch(() => { Messages.Clear(); StatusText = $"Searching '{SearchQuery}'..."; });

        foreach (var server in GetImapServers(_selectedAccount.Domain))
        {
            using var client = new Net.Mail.ImapClient(_selectedAccount, server);
            try
            {
                if (client.Connect() != OperationResult.Ok) continue;
                if (client.Login() != OperationResult.Ok) continue;

                var folder = _selectedFolder ?? "INBOX";
                if (client.SelectFolder(folder) != OperationResult.Ok) continue;

                var q = SearchQuery;
                var criteria = $"SUBJECT \"{q}\" OR FROM \"{q}\" OR BODY \"{q}\"";
                var uids = client.Search(criteria);

                if (uids.Count == 0)
                    uids = client.Search($"SUBJECT \"{q}\"");

                var last30 = uids.Count > 30 ? uids.Skip(uids.Count - 30).ToList() : uids;

                foreach (var uid in last30.AsEnumerable().Reverse())
                {
                    var msg = client.FetchMessage(uid);
                    if (msg == null) continue;
                    Dispatch(() => Messages.Add(MimeToItem(msg, uid)));
                }

                Dispatch(() => StatusText = $"🔍 {Messages.Count} results for '{SearchQuery}'");
                client.Disconnect();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Search failed");
    }

    // ─── Display Message — render ORIGINAL HTML in WebView2 ───────────

    private void DisplayMessage(EmailItem item)
    {
        try
        {
            string html;

            if (item.HasHtml)
            {
                // Show the ORIGINAL HTML body exactly like the real email
                html = item.HtmlBody;
            }
            else
            {
                // Plain text → wrap in minimal styling
                html = "<html><head><meta charset='utf-8'/></head><body style='background:#fff;color:#222;font-family:Segoe UI,sans-serif;padding:20px;font-size:14px'>"
                    + $"<pre style='white-space:pre-wrap;word-wrap:break-word'>{System.Net.WebUtility.HtmlEncode(item.TextBody)}</pre>"
                    + "</body></html>";
            }

            _webView.Dispatcher.Invoke(() => _webView.CoreWebView2?.NavigateToString(html));
        }
        catch { }
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static EmailItem MimeToItem(MimeMessage msg, UniqueId uid)
    {
        return new EmailItem
        {
            Subject = msg.Subject ?? "(No Subject)",
            From = msg.From?.ToString() ?? "",
            Date = msg.Date.ToString("yyyy-MM-dd HH:mm"),
            HtmlBody = msg.HtmlBody ?? "",
            TextBody = msg.TextBody ?? "",
            Uid = uid
        };
    }

    private static void Dispatch(Action action)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(action);
    }

    private static List<Server> GetImapServers(string domain)
    {
        var list = new List<Server>();
        var fromDb = ServerDatabase.Instance.GetImapServers(domain);
        if (fromDb != null)
        {
            list.AddRange(fromDb.Where(s => s.Socket == SocketType.SSL));
            list.AddRange(fromDb.Where(s => s.Socket == SocketType.Plain));
        }
        if (list.Count == 0)
        {
            list.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 993, Protocol = ProtocolType.IMAP, Socket = SocketType.SSL });
            list.Add(new Server { Domain = domain, Hostname = $"imap.{domain}", Port = 143, Protocol = ProtocolType.IMAP, Socket = SocketType.Plain });
        }
        return list;
    }

    private static List<Server> GetPop3Servers(string domain)
    {
        var list = new List<Server>();
        var fromDb = ServerDatabase.Instance.GetPop3Servers(domain);
        if (fromDb != null)
        {
            list.AddRange(fromDb.Where(s => s.Socket == SocketType.SSL));
            list.AddRange(fromDb.Where(s => s.Socket == SocketType.Plain));
        }
        if (list.Count == 0)
        {
            list.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 995, Protocol = ProtocolType.POP3, Socket = SocketType.SSL });
            list.Add(new Server { Domain = domain, Hostname = $"pop.{domain}", Port = 110, Protocol = ProtocolType.POP3, Socket = SocketType.Plain });
        }
        return list;
    }
}
