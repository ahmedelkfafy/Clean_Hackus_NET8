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
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Clean_Hackus_NET8.Components.Viewer;

public class EmailMessage
{
    public string Subject { get; set; } = "(No Subject)";
    public string From { get; set; } = "";
    public string Date { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }
}

public class ViewerViewModel : BindableObject
{
    private readonly WebView2 _webView;

    public ObservableCollection<Mailbox> Accounts { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<EmailMessage> Messages { get; } = [];

    private Mailbox? _selectedAccount;
    public Mailbox? SelectedAccount
    {
        get => _selectedAccount;
        set { _selectedAccount = value; OnPropertyChanged(); if (value != null) _ = LoadFoldersAsync(value); }
    }

    private string? _selectedFolder;
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set { _selectedFolder = value; OnPropertyChanged(); if (value != null && _selectedAccount != null) _ = LoadMessagesAsync(_selectedAccount, value); }
    }

    private EmailMessage? _selectedMessage;
    public EmailMessage? SelectedMessage
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

    private string _statusText = "Paste email:password to connect, or load from Good.txt";
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
        SearchCommand = new RelayCommand(_ => _ = ExecuteSearchAsync());
        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NavigateToString("<html><body style='background:#1e1e2e;color:#cdd6f4;font-family:Segoe UI;padding:24px'><p>Paste <b>email:password</b> above to connect, or load from Good.txt.</p></body></html>");
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

    // ─── Load from file ───────────────────────────────────────────────

