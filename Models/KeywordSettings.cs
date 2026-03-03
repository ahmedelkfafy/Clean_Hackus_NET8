using System.Collections.ObjectModel;

namespace Clean_Hackus_NET8.Models;

/// <summary>
/// Keyword search settings. When enabled, forces IMAP-only checking.
/// </summary>
public class KeywordSettings
{
    private static readonly KeywordSettings _instance = new();
    public static KeywordSettings Instance => _instance;

    public bool Enabled { get; set; }
    public ObservableCollection<string> SenderKeywords { get; } = [];
    public ObservableCollection<string> SubjectKeywords { get; } = [];
    public ObservableCollection<string> BodyKeywords { get; } = [];

    private KeywordSettings() { }

    /// <summary>Build IMAP SEARCH query from keywords.</summary>
    public string BuildImapSearchQuery()
    {
        var parts = new System.Collections.Generic.List<string>();

        foreach (var kw in SenderKeywords)
            parts.Add($"FROM \"{kw}\"");
        foreach (var kw in SubjectKeywords)
            parts.Add($"SUBJECT \"{kw}\"");
        foreach (var kw in BodyKeywords)
            parts.Add($"BODY \"{kw}\"");

        if (parts.Count == 0) return "ALL";

        // OR logic: IMAP OR needs nesting — OR (A) (OR (B) (C))
        if (parts.Count == 1) return parts[0];

        var result = parts[^1];
        for (int i = parts.Count - 2; i >= 0; i--)
        {
            result = $"OR {parts[i]} {result}";
        }
        return result;
    }

    public bool HasKeywords => SenderKeywords.Count > 0 || SubjectKeywords.Count > 0 || BodyKeywords.Count > 0;
}
