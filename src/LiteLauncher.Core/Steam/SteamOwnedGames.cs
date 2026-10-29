using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace LiteLauncher.Core.Steam;

public sealed record OwnedGame(string AppId, string Name);

/// <summary>
/// The games an account owns on Steam (installed or not). Uses the Steam Web API when the user has a key,
/// otherwise the public profile's game list (which works only while "Game details" are public on the profile).
/// </summary>
public static class SteamOwnedGames
{
    /// <returns>The list, or null when it could not be read (offline, private profile, wrong key).</returns>
    public static async Task<IReadOnlyList<OwnedGame>?> FetchAsync(HttpClient http, string steamId64, string? apiKey,
                                                                   CancellationToken ct = default)
    {
        if (!SteamLoginUsers.IsSteamId64(steamId64)) return null;

        try
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                          $"?key={Uri.EscapeDataString(apiKey.Trim())}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1&format=json";
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var list = ParseApi(await response.Content.ReadAsStringAsync(ct));
                    if (list.Count > 0) return list;
                }
            }

            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId64}/games?tab=all&xml=1", ct);
            var fromXml = ParseXml(xml);
            return fromXml.Count > 0 ? fromXml : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public static IReadOnlyList<OwnedGame> ParseApi(string? json)
    {
        var list = new List<OwnedGame>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r) ||
                !r.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array) return list;
            foreach (var g in games.EnumerateArray())
            {
                if (!g.TryGetProperty("appid", out var id) || !id.TryGetInt64(out var appId) || appId <= 0) continue;
                var name = g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name)) list.Add(new OwnedGame(appId.ToString(), name.Trim()));
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static IReadOnlyList<OwnedGame> ParseXml(string? xml)
    {
        var list = new List<OwnedGame>();
        if (string.IsNullOrWhiteSpace(xml)) return list;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var doc = XDocument.Load(reader);
            foreach (var game in doc.Descendants("game"))
            {
                var id = game.Element("appID")?.Value.Trim();
                var name = game.Element("name")?.Value.Trim();
                if (!string.IsNullOrEmpty(id) && id.All(char.IsAsciiDigit) && !string.IsNullOrEmpty(name)) list.Add(new OwnedGame(id, name));
            }
        }
        catch (XmlException) { }
        return list;
    }

    /// <summary>Owned games that are not installed, as tiles for the library, sorted by name.</summary>
    public static IReadOnlyList<Game> NotInstalled(IEnumerable<OwnedGame> owned, ISet<string> installedIds, string? steamPath)
    {
        var games = new Dictionary<string, Game>();
        foreach (var o in owned)
        {
            if (installedIds.Contains(o.AppId) || SteamLibraryReader.IsIgnored(o.AppId, o.Name)) continue;
            games[o.AppId] = new Game(SteamProvider.ProviderName, o.AppId, o.Name)
            {
                NotInstalled = true,
                CoverPath = steamPath is null ? null : SteamArtwork.FindCover(steamPath, o.AppId),
                BackdropPath = steamPath is null ? null : SteamArtwork.FindBackdrop(steamPath, o.AppId),
            };
        }
        return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
