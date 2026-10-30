using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using LiteLauncher.Core.Apps;

namespace LiteLauncher.Core;

/// <summary>
/// Wallpapers from wallhaven.cc (public API v1, no key: safe-for-work results only, 45 requests per minute).
/// A picture is used only when its tags show it is about this game, since the search is by keyword.
/// </summary>
public static class Wallhaven
{
    private const string Api = "https://wallhaven.cc/api/v1";
    public const int MaxFileBytes = 12 * 1024 * 1024;
    private const int MaxCandidates = 3;
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(1600); // ~37 requests a minute, under the 45 limit

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    /// <summary>Only https pictures from wallhaven's own picture host are fetched.</summary>
    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("w.wallhaven.cc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Safe-for-work "general" and "anime" wallpapers of exactly 16:9 sizes the launcher can show well, best match first.</summary>
    public static string BuildSearchUrl(string name) =>
        $"{Api}/search?q={Uri.EscapeDataString(StartApps.Normalize(name))}&categories=110&purity=100" +
        "&resolutions=1920x1080,2560x1440,3840x2160&sorting=relevance";

    /// <summary>The 16:9 wallpapers a search found (for the user to pick from): full picture, thumbnail and size.</summary>
    public static IReadOnlyList<ArtImage> ParseImages(string? json, int max = 24)
    {
        var images = new List<ArtImage>();
        if (string.IsNullOrWhiteSpace(json)) return images;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return images;
            foreach (var item in data.EnumerateArray())
            {
                var full = item.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                if (!IsAllowedUrl(full)) continue;
                if (item.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var bytes) && bytes > MaxFileBytes) continue;

                string? thumb = null;
                if (item.TryGetProperty("thumbs", out var thumbs) && thumbs.ValueKind == JsonValueKind.Object
                    && thumbs.TryGetProperty("large", out var large) && large.ValueKind == JsonValueKind.String) thumb = large.GetString();
                if (!Uri.TryCreate(thumb, UriKind.Absolute, out var t) || t.Scheme != Uri.UriSchemeHttps
                    || !(t.Host.Equals("th.wallhaven.cc", StringComparison.OrdinalIgnoreCase) || t.Host.Equals("w.wallhaven.cc", StringComparison.OrdinalIgnoreCase)))
                    thumb = full;

                var (w, h) = (0, 0);
                if (item.TryGetProperty("resolution", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    var parts = (r.GetString() ?? "").Split('x');
                    if (parts.Length == 2) { int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out w); int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out h); }
                }
                images.Add(new ArtImage(full!, thumb!, w, h));
                if (images.Count == max) break;
            }
        }
        catch (JsonException) { }
        return images;
    }

    /// <summary>All the 16:9 wallpapers the search finds for a name, best match first (the user picks; no tag check).</summary>
    public static async Task<IReadOnlyList<ArtImage>> SearchAsync(HttpClient http, string gameName, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(gameName) ? Array.Empty<ArtImage>() : ParseImages(await GetPoliteAsync(http, BuildSearchUrl(gameName), ct));

    /// <summary>The ids of the first few results.</summary>
    public static IReadOnlyList<string> ParseIds(string? json)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return ids;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return ids;
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())
                    && id.GetString()!.All(char.IsLetterOrDigit))
                    ids.Add(id.GetString()!);
                if (ids.Count == MaxCandidates) break;
            }
        }
        catch (JsonException) { }
        return ids;
    }

    /// <summary>
    /// The picture address when this wallpaper is about the game: one tag is the game's name (or "name game") and another says
    /// "video game". Null when the tags do not show that, or the file is too big.
    /// </summary>
    public static string? ParseMatchingPath(string? json, string gameName)
    {
        var wanted = StartApps.Normalize(gameName);
        if (wanted.Length == 0 || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            if (data.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var bytes) && bytes > MaxFileBytes) return null;
            if (!data.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array) return null;

            var named = false;
            var isGame = false;
            foreach (var tag in tags.EnumerateArray())
            {
                if (!tag.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var t = StartApps.Normalize(n.GetString() ?? "");
                if (t == wanted || t == wanted + " game" || t == wanted + " video game") named = true;
                if (t.Contains("video game", StringComparison.Ordinal)) isGame = true;
            }
            if (!named || !isGame) return null;
            if (data.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String && IsAllowedUrl(path.GetString())) return path.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Looks the game up and returns the address of a matching wallpaper, or null. Network errors surface as <see cref="HttpRequestException"/>.</summary>
    public static async Task<string?> FindWallpaperUrlAsync(HttpClient http, string gameName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;
        foreach (var id in ParseIds(await GetPoliteAsync(http, BuildSearchUrl(gameName), ct)))
        {
            var url = ParseMatchingPath(await GetPoliteAsync(http, $"{Api}/w/{id}", ct), gameName);
            if (url is not null) return url;
        }
        return null;
    }

    /// <summary>One request at a time, with a pause in between so the site's per-minute limit is not hit.</summary>
    private static async Task<string> GetPoliteAsync(HttpClient http, string url, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + MinGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            try { return await http.GetStringAsync(url, ct); }
            finally { _lastRequest = DateTime.UtcNow; }
        }
        finally { Gate.Release(); }
    }
}
