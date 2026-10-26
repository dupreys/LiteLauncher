using System.Net.Http.Headers;
using System.Text.Json;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.Core;

public enum ArtKind { Cover, Background, Logo }

public sealed record ArtImage(string Url, string ThumbUrl, int Width, int Height);

/// <summary>
/// Community artwork from SteamGridDB (https://www.steamgriddb.com). Needs the user's own free API key
/// (steamgriddb.com → Preferences → API). Steam games are matched by app id, others by name.
/// </summary>
public static class SteamGridDb
{
    private const string Api = "https://www.steamgriddb.com/api/v2";

    public static async Task<int?> FindGameIdAsync(HttpClient http, string apiKey, Game game, CancellationToken ct = default)
    {
        if (game.Source == SteamProvider.ProviderName)
        {
            var json = await GetAsync(http, apiKey, $"{Api}/games/steam/{game.Id}", ct);
            if (ParseGameId(json) is { } id) return id;
        }

        var search = await GetAsync(http, apiKey,
            $"{Api}/search/autocomplete/{Uri.EscapeDataString(game.OriginalName ?? game.Name)}", ct);
        return ParseFirstSearchId(search);
    }

    public static async Task<IReadOnlyList<ArtImage>> GetImagesAsync(HttpClient http, string apiKey, int gameId, ArtKind kind,
                                                                     CancellationToken ct = default)
    {
        var url = kind switch
        {
            ArtKind.Cover => $"{Api}/grids/game/{gameId}?dimensions=600x900,342x482,660x930",
            ArtKind.Background => $"{Api}/heroes/game/{gameId}",
            _ => $"{Api}/logos/game/{gameId}",
        };
        return ParseImages(await GetAsync(http, apiKey, url, ct));
    }

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> for a wrong key; returns null for "not found".</summary>
    private static async Task<string?> GetAsync(HttpClient http, string apiKey, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("SteamGridDB rejected the API key.");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    public static int? ParseGameId(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object &&
                   d.TryGetProperty("id", out var id) && id.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException) { return null; }
    }

    public static int? ParseFirstSearchId(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in d.EnumerateArray())
                if (item.TryGetProperty("id", out var id) && id.TryGetInt32(out var n)) return n;
            return null;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<ArtImage> ParseImages(string? json)
    {
        var list = new List<ArtImage>();
        if (json is null) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in d.EnumerateArray())
            {
                var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(url)) continue;
                var thumb = item.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                var w = item.TryGetProperty("width", out var wi) && wi.TryGetInt32(out var wv) ? wv : 0;
                var h = item.TryGetProperty("height", out var hi) && hi.TryGetInt32(out var hv) ? hv : 0;
                list.Add(new ArtImage(url, string.IsNullOrEmpty(thumb) ? url : thumb, w, h));
            }
        }
        catch (JsonException) { }
        return list;
    }
}
