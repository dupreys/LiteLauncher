using System.Xml;
using System.Xml.Linq;

namespace LiteLauncher.Core.Steam;

/// <summary>Public Steam profile info. Never contains credentials.</summary>
public sealed record SteamAccount(string SteamId64, string PersonaName, string? AvatarUrl = null);

/// <summary>Reads the accounts that have signed in to the local Steam client.</summary>
public static class SteamLoginUsers
{
    public static bool IsSteamId64(string? value) =>
        value is { Length: 17 } && value.All(char.IsAsciiDigit);

    /// <summary>The account most recently used in the Steam client on this PC, if any.</summary>
    public static SteamAccount? ReadMostRecent(string steamPath)
    {
        var file = Path.Combine(steamPath, "config", "loginusers.vdf");
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SteamAccount? Parse(string vdf)
    {
        var users = VdfParser.Parse(vdf)["users"];
        if (users is null) return null;

        SteamAccount? best = null;
        var bestMostRecent = false;
        long bestTimestamp = -1;

        foreach (var (id, node) in users.Children)
        {
            if (!IsSteamId64(id)) continue;

            var name = node.GetString("PersonaName") ?? node.GetString("AccountName") ?? id;
            var mostRecent = node.GetString("MostRecent") == "1";
            var timestamp = node.GetLong("Timestamp");

            var better = best is null
                || (mostRecent && !bestMostRecent)
                || (mostRecent == bestMostRecent && timestamp > bestTimestamp);

            if (!better) continue;
            best = new SteamAccount(id, name);
            bestMostRecent = mostRecent;
            bestTimestamp = timestamp;
        }

        return best;
    }
}

/// <summary>Fetches name and avatar from the public Steam Community profile (no API key).</summary>
public static class SteamProfile
{
    public static async Task<SteamAccount?> FetchAsync(HttpClient http, string steamId64, CancellationToken cancellationToken = default)
    {
        if (!SteamLoginUsers.IsSteamId64(steamId64)) return null;

        try
        {
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId64}/?xml=1", cancellationToken);
            return ParseProfileXml(xml, steamId64);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            return null; // offline, private profile, or Steam hiccup: caller falls back to local info
        }
    }

    public static SteamAccount? ParseProfileXml(string xml, string steamId64)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);

        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name != "profile") return null; // private/unknown profiles return <response><error>

        var name = root.Element("steamID")?.Value;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var avatar = root.Element("avatarFull")?.Value;
        if (avatar is not null && !avatar.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) avatar = null;

        return new SteamAccount(steamId64, name.Trim(), avatar);
    }
}
