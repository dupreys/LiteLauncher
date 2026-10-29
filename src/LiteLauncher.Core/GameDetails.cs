using System.Globalization;
using System.Text.Json;
using LiteLauncher.Core.Epic;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.Core;

/// <summary>Extra information shown in the game menu. Every field is optional: offline means "just less to show".</summary>
public sealed record GameDetails
{
    public string? Developer { get; init; }
    public string? ReleaseDate { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public string? Description { get; init; }
    public int? PlayersNow { get; init; }
    public int? PlaytimeMinutes { get; init; }
}

public static class GameInfo
{
    /// <summary>The store page for the game, in the store client when possible.</summary>
    public static string StoreUrl(Game game) => game.Source switch
    {
        SteamProvider.ProviderName => $"steam://store/{game.Id}",
        EpicProvider.ProviderName => "https://store.epicgames.com/browse?q=" +
                                     Uri.EscapeDataString(game.OriginalName ?? game.Name) + "&sortBy=relevancy",
        _ => "https://www.google.com/search?q=" + Uri.EscapeDataString(game.OriginalName ?? game.Name),
    };

    /// <summary>Browser fallback when the store client is not installed.</summary>
    public static string StoreWebUrl(Game game) => game.Source == SteamProvider.ProviderName
        ? $"https://store.steampowered.com/app/{game.Id}/"
        : StoreUrl(game);

    public static async Task<GameDetails> FetchAsync(HttpClient http, Game game, string? steamPath, string? steamId64,
                                                     CancellationToken cancellationToken = default)
    {
        if (game.Source != SteamProvider.ProviderName) return new GameDetails();

        var playtime = steamPath is null ? null : SteamPlaytime.Read(steamPath, steamId64, game.Id);
        var storeTask = GetStringOrNull(http, $"https://store.steampowered.com/api/appdetails?appids={game.Id}&l=english", cancellationToken);
        var playersTask = GetStringOrNull(http, $"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={game.Id}", cancellationToken);
        await Task.WhenAll(storeTask, playersTask);

        var details = ParseSteamStore(await storeTask, game.Id) ?? new GameDetails();
        return details with { PlayersNow = ParsePlayerCount(await playersTask), PlaytimeMinutes = playtime };
    }

    private static async Task<string?> GetStringOrNull(HttpClient http, string url, CancellationToken ct)
    {
        try { return await http.GetStringAsync(url, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public static GameDetails? ParseSteamStore(string? json, string appId)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var app) ||
                !app.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !app.TryGetProperty("data", out var data)) return null;

            string? Str(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var developer = data.TryGetProperty("developers", out var devs) && devs.ValueKind == JsonValueKind.Array
                ? string.Join(", ", devs.EnumerateArray().Select(d => d.GetString()).Where(d => !string.IsNullOrEmpty(d)))
                : null;
            var genres = data.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
                ? g.EnumerateArray().Select(x => Str(x, "description")).OfType<string>().ToList()
                : new List<string>();
            var date = data.TryGetProperty("release_date", out var rd) ? Str(rd, "date") : null;

            return new GameDetails
            {
                Developer = string.IsNullOrEmpty(developer) ? null : developer,
                ReleaseDate = date,
                Genres = genres,
                Description = System.Net.WebUtility.HtmlDecode(Str(data, "short_description") ?? "").Trim() is { Length: > 0 } d ? d : null,
            };
        }
        catch (JsonException) { return null; }
    }

    public static int? ParsePlayerCount(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("response", out var r) &&
                   r.TryGetProperty("player_count", out var c) && c.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException) { return null; }
    }

    public static string FormatPlaytime(int minutes) =>
        minutes < 60 ? Loc.F("{0} min", minutes) : Loc.F("{0} hrs {1} min", minutes / 60, minutes % 60);

    public static string FormatSize(long bytes) => bytes <= 0
        ? "—"
        : bytes >= 1L << 30
            ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
}

/// <summary>Your playtime, as the Steam client stores it in userdata\&lt;account&gt;\config\localconfig.vdf.</summary>
public static class SteamPlaytime
{
    private const long SteamId64Base = 76561197960265728;

    public static int? Read(string steamPath, string? steamId64, string appId)
    {
        try
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return null;

            // The signed-in account first; otherwise any account on this PC that has a value.
            var folders = new List<string>();
            if (long.TryParse(steamId64, out var id64) && id64 > SteamId64Base)
                folders.Add(Path.Combine(userdata, (id64 - SteamId64Base).ToString(CultureInfo.InvariantCulture)));
            folders.AddRange(Directory.EnumerateDirectories(userdata));

            foreach (var folder in folders.Distinct())
            {
                var file = Path.Combine(folder, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                if (ParseLocalConfig(File.ReadAllText(file), appId) is { } minutes) return minutes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Playtime in minutes of every game in the account's localconfig.vdf, read once (appId -> minutes).</summary>
    public static IReadOnlyDictionary<string, int> ReadAll(string steamPath, string? steamId64)
    {
        var result = new Dictionary<string, int>();
        try
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return result;

            var folders = new List<string>();
            if (long.TryParse(steamId64, out var id64) && id64 > SteamId64Base)
                folders.Add(Path.Combine(userdata, (id64 - SteamId64Base).ToString(CultureInfo.InvariantCulture)));
            folders.AddRange(Directory.EnumerateDirectories(userdata));

            foreach (var folder in folders.Distinct())
            {
                var file = Path.Combine(folder, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                var all = ParseAllLocalConfig(File.ReadAllText(file));
                if (all.Count == 0) continue;
                return all;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public static IReadOnlyDictionary<string, int> ParseAllLocalConfig(string vdf)
    {
        var result = new Dictionary<string, int>();
        var steam = VdfParser.Parse(vdf)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"];
        var apps = steam?["apps"] ?? steam?["Apps"];
        if (apps is null) return result;
        foreach (var (id, node) in apps.Children)
        {
            var minutes = node.GetLong("Playtime", -1);
            if (minutes > 0 && id.All(char.IsAsciiDigit)) result[id] = (int)Math.Min(minutes, int.MaxValue);
        }
        return result;
    }

    public static int? ParseLocalConfig(string vdf, string appId)
    {
        var root = VdfParser.Parse(vdf);
        var app = root["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"]?[appId]
                  ?? root["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["Apps"]?[appId];
        var value = app?.GetLong("Playtime", -1) ?? -1;
        return value >= 0 ? (int)value : null;
    }
}
