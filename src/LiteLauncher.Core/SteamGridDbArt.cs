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
        images.Where(i => IsAllowedUrl(i.Url))
              .OrderByDescending(i => kind == StoreArtKind.Cover ? (i.Height > i.Width ? 1 : 0) : (i.Width >= i.Height * 1.5 ? 1 : 0))
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
