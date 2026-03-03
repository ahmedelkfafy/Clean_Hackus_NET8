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
        set
        {
            _selectedAccount = value;
            OnPropertyChanged();
            if (value != null) _ = LoadFoldersAsync(value);
        }
    }

    private string? _selectedFolder;
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            _selectedFolder = value;
            OnPropertyChanged();
            if (value != null && _selectedAccount != null) _ = LoadMessagesAsync(_selectedAccount, value);
        }
    }

    private EmailMessage? _selectedMessage;
    public EmailMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            _selectedMessage = value;
            OnPropertyChanged();
            if (value != null) DisplayMessage(value);
        }
    }

    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); }
    }

    // Quick Connect: single field for "email:password" — auto-connects on paste
    private string _quickConnectInput = "";
    public string QuickConnectInput
    {
        get => _quickConnectInput;
        set
        {
            _quickConnectInput = value;
            OnPropertyChanged();

            // Auto-connect when pasting email:password
            if (!string.IsNullOrWhiteSpace(value) && value.Contains(':') && value.Contains('@'))
            {
                ExecuteQuickConnect();
            }
        }
    }

    public ICommand LoadHitsCommand { get; }
    public ICommand SearchCommand { get; }

    public ViewerViewModel(WebView2 webView)
    {
        _webView = webView;
        LoadHitsCommand = new RelayCommand(_ => ExecuteLoadHits());
        SearchCommand = new RelayCommand(_ => ExecuteSearch(), _ => !string.IsNullOrWhiteSpace(SearchQuery) && _selectedAccount != null);
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

    // ─── Quick Connect (auto on paste) ────────────────────────────────

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

        // Avoid duplicates
        if (Accounts.All(a => a.Address != email))
            Accounts.Add(mailbox);

        SelectedAccount = mailbox;
    }

    // ─── Load from Good.txt ───────────────────────────────────────────

    private void ExecuteLoadHits()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load Good/Hits File"
        };

        if (dialog.ShowDialog() != true) return;

        Accounts.Clear();
        var lines = File.ReadAllLines(dialog.FileName);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var mainPart = line.Split('|')[0].Trim();
            var separatorIndex = mainPart.IndexOf(':');
            if (separatorIndex <= 0) continue;

            var email = mainPart[..separatorIndex].Trim();
            var password = mainPart[(separatorIndex + 1)..].Trim();

            if (!email.Contains('@')) continue;
            var domain = email[(email.IndexOf('@') + 1)..];

            Accounts.Add(new Mailbox(email, password, domain));
        }
    }

    // ─── Load Folders: IMAP first → POP3 fallback ─────────────────────

    private async Task LoadFoldersAsync(Mailbox mailbox)
    {
        Folders.Clear();
        Messages.Clear();

        // Try IMAP first (SSL then Plain)
        var imapLoaded = await TryLoadFoldersImapAsync(mailbox);
        if (imapLoaded) return;

        // Fallback: POP3 (no real folders, just show "Inbox")
        Folders.Add("Inbox (POP3)");
    }

    private async Task<bool> TryLoadFoldersImapAsync(Mailbox mailbox)
    {
        var serversToTry = GetImapServers(mailbox.Domain);

        foreach (var server in serversToTry)
        {
            try
            {
                using var client = new Net.Mail.ImapClient(mailbox, server);
                var ok = await client.ConnectAsync(null);
                if (ok != OperationResult.Ok) continue;

                var login = await client.LoginAsync();
                if (login != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                // Real folder listing
                var folderList = await client.ListFoldersAsync();
                foreach (var f in folderList)
                    Folders.Add(f);

                await client.DisconnectAsync();
                return true;
            }
            catch { }
        }
        return false;
    }

    private List<Server> GetImapServers(string domain)
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

    private List<Server> GetPop3Servers(string domain)
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

    // ─── Load Messages: IMAP first → POP3 last 10 ────────────────────

    private async Task LoadMessagesAsync(Mailbox mailbox, string folderName)
    {
        Messages.Clear();

        if (folderName.Contains("POP3"))
        {
            await LoadMessagesPop3Async(mailbox);
            return;
        }

        // Try IMAP
        var loaded = await TryLoadMessagesImapAsync(mailbox, folderName);
        if (!loaded)
        {
            // Fallback to POP3
            await LoadMessagesPop3Async(mailbox);
        }
    }

    private async Task<bool> TryLoadMessagesImapAsync(Mailbox mailbox, string folderName)
    {
        var serversToTry = GetImapServers(mailbox.Domain);

        foreach (var server in serversToTry)
        {
            try
            {
                using var client = new Net.Mail.ImapClient(mailbox, server);
                var ok = await client.ConnectAsync(null);
                if (ok != OperationResult.Ok) continue;

                var login = await client.LoginAsync();
                if (login != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var selectResult = await client.SelectFolderAsync(new Folder { Name = folderName });
                if (selectResult != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                // Get last 20 messages
                var uids = await client.SearchAsync("ALL");
                var lastUids = uids.Count > 20 ? uids.Skip(uids.Count - 20).ToList() : uids;

                foreach (var uid in lastUids.AsEnumerable().Reverse())
                {
                    var (subject, from, date, body) = await client.FetchMessageAsync(uid);
                    Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from,
                        Date = date,
                        Body = body
                    });
                }

                await client.DisconnectAsync();
                return true;
            }
            catch { }
        }
        return false;
    }

    private async Task LoadMessagesPop3Async(Mailbox mailbox)
    {
        var serversToTry = GetPop3Servers(mailbox.Domain);

        foreach (var server in serversToTry)
        {
            try
            {
                using var client = new Net.Mail.Pop3Client(mailbox, server);
                var ok = await client.ConnectAsync(null);
                if (ok != OperationResult.Ok) continue;

                var login = await client.LoginAsync();
                if (login != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var count = await client.GetMessageCountAsync();
                var startFrom = Math.Max(1, count - 9); // Last 10 messages

                for (int i = count; i >= startFrom; i--)
                {
                    var raw = await client.RetrieveMessageAsync(i);
                    if (string.IsNullOrEmpty(raw)) continue;

                    var (subject, from, date, body) = Net.Mail.Pop3Client.ParseRawMessage(raw);
                    Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from,
                        Date = date,
                        Body = body
                    });
                }

                await client.DisconnectAsync();
                return;
            }
            catch { }
        }
    }

    // ─── Search ───────────────────────────────────────────────────────

    private async void ExecuteSearch()
    {
        if (_selectedAccount == null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        Messages.Clear();
        var serversToTry = GetImapServers(_selectedAccount.Domain);

        foreach (var server in serversToTry)
        {
            try
            {
                using var client = new Net.Mail.ImapClient(_selectedAccount, server);
                var ok = await client.ConnectAsync(null);
                if (ok != OperationResult.Ok) continue;

                var login = await client.LoginAsync();
                if (login != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                // Search across INBOX
                var selectResult = await client.SelectFolderAsync(new Folder { Name = _selectedFolder ?? "INBOX" });
                if (selectResult != OperationResult.Ok) { await client.DisconnectAsync(); continue; }

                var criteria = $"OR SUBJECT \"{SearchQuery}\" OR FROM \"{SearchQuery}\" BODY \"{SearchQuery}\"";
                var uids = await client.SearchAsync(criteria);

                var lastUids = uids.Count > 30 ? uids.Skip(uids.Count - 30).ToList() : uids;

                foreach (var uid in lastUids.AsEnumerable().Reverse())
                {
                    var (subject, from, date, body) = await client.FetchMessageAsync(uid);
                    Messages.Add(new EmailMessage
                    {
                        Subject = string.IsNullOrEmpty(subject) ? "(No Subject)" : subject,
                        From = from,
                        Date = date,
                        Body = body
                    });
                }

                await client.DisconnectAsync();
                return;
            }
            catch { }
        }
    }

    // ─── Display Message in WebView2 ──────────────────────────────────

    private void DisplayMessage(EmailMessage message)
    {
        try
        {
            var body = message.Body;
            // If body doesn't look like HTML, wrap in <pre>
            if (!body.TrimStart().StartsWith("<", StringComparison.OrdinalIgnoreCase))
            {
                body = $"<pre style='white-space:pre-wrap;word-wrap:break-word'>{System.Net.WebUtility.HtmlEncode(body)}</pre>";
            }

            var html = $"""
                <html>
                <head><style>
                    body {{ background:#1e1e2e; color:#cdd6f4; font-family:'Segoe UI',sans-serif; padding:24px; }}
                    h2 {{ color:#89b4fa; margin-bottom:4px; }}
                    .meta {{ color:#6c7086; font-size:13px; margin-bottom:16px; }}
                    hr {{ border:1px solid #313244; }}
                    a {{ color:#89b4fa; }}
                    pre {{ color:#cdd6f4; }}
                </style></head>
                <body>
                    <h2>{System.Net.WebUtility.HtmlEncode(message.Subject)}</h2>
                    <div class="meta">From: {System.Net.WebUtility.HtmlEncode(message.From)} &bull; {System.Net.WebUtility.HtmlEncode(message.Date)}</div>
                    <hr/>
                    <div>{body}</div>
                </body>
                </html>
                """;

            _webView.CoreWebView2?.NavigateToString(html);
        }
        catch { }
    }
}