    private void ExecuteLoadHits()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load Good/Hits File"
        };

        if (dialog.ShowDialog() != true) return;

        Accounts.Clear();
        foreach (var rawLine in File.ReadAllLines(dialog.FileName))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var mainPart = line.Split('|')[0].Trim();
            var separatorIndex = mainPart.IndexOf(':');
            if (separatorIndex <= 0) continue;
            var email = mainPart[..separatorIndex].Trim();
            var password = mainPart[(separatorIndex + 1)..].Trim();
            if (!email.Contains('@')) continue;
            Accounts.Add(new Mailbox(email, password, email[(email.IndexOf('@') + 1)..]));
        }
        StatusText = $"Loaded {Accounts.Count} accounts";
    }

    // ─── Load Folders ─────────────────────────────────────────────────

    private async Task LoadFoldersAsync(Mailbox mailbox)
    {
        Dispatch(() => { Folders.Clear(); Messages.Clear(); StatusText = $"Connecting to {mailbox.Address}..."; });

        // Try IMAP
        foreach (var server in GetImapServers(mailbox.Domain))
        {
            try
            {
                using var client = new Net.Mail.ImapClient(mailbox, server);
                var connectResult = await client.ConnectAsync(null);
                if (connectResult != OperationResult.Ok) continue;

                var loginResult = await client.LoginAsync();
                if (loginResult == OperationResult.Bad)
                {
                    Dispatch(() => StatusText = $"❌ Wrong password for {mailbox.Address} via {server.Hostname}");
                    await client.DisconnectAsync();
                    return;
                }
                if (loginResult != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var folderList = await client.ListFoldersAsync();
                Dispatch(() =>
                {
                    foreach (var f in folderList) Folders.Add(f);
                    StatusText = $"✅ Connected via IMAP ({server.Hostname}) — {folderList.Count} folders";
                });

                await client.DisconnectAsync();
                return;
            }
            catch { }
        }

        // Fallback: POP3
        foreach (var server in GetPop3Servers(mailbox.Domain))
        {
            try
            {
                using var client = new Net.Mail.Pop3Client(mailbox, server);
                var connectResult = await client.ConnectAsync(null);
                if (connectResult != OperationResult.Ok) continue;

                var loginResult = await client.LoginAsync();
                if (loginResult == OperationResult.Bad)
                {
                    Dispatch(() => StatusText = $"❌ Wrong password for {mailbox.Address} via POP3 {server.Hostname}");
                    await client.DisconnectAsync();
                    return;
                }
                if (loginResult != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var count = client.GetMessageCount();
                Dispatch(() =>
                {
                    Folders.Add($"Inbox (POP3 — {count} msgs)");
                    StatusText = $"✅ Connected via POP3 ({server.Hostname}) — {count} messages";
                });

                await client.DisconnectAsync();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = $"❌ Failed to connect to {mailbox.Address}");
    }

    // ─── Load Messages ────────────────────────────────────────────────

    private async Task LoadMessagesAsync(Mailbox mailbox, string folderName)
    {
        Dispatch(() => { Messages.Clear(); StatusText = $"Loading messages from {folderName}..."; });

        if (folderName.Contains("POP3"))
        {
            await LoadMessagesPop3Async(mailbox);
            return;
        }

        // IMAP
        foreach (var server in GetImapServers(mailbox.Domain))
        {
            try
            {
                using var client = new Net.Mail.ImapClient(mailbox, server);
                if (await client.ConnectAsync(null) != OperationResult.Ok) continue;
                if (await client.LoginAsync() != OperationResult.Ok) { await client.DisconnectAsync(); continue; }
                if (await client.SelectFolderAsync(new Folder { Name = folderName }) != OperationResult.Ok)
                {
                    await client.DisconnectAsync();
                    continue;
                }

                var uids = await client.SearchAsync("ALL");
                var lastUids = uids.Count > 20 ? uids.Skip(uids.Count - 20).ToList() : uids;

                foreach (var uid in lastUids.AsEnumerable().Reverse())
                {
                    var (subject, from, date, body) = await client.FetchMessageAsync(uid);
                    var isHtml = body.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase);
                    Dispatch(() => Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from, Date = date, Body = body, IsHtml = isHtml
                    }));
                }

                Dispatch(() => StatusText = $"✅ Loaded {Messages.Count} messages from {folderName}");
                await client.DisconnectAsync();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Failed to load messages");
    }

    private async Task LoadMessagesPop3Async(Mailbox mailbox)
    {
        foreach (var server in GetPop3Servers(mailbox.Domain))
        {
            try
            {
                using var client = new Net.Mail.Pop3Client(mailbox, server);
                if (await client.ConnectAsync(null) != OperationResult.Ok) continue;
                if (await client.LoginAsync() != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var count = client.GetMessageCount();
                var startFrom = Math.Max(0, count - 10);

                for (int i = count - 1; i >= startFrom; i--)
                {
                    var (subject, from, date, body) = await client.GetMessageAsync(i);
                    if (string.IsNullOrEmpty(subject) && string.IsNullOrEmpty(body)) continue;
                    var isHtml = body.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase);
                    Dispatch(() => Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from, Date = date, Body = body, IsHtml = isHtml
                    }));
                }

                Dispatch(() => StatusText = $"✅ Loaded {Messages.Count} messages via POP3");
                await client.DisconnectAsync();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Failed to load messages via POP3");
    }

    // ─── Search ───────────────────────────────────────────────────────

    private async Task ExecuteSearchAsync()
    {
        if (_selectedAccount == null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        Dispatch(() => { Messages.Clear(); StatusText = $"Searching for '{SearchQuery}'..."; });

        foreach (var server in GetImapServers(_selectedAccount.Domain))
        {
            try
            {
                using var client = new Net.Mail.ImapClient(_selectedAccount, server);
                if (await client.ConnectAsync(null) != OperationResult.Ok) continue;
                if (await client.LoginAsync() != OperationResult.Ok) { await client.DisconnectAsync(); continue; }
                if (await client.SelectFolderAsync(new Folder { Name = _selectedFolder ?? "INBOX" }) != OperationResult.Ok)
                {
                    await client.DisconnectAsync();
                    continue;
                }

                var criteria = $"OR SUBJECT \"{SearchQuery}\" OR FROM \"{SearchQuery}\" BODY \"{SearchQuery}\"";
                var uids = await client.SearchAsync(criteria);
                var lastUids = uids.Count > 30 ? uids.Skip(uids.Count - 30).ToList() : uids;

                foreach (var uid in lastUids.AsEnumerable().Reverse())
                {
                    var (subject, from, date, body) = await client.FetchMessageAsync(uid);
                    var isHtml = body.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase);
                    Dispatch(() => Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from, Date = date, Body = body, IsHtml = isHtml
                    }));
                }

                Dispatch(() => StatusText = $"Found {Messages.Count} results for '{SearchQuery}'");
                await client.DisconnectAsync();
                return;
            }
            catch { }
        }

        Dispatch(() => StatusText = "❌ Search failed");
    }

    // ─── Display ──────────────────────────────────────────────────────

    private void DisplayMessage(EmailMessage message)
    {
        try
        {
            var body = message.Body;
            if (!message.IsHtml)
                body = $"<pre style='white-space:pre-wrap;word-wrap:break-word'>{System.Net.WebUtility.HtmlEncode(body)}</pre>";

            var html = "<html><head><style>"
                + "body { background:#1e1e2e; color:#cdd6f4; font-family:'Segoe UI',sans-serif; padding:24px; }"
                + "h2 { color:#89b4fa; margin-bottom:4px; }"
                + ".meta { color:#6c7086; font-size:13px; margin-bottom:16px; }"
                + "hr { border:1px solid #313244; }"
                + "a { color:#89b4fa; } pre { color:#cdd6f4; }"
                + "</style></head><body>"
                + $"<h2>{System.Net.WebUtility.HtmlEncode(message.Subject)}</h2>"
                + $"<div class='meta'>From: {System.Net.WebUtility.HtmlEncode(message.From)} &bull; {System.Net.WebUtility.HtmlEncode(message.Date)}</div>"
                + "<hr/>"
                + $"<div>{body}</div>"
                + "</body></html>";

            _webView.Dispatcher.Invoke(() => _webView.CoreWebView2?.NavigateToString(html));
        }
        catch { }
    }

    // ─── Helpers ──────────────────────────────────────────────────────

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
