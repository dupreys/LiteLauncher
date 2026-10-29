using System.Text;

namespace LiteLauncher.Core;

/// <summary>Builds the plain-text diagnostics shown in Settings. It lists facts only: never keys, passwords or tokens.</summary>
public static class DiagnosticsReport
{
    public static string Build(IEnumerable<(string Section, IReadOnlyList<(string Label, string Value)> Lines)> sections, string? crashTail)
    {
        var sb = new StringBuilder();
        foreach (var (section, lines) in sections)
        {
            sb.AppendLine(section.ToUpperInvariant());
            var width = lines.Count == 0 ? 0 : lines.Max(l => l.Label.Length);
            foreach (var (label, value) in lines)
                sb.AppendLine($"  {label.PadRight(width)}  {value}");
            sb.AppendLine();
        }

        sb.AppendLine("LAST ERRORS");
        sb.AppendLine(string.IsNullOrWhiteSpace(crashTail) ? "  none recorded" : crashTail.TrimEnd());
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Replaces the Windows user name in paths, so a report can be shared without it.</summary>
    public static string Redact(string text, string? userName = null, string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userName ??= Environment.UserName;

        if (!string.IsNullOrEmpty(userProfile)) text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (userName.Length >= 3) text = text.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>The last <paramref name="maxChars"/> characters of a log file's text, starting at a line boundary.</summary>
    public static string Tail(string? text, int maxChars = 2500)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
        var cut = text[^maxChars..];
        var newline = cut.IndexOf('\n');
        return newline >= 0 && newline < cut.Length - 1 ? cut[(newline + 1)..] : cut;
    }
}
