using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public sealed class LiveUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "litelauncher-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Last_played_orders_the_library_and_never_played_goes_last()
    {
        var older = new Game("Steam", "1", "Old", DateTimeOffset.FromUnixTimeSeconds(1000));
        var newer = new Game("Steam", "2", "New", DateTimeOffset.FromUnixTimeSeconds(2000));
        var never = new Game("Steam", "3", "Never");

        var ordered = GameSorter.MostRecentFirst(new[] { older, never, newer });

        Assert.Equal(new[] { "2", "1", "3" }, ordered.Select(g => g.Id));
    }

    [Fact]
    public void Played_now_moves_a_game_to_the_top_but_never_overrides_a_newer_stored_time()
    {
        var older = new Game("Steam", "1", "Old", DateTimeOffset.FromUnixTimeSeconds(1000));
        var newer = new Game("Steam", "2", "New", DateTimeOffset.FromUnixTimeSeconds(2000));
        var games = new[] { older, newer };

        var promote = new Dictionary<string, DateTimeOffset> { [GameSorter.Key(older)] = DateTimeOffset.FromUnixTimeSeconds(3000) };
        Assert.Equal("1", GameSorter.MostRecentFirst(games, promote).First().Id);

        var stale = new Dictionary<string, DateTimeOffset> { [GameSorter.Key(older)] = DateTimeOffset.FromUnixTimeSeconds(500) };
        Assert.Equal("2", GameSorter.MostRecentFirst(games, stale).First().Id);
    }

    [Fact]
    public async Task Watcher_reports_manifest_changes_and_ignores_other_files()
    {
        var steam = Path.Combine(_root, "Steam");
        var steamapps = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(steamapps);

        using var watcher = new SteamLibraryWatcher(steam);
        var fired = new TaskCompletionSource();
        watcher.Changed += () => fired.TrySetResult();

        File.WriteAllText(Path.Combine(steamapps, "notes.txt"), "not a manifest");
        var first = await Task.WhenAny(fired.Task, Task.Delay(700));
        Assert.NotSame(fired.Task, first);

        File.WriteAllText(Path.Combine(steamapps, "appmanifest_1.acf"), "\"AppState\" { }");
        var second = await Task.WhenAny(fired.Task, Task.Delay(5000));
        Assert.Same(fired.Task, second);
    }
}
