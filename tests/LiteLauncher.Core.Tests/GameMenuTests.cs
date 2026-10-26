using LiteLauncher.Core;
using LiteLauncher.Core.Epic;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class GameMenuTests
{
    [Fact]
    public void Override_renames_game_and_keeps_store_name()
    {
        var game = new Game("Steam", "570", "Dota 2");
        var result = GameOverrides.Apply(game, new Dictionary<string, GameOverride>
        {
            ["Steam:570"] = new() { Name = "My Dota" },
        });

        Assert.Equal("My Dota", result.Name);
        Assert.Equal("Dota 2", result.OriginalName);
    }

    [Fact]
    public void Override_with_missing_picture_keeps_store_cover()
    {
        var game = new Game("Steam", "570", "Dota 2") { CoverPath = "store.jpg" };
        var result = GameOverrides.Apply(game, new Dictionary<string, GameOverride>
        {
            ["Steam:570"] = new() { CoverPath = "Z:/does/not/exist.png" },
        });

        Assert.Equal("store.jpg", result.CoverPath);
    }

    [Fact]
    public void Name_equal_to_store_name_means_no_override() =>
        Assert.Null(GameOverrides.NormalizeName("  Dota 2 ", "Dota 2"));

    [Fact]
    public void Steam_store_link_opens_store_page_for_the_app() =>
        Assert.Equal("steam://store/570", GameInfo.StoreUrl(new Game("Steam", "570", "Dota 2")));

    [Fact]
    public void Epic_manifest_is_read_and_dlc_is_skipped()
    {
        var game = EpicProvider.ParseManifest("""
            {"AppName":"Fortnite","MainGameAppName":"Fortnite","DisplayName":"Fortnite",
             "CatalogNamespace":"fn","CatalogItemId":"abc","InstallLocation":"C:\\Games\\Fortnite",
             "LaunchExecutable":"FortniteLauncher.exe","InstallSize":1073741824}
            """);
        Assert.NotNull(game);
        Assert.Equal("Epic", game!.Source);
        Assert.Equal("com.epicgames.launcher://apps/fn%3Aabc%3AFortnite?action=launch&silent=true", EpicProvider.LaunchUri(game));

        Assert.Null(EpicProvider.ParseManifest("""{"AppName":"DlcX","MainGameAppName":"Fortnite","DisplayName":"DLC"}"""));
    }

    [Fact]
    public void Steam_store_json_gives_developer_genres_and_description()
    {
        var d = GameInfo.ParseSteamStore("""
            {"570":{"success":true,"data":{"short_description":"Fight &amp; win","developers":["Valve"],
              "release_date":{"date":"9 Jul, 2013"},"genres":[{"description":"Action"},{"description":"Strategy"}]}}}
            """, "570");
        Assert.Equal("Valve", d!.Developer);
        Assert.Equal("Fight & win", d.Description);
        Assert.Equal(new[] { "Action", "Strategy" }, d.Genres);
    }

    [Fact]
    public void Playtime_is_read_from_localconfig()
    {
        const string vdf = "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"570\" { \"Playtime\" \"277\" } } } } } }";
        Assert.Equal(277, SteamPlaytime.ParseLocalConfig(vdf, "570"));
        Assert.Equal("4 hrs 37 min", GameInfo.FormatPlaytime(277));
    }
}

public class PlayTrackerTests
{
    [Fact]
    public void Session_reports_start_and_minutes_on_stop()
    {
        var tracker = new PlayTracker();
        var t0 = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(new[] { "Epic:Fortnite" }, tracker.Update(new[] { "Epic:Fortnite" }, t0).Started);
        Assert.Empty(tracker.Update(new[] { "Epic:Fortnite" }, t0.AddMinutes(10)).Started);

        var stopped = tracker.Update(Array.Empty<string>(), t0.AddMinutes(42)).Stopped;
        Assert.Equal(("Epic:Fortnite", 42), Assert.Single(stopped));
    }

    [Fact]
    public void Recorded_last_played_wins_when_newer()
    {
        var played = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var game = PlayTracker.Apply(new Game("Epic", "Fortnite", "Fortnite"),
            new Dictionary<string, PlayRecord> { ["Epic:Fortnite"] = new() { LastPlayed = played } });
        Assert.Equal(played, game.LastPlayed);
    }
}
