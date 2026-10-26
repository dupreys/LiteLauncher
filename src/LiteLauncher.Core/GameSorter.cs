namespace LiteLauncher.Core;

public static class GameSorter
{
    /// <summary>Stable unique key across providers.</summary>
    public static string Key(Game game) => $"{game.Source}:{game.Id}";

    /// <summary>
    /// Most recently played first, then alphabetical. <paramref name="playedNow"/> lets the app say
    /// "this game was started just now" before the store has written the new timestamp to disk;
    /// it only wins when it is newer than what the store reports.
    /// </summary>
    public static List<Game> MostRecentFirst(IEnumerable<Game> games, IReadOnlyDictionary<string, DateTimeOffset>? playedNow = null)
    {
        DateTimeOffset Effective(Game game)
        {
            var stored = game.LastPlayed ?? DateTimeOffset.MinValue;
            return playedNow is not null && playedNow.TryGetValue(Key(game), out var now) && now > stored ? now : stored;
        }

        return games
            .OrderByDescending(Effective)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
