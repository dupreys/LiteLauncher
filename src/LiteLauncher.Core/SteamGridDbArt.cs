using System.Net.Http;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.Core;

/// <summary>
/// Automatic artwork from SteamGridDB for games that have none (Epic, manually added): looked up by name with the
/// user's own API key, kept in the same cache folder as the Steam artwork and never downloaded twice.
/// </summary>
public static class SteamGridDbArt
{
    private const int MaxBytes = 8 * 1024 * 1024;

    private static string Stem(Game game, StoreArtKind kind) =>
        $"sgdb_{Safe(game.Source)}_{Safe(game.Id)}_{(kind == StoreArtKind.Cover ? "cover" : "hero")}";

    private static string Safe(string value) => string.Concat(value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));

    /// <summary>Games that get SteamGridDB art automatically.</summary>
    public static bool Applies(Game game) => game.Source is "Epic" or "Manual";

    // ---- Steam store screenshot (no key needed): for games that also exist on Steam ----

    private static string ShotStem(Game game) => $"steamshot_{Safe(game.Source)}_{Safe(game.Id)}_hero";

    public static string? CachedSteamShot(string cacheDir, Game game)
    {
        if (!Applies(game) || !Directory.Exists(cacheDir)) return null;
        return Directory.EnumerateFiles(cacheDir, ShotStem(game) + ".*")
            .FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && new FileInfo(f).Length > 0);
    }

    private static string WallStem(Game game) => $"wallhaven_{Safe(game.Source)}_{Safe(game.Id)}_hero";

    public static string? CachedWallhaven(string cacheDir, Game game)
    {
        if (!Applies(game) || !Directory.Exists(cacheDir)) return null;
        return Directory.EnumerateFiles(cacheDir, WallStem(game) + ".*")
            .FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && new FileInfo(f).Length > 0);
    }

    /// <summary>Downloads a 16:9 wallpaper of the game from wallhaven.cc (no key); only when its tags show it is this game.</summary>
    public static async Task<string?> DownloadWallhavenAsync(HttpClient http, string cacheDir, Game game, CancellationToken ct = default)
    {
        if (!Applies(game)) return null;
        if (CachedWallhaven(cacheDir, game) is { } existing) return existing;

        try
        {
            var url = await Wallhaven.FindWallpaperUrlAsync(http, game.OriginalName ?? game.Name, ct);
            if (url is null) return null;

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > Wallhaven.MaxFileBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is < 512 or > Wallhaven.MaxFileBytes) return null;

            try
            {
                Directory.CreateDirectory(cacheDir);
                var path = Path.Combine(cacheDir, WallStem(game) + Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant());
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline or rate limited: tried again next start
        }
    }

    /// <summary>The 16:9 background already on disk (the Steam screenshot first, then the Wallhaven one), if any.</summary>
    public static string? CachedWideHero(string cacheDir, Game game) => CachedSteamShot(cacheDir, game) ?? CachedWallhaven(cacheDir, game);

    /// <summary>
    /// Looks the game up in the Steam store by its exact name and saves the first official 1920x1080 screenshot.
    /// Needs no key; games that are not on Steam (or whose name differs) simply get nothing.
    /// </summary>
    public static async Task<string?> DownloadSteamShotAsync(HttpClient http, string cacheDir, Game game, CancellationToken ct = default)
    {
        if (!Applies(game)) return null;
        if (CachedSteamShot(cacheDir, game) is { } existing) return existing;

        try
        {
            var appId = await SteamStoreSearch.FindAppIdAsync(http, game.OriginalName ?? game.Name, ct);
            if (appId is null) return null;
            var shots = await SteamScreenshots.FetchAsync(http, appId, ct);
            if (shots.Count == 0 || !SteamScreenshots.IsSteamImage(shots[0].Url)) return null;

            using var response = await http.GetAsync(shots[0].Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is < 512 or > MaxBytes) return null;

            try
            {
                Directory.CreateDirectory(cacheDir);
                var path = Path.Combine(cacheDir, ShotStem(game) + ".jpg");
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline: tried again next start
        }
    }

    public static string? Cached(string cacheDir, Game game, StoreArtKind kind)
    {
        if (!Applies(game) || !Directory.Exists(cacheDir)) return null;
        return Directory.EnumerateFiles(cacheDir, Stem(game, kind) + ".*")
            .FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && new FileInfo(f).Length > 0);
    }

    /// <summary>Only https pictures from SteamGridDB's own hosts are fetched.</summary>
    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host == "steamgriddb.com" || uri.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Picks the best-looking picture: tall for covers, wide for backgrounds.</summary>
    public static ArtImage? Pick(IEnumerable<ArtImage> images, StoreArtKind kind) =>
        kind == StoreArtKind.Cover
            ? images.Where(i => IsAllowedUrl(i.Url)).OrderByDescending(i => i.Height > i.Width ? 1 : 0).FirstOrDefault()
            // Backgrounds: still wider than tall, but the least wide ones first (they fill the screen better).
            : images.Where(i => IsAllowedUrl(i.Url))
                    .OrderBy(i => i.Height > 0 && i.Width >= i.Height * 1.5 ? 0 : 1)
                    .ThenBy(i => i.Height > 0 ? (double)i.Width / i.Height : 99)
                    .ThenByDescending(i => i.Width)
                    .FirstOrDefault();

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when SteamGridDB rejects the key.</summary>
    public static async Task<string?> DownloadAsync(HttpClient http, string apiKey, string cacheDir, Game game, StoreArtKind kind,
                                                    int? knownGameId = null, CancellationToken ct = default)
    {
        if (!Applies(game)) return null;
        if (Cached(cacheDir, game, kind) is { } existing) return existing;

        try
        {
            var id = knownGameId ?? await SteamGridDb.FindGameIdAsync(http, apiKey, game, ct);
            if (id is null) return null;

            var images = await SteamGridDb.GetImagesAsync(http, apiKey, id.Value,
                kind == StoreArtKind.Cover ? ArtKind.Cover : ArtKind.Background, ct);
            if (Pick(images, kind) is not { } art) return null;

            using var response = await http.GetAsync(art.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is < 512 or > MaxBytes) return null;

            var extension = Path.GetExtension(new Uri(art.Url).AbsolutePath).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) extension = ".png";

            try
            {
                Directory.CreateDirectory(cacheDir);
                var path = Path.Combine(cacheDir, Stem(game, kind) + extension);
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // a file problem (not the key): tried again next start
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline: tried again next start
        }
    }
}
