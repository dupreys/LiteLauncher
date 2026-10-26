namespace LiteLauncher.Core;

/// <summary>What LiteLauncher itself recorded for stores that don't expose play history (Epic).</summary>
public sealed class PlayRecord
{
    public DateTimeOffset? LastPlayed { get; set; }
    public int Minutes { get; set; }
}

/// <summary>
/// Turns "is it running?" samples into sessions. Feed it every poll; it reports when a game starts and,
/// when it stops, how many minutes the session lasted. Pure logic, so it is testable without processes.
/// </summary>
public sealed class PlayTracker
{
    private readonly Dictionary<string, DateTimeOffset> _running = new();

    public IReadOnlyCollection<string> Running => _running.Keys;

    /// <returns>Keys that just started, and finished sessions with their length in whole minutes.</returns>
    public (IReadOnlyList<string> Started, IReadOnlyList<(string Key, int Minutes)> Stopped) Update(
        IEnumerable<string> runningNow, DateTimeOffset now)
    {
        var current = new HashSet<string>(runningNow);
        var started = new List<string>();
        var stopped = new List<(string, int)>();

        foreach (var key in current)
            if (_running.TryAdd(key, now)) started.Add(key);

        foreach (var (key, since) in _running.ToList())
        {
            if (current.Contains(key)) continue;
            _running.Remove(key);
            stopped.Add((key, Math.Max(0, (int)Math.Round((now - since).TotalMinutes))));
        }

        return (started, stopped);
    }

    /// <summary>Applies what LiteLauncher recorded: newer last-played wins over the store's value.</summary>
    public static Game Apply(Game game, IReadOnlyDictionary<string, PlayRecord>? records)
    {
        if (records is null || !records.TryGetValue(GameSorter.Key(game), out var r) || r.LastPlayed is not { } played)
            return game;
        return game.LastPlayed is { } stored && stored >= played ? game : game with { LastPlayed = played };
    }
}
