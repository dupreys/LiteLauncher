using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.Core;

public enum AchievementStatus
{
    /// <summary><see cref="AchievementResult.Unlocked"/> and <see cref="AchievementResult.Total"/> are valid.</summary>
    Ok,
    /// <summary>The game has no achievements.</summary>
    None,
    /// <summary>No Steam account is signed in, so there is nobody to look up.</summary>
    NotSignedIn,
    /// <summary>Steam would not tell us: private profile / private game details, offline, or an error.</summary>
    Unavailable,
    /// <summary>This store does not expose achievements to other apps (Epic).</summary>
    NotSupported,
}

/// <summary>One achievement of a game, as shown in the achievements screen.</summary>
public sealed record AchievementItem(string Name, string? Description, bool Unlocked, DateTimeOffset? UnlockedAt = null, string? IconUrl = null);

public sealed record AchievementResult(AchievementStatus Status, int Unlocked = 0, int Total = 0)
{
    /// <summary>The individual achievements: unlocked ones first (newest first), then the locked ones.</summary>
    public IReadOnlyList<AchievementItem> Items { get; init; } = Array.Empty<AchievementItem>();

    public double Fraction => Total > 0 ? Math.Clamp(Unlocked / (double)Total, 0, 1) : 0;
}

public static class Achievements
{
    private static readonly AchievementResult NoneResult = new(AchievementStatus.None);
    private static readonly AchievementResult UnavailableResult = new(AchievementStatus.Unavailable);

    /// <summary>
    /// Steam: the public community stats page (it carries names, descriptions and icons), and the official Web API
    /// as a fallback when you saved an API key. Both only work when your Steam "Game details" privacy is Public.
    /// Epic: no public or local source exists, so it reports <see cref="AchievementStatus.NotSupported"/>.
    /// </summary>
    public static async Task<AchievementResult> FetchAsync(HttpClient http, Game game, string? steamId64, string? apiKey,
                                                           CancellationToken cancellationToken = default)
    {
        if (game.Source != SteamProvider.ProviderName) return new AchievementResult(AchievementStatus.NotSupported);
        if (!SteamLoginUsers.IsSteamId64(steamId64)) return new AchievementResult(AchievementStatus.NotSignedIn);

        var fromCommunity = await TryCommunityAsync(http, game.Id, steamId64!, cancellationToken);
        if (fromCommunity.Status is AchievementStatus.Ok or AchievementStatus.None || string.IsNullOrWhiteSpace(apiKey))
            return fromCommunity;

        return await TryApiAsync(http, game.Id, steamId64!, apiKey, cancellationToken) is { Status: AchievementStatus.Ok } fromApi
            ? fromApi
            : fromCommunity;
    }

    private static async Task<AchievementResult> TryCommunityAsync(HttpClient http, string appId, string steamId64, CancellationToken ct)
    {
        try
        {
            var url = $"https://steamcommunity.com/profiles/{Uri.EscapeDataString(steamId64)}/stats/{Uri.EscapeDataString(appId)}/?xml=1";
            return ParseCommunityXml(await http.GetStringAsync(url, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return UnavailableResult;
        }
    }

    private static async Task<AchievementResult> TryApiAsync(HttpClient http, string appId, string steamId64, string apiKey, CancellationToken ct)
    {
        try
        {
            var url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/" +
                      $"?appid={Uri.EscapeDataString(appId)}&steamid={Uri.EscapeDataString(steamId64)}" +
                      $"&key={Uri.EscapeDataString(apiKey.Trim())}&l=english";
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) return NoneResult; // game without stats
            return response.IsSuccessStatusCode ? ParseApiJson(await response.Content.ReadAsStringAsync(ct)) : UnavailableResult;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return UnavailableResult;
        }
    }

    /// <summary>Parses <c>ISteamUserStats/GetPlayerAchievements</c> (names need <c>l=english</c>; no icons).</summary>
    public static AchievementResult ParseApiJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return UnavailableResult;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("playerstats", out var stats)) return UnavailableResult;

            if (!stats.TryGetProperty("achievements", out var list) || list.ValueKind != JsonValueKind.Array)
                return NoneResult;

            var items = new List<AchievementItem>();
            foreach (var item in list.EnumerateArray())
            {
                var unlocked = item.TryGetProperty("achieved", out var achieved) &&
                               achieved.ValueKind == JsonValueKind.Number && achieved.TryGetInt32(out var flag) && flag == 1;

                var name = Text(item, "name") ?? Text(item, "apiname") ?? "Achievement";
                DateTimeOffset? at = unlocked && item.TryGetProperty("unlocktime", out var t) && t.TryGetInt64(out var secs) && secs > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(secs)
                    : null;
                items.Add(new AchievementItem(name, Text(item, "description"), unlocked, at));
            }

            return Build(items);
        }
        catch (JsonException)
        {
            return UnavailableResult;
        }

        static string? Text(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim()
                : null;
    }

    /// <summary>Parses <c>steamcommunity.com/profiles/&lt;id&gt;/stats/&lt;appid&gt;/?xml=1</c>.</summary>
    public static AchievementResult ParseCommunityXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return UnavailableResult;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, settings);
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "playerstats") return UnavailableResult;
            if (root.Element("error") is not null) return UnavailableResult; // private details or unknown profile

            var nodes = root.Element("achievements")?.Elements("achievement").ToList();
            if (nodes is null || nodes.Count == 0) return NoneResult;

            var items = new List<AchievementItem>();
            foreach (var node in nodes)
            {
                var unlocked = (string?)node.Attribute("closed") == "1";
                var name = Clean(node.Element("name")) ?? Clean(node.Element("apiname")) ?? "Achievement";

                // iconClosed is the colour icon (unlocked); iconOpen is the grey one (still locked).
                var icon = Clean(node.Element(unlocked ? "iconClosed" : "iconOpen")) ?? Clean(node.Element("iconClosed"));
                DateTimeOffset? at = unlocked && long.TryParse(Clean(node.Element("unlockTimestamp")), NumberStyles.None,
                                         CultureInfo.InvariantCulture, out var secs) && secs > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(secs)
                    : null;
                items.Add(new AchievementItem(name, Clean(node.Element("description")), unlocked, at, icon));
            }

            return Build(items);
        }
        catch (XmlException)
        {
            return UnavailableResult; // an HTML error page instead of XML
        }

        static string? Clean(XElement? e) => string.IsNullOrWhiteSpace(e?.Value) ? null : e!.Value.Trim();
    }

    private static AchievementResult Build(List<AchievementItem> items)
    {
        if (items.Count == 0) return NoneResult;

        // Stable sort keeps the game's own order inside each group.
        var ordered = items.Where(i => i.Unlocked).OrderByDescending(i => i.UnlockedAt ?? DateTimeOffset.MinValue)
            .Concat(items.Where(i => !i.Unlocked))
            .ToList();
        return new AchievementResult(AchievementStatus.Ok, items.Count(i => i.Unlocked), items.Count) { Items = ordered };
    }

    public static string Describe(AchievementResult result) => result.Status switch
    {
        AchievementStatus.Ok => Loc.F("{0} of {1} unlocked", result.Unlocked, result.Total),
        AchievementStatus.None => Loc.T("This game has no achievements."),
        AchievementStatus.NotSignedIn => Loc.T("Sign in with Steam in Settings to see your achievements."),
        AchievementStatus.NotSupported => Loc.T("Epic does not share achievements with other apps."),
        _ => Loc.T("Not available. Set your Steam \"Game details\" privacy to Public."),
    };
}
