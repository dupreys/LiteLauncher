using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiteLauncher.Core;

/// <param name="Tag">For example "v1.4.0".</param>
/// <param name="Body">The notes as plain text (see <see cref="ToPlainText"/>).</param>
/// <param name="PageUrl">The github.com page of the release.</param>
public sealed record ReleaseNotesInfo(string Tag, string Title, string Body, string PageUrl);

/// <summary>The "What's new" text of a GitHub release, shown inside the app.</summary>
public static class ReleaseNotes
{
    public const int MaxLength = 6000;

    public static ReleaseNotesInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var tag = Text("tag_name");
            var url = Text("html_url");
            if (string.IsNullOrWhiteSpace(tag) || url is null) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com") return null;

            var body = ToPlainText(Text("body"));
            return body.Length == 0 ? null : new ReleaseNotesInfo(tag, Text("name") ?? tag, body, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Markdown to readable plain text: headings, bold, code, links and bullets are simplified.</summary>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var text = Regex.Replace(markdown.Replace("\r\n", "\n"), "<!--.*?-->", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>\n]+>", "");                       // html tags
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");            // images
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");         // links -> their text
        text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline); // headings
        text = Regex.Replace(text, @"^\s*[-*+]\s+", "•  ", RegexOptions.Multiline);   // bullets
        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();

        return text.Length > MaxLength ? text[..MaxLength].TrimEnd() + "…" : text;
    }

    /// <summary>Notes of the release with this tag ("v1.4.0" and "1.4.0" are both tried); null when offline or unknown.</summary>
    public static async Task<ReleaseNotesInfo?> FetchAsync(HttpClient http, string? repository, Version version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/')) return null;
        var number = $"{Math.Max(version.Major, 0)}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
        foreach (var tag in new[] { "v" + number, number })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}");
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("User-Agent", "LiteLauncher");
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;
                if (Parse(await response.Content.ReadAsStringAsync(ct)) is { } info) return info;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                return null; // offline: try again next start
            }
        }
        return null;
    }
}
