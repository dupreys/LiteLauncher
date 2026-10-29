using System.Net.Http;
using System.Text.Json;

namespace LiteLauncher.Core;

/// <summary>
/// The official screenshots of a Steam game from the Steam store (1920x1080, always 16:9), a good source of
/// wallpapers that fill the whole screen. No API key needed.
/// </summary>
public static class SteamScreenshots
{
    public const int Width = 1920;
    public const int Height = 1080;

    public static async Task<IReadOnlyList<ArtImage>> FetchAsync(HttpClient http, string appId, CancellationToken ct = default)
    {
        if (appId.Length == 0 || !appId.All(char.IsDigit)) return Array.Empty<ArtImage>();
        try
        {
            var json = await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", ct);
            return Parse(json, appId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<ArtImage>();
        }
    }

    public static IReadOnlyList<ArtImage> Parse(string? json, string appId)
    {
        var result = new List<ArtImage>();
        if (json is null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var app) ||
                !app.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("screenshots", out var shots) || shots.ValueKind != JsonValueKind.Array) return result;

            foreach (var shot in shots.EnumerateArray())
            {
                var full = Text(shot, "path_full");
                var thumb = Text(shot, "path_thumbnail") ?? full;
                if (full is null || thumb is null || !IsSteamImage(full) || !IsSteamImage(thumb)) continue;
                result.Add(new ArtImage(full, thumb, Width, Height));
            }
        }
        catch (JsonException) { }
        return result;

        static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>Only https images from Steam's own CDNs are downloaded.</summary>
    public static bool IsSteamImage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));
}
