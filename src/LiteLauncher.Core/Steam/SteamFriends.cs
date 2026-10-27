using System.Text.Json;

namespace LiteLauncher.Core.Steam;

public enum FriendState { Offline = 0, Online = 1, Busy = 2, Away = 3, Snooze = 4, LookingToTrade = 5, LookingToPlay = 6 }

public sealed record SteamFriend(string SteamId, string Name, string? AvatarUrl, FriendState State, string? Game)
{
    public string StatusText => Game is not null
        ? Loc.F("Playing {0}", Game)
        : State switch
        {
            FriendState.Busy => Loc.T("Busy"),
            FriendState.Away or FriendState.Snooze => Loc.T("Away"),
            FriendState.Offline => Loc.T("Offline"),
            _ => Loc.T("Online"),
        };
}

/// <summary>
/// Your online Steam friends through the official Steam Web API. Needs the user's own Web API key
/// (steamcommunity.com/dev/apikey) and a signed-in SteamID. Chat itself happens in the Steam client
/// (steam://friends/message/&lt;id&gt;); nothing here can send messages.
/// </summary>
public static class SteamFriends
{
    private const string Api = "https://api.steampowered.com";

    public static async Task<IReadOnlyList<SteamFriend>> FetchOnlineAsync(HttpClient http, string apiKey, string steamId64,
                                                                          CancellationToken ct = default)
    {
        var key = Uri.EscapeDataString(apiKey.Trim());
        using var listResponse = await http.GetAsync($"{Api}/ISteamUser/GetFriendList/v1/?key={key}&steamid={steamId64}&relationship=friend", ct);
        if (listResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            // 401 happens both for a bad key and for a private friends list
            throw new UnauthorizedAccessException("Steam refused the request.");
        }
        listResponse.EnsureSuccessStatusCode();
        var ids = ParseFriendIds(await listResponse.Content.ReadAsStringAsync(ct));

        var friends = new List<SteamFriend>();
        foreach (var batch in ids.Chunk(100)) // GetPlayerSummaries takes up to 100 ids
        {
            var json = await http.GetStringAsync(
                $"{Api}/ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={string.Join(',', batch)}", ct);
            friends.AddRange(ParseSummaries(json));
        }
        return SortOnline(friends);
    }

    /// <summary>Online only; in-game first, then online, then away/busy; alphabetical within each.</summary>
    public static IReadOnlyList<SteamFriend> SortOnline(IEnumerable<SteamFriend> friends) => friends
        .Where(f => f.State != FriendState.Offline)
        .OrderBy(f => f.Game is not null ? 0 : f.State is FriendState.Online or FriendState.LookingToPlay or FriendState.LookingToTrade ? 1 : 2)
        .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static IReadOnlyList<string> ParseFriendIds(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("friendslist", out var fl) ||
                !fl.TryGetProperty("friends", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return arr.EnumerateArray()
                .Select(f => f.TryGetProperty("steamid", out var id) ? id.GetString() : null)
                .Where(SteamLoginUsers.IsSteamId64)
                .Select(id => id!)
                .ToList();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<SteamFriend> ParseSummaries(string json)
    {
        var list = new List<SteamFriend>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r) ||
                !r.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return list;

            foreach (var p in players.EnumerateArray())
            {
                string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = Str("steamid");
                if (id is null) continue;
                var state = p.TryGetProperty("personastate", out var s) && s.TryGetInt32(out var n) && n is >= 0 and <= 6
                    ? (FriendState)n
                    : FriendState.Offline;
                list.Add(new SteamFriend(id, Str("personaname") ?? "Steam user", Str("avatarmedium") ?? Str("avatar"),
                                         state, Str("gameextrainfo")));
            }
        }
        catch (JsonException) { }
        return list;
    }
}
