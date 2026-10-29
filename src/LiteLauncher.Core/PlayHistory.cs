namespace LiteLauncher.Core;

/// <summary>One finished play session.</summary>
public sealed class PlaySession
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public int Minutes { get; set; }
}

/// <summary>Pure helpers over the list of sessions (kept in the settings file).</summary>
public static class PlayHistory
{
    public const int MaxSessions = 500;

    /// <summary>Adds a session (ignored when shorter than a minute) and drops the oldest beyond the cap.</summary>
    public static bool Add(IList<PlaySession> sessions, string key, string name, DateTimeOffset start, int minutes)
    {
        if (minutes < 1 || string.IsNullOrWhiteSpace(key)) return false;
        sessions.Add(new PlaySession { Key = key, Name = (name ?? "").Trim(), Start = start, Minutes = Math.Min(minutes, 24 * 60) });
        while (sessions.Count > MaxSessions) sessions.RemoveAt(0);
        return true;
    }

    /// <summary>Removes damaged entries (a hand-edited file) and enforces the cap.</summary>
    public static List<PlaySession> Clean(IEnumerable<PlaySession>? sessions)
    {
        var list = (sessions ?? Enumerable.Empty<PlaySession>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Key) && s.Minutes > 0 && s.Minutes <= 24 * 60 && s.Start.Year >= 2000)
            .OrderBy(s => s.Start)
            .ToList();
        if (list.Count > MaxSessions) list.RemoveRange(0, list.Count - MaxSessions);
        return list;
    }

    /// <summary>Minutes played per local day, oldest first; the last entry is <paramref name="today"/>.</summary>
    public static int[] MinutesPerDay(IEnumerable<PlaySession> sessions, DateTime today, int days)
    {
        var result = new int[days];
        var first = today.Date.AddDays(-(days - 1));
        foreach (var s in sessions)
        {
            var index = (s.Start.ToLocalTime().Date - first).Days;
            if (index >= 0 && index < days) result[index] += s.Minutes;
        }
        return result;
    }

    public static int TotalMinutes(IEnumerable<PlaySession> sessions, DateTime today, int days) =>
        MinutesPerDay(sessions, today, days).Sum();

    /// <summary>Totals per game from the sessions alone (name = the most recent one used).</summary>
    public static IReadOnlyList<(string Key, string Name, int Minutes)> TotalsByGame(IEnumerable<PlaySession> sessions) =>
        sessions.GroupBy(s => s.Key)
                .Select(g => (g.Key, g.OrderBy(s => s.Start).Last().Name, g.Sum(s => s.Minutes)))
                .OrderByDescending(t => t.Item3)
                .ToList();

    /// <summary>"2 h 05 min" style text, locale-free (the caller localises the units).</summary>
    public static (int Hours, int Minutes) Split(int minutes) => (minutes / 60, minutes % 60);
}
