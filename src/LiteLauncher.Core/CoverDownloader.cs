using System.Net.Http;

namespace LiteLauncher.Core;

public enum StoreArtKind { Cover, Backdrop }

/// <summary>
/// Fetches Steam store artwork for games whose cover Steam has not cached locally yet (new installs, small apps).
/// Files are kept in a cache folder; nothing is downloaded twice. Epic offers no public artwork, so those keep the letter.
/// </summary>
public static class CoverDownloader
{
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    public static string CachePath(string cacheDir, Game game, StoreArtKind kind) =>
        Path.Combine(cacheDir, $"steam_{game.Id}_{(kind == StoreArtKind.Cover ? "cover" : "hero")}.jpg");

    public static IReadOnlyList<string> Urls(Game game, StoreArtKind kind) => kind == StoreArtKind.Cover
        ? new[] { $"{Cdn}/{game.Id}/library_600x900.jpg", $"{Cdn}/{game.Id}/header.jpg" }
        : new[] { $"{Cdn}/{game.Id}/library_hero.jpg", $"{Cdn}/{game.Id}/page_bg_generated_v6b.jpg" };

    /// <summary>The cached file when it exists already; null otherwise. Never touches the network.</summary>
    public static string? Cached(string cacheDir, Game game, StoreArtKind kind)
    {
        if (!IsSteamAppId(game)) return null;
        var path = CachePath(cacheDir, game, kind);
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>Downloads the artwork (first URL that works) into the cache and returns its path; null when none was available.</summary>
    public static async Task<string?> DownloadAsync(HttpClient http, string cacheDir, Game game, StoreArtKind kind, CancellationToken ct = default)
    {
        if (!IsSteamAppId(game)) return null;
        if (Cached(cacheDir, game, kind) is { } existing) return existing;

        foreach (var url in Urls(game, kind))
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 512) continue; // an error page, not a picture

                Directory.CreateDirectory(cacheDir);
                var path = CachePath(cacheDir, game, kind);
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                // offline or blocked: the next refresh tries again
            }
        }
        return null;
    }

    private static bool IsSteamAppId(Game game) =>
        game.Source == "Steam" && game.Id.Length > 0 && game.Id.All(char.IsAsciiDigit);
}
