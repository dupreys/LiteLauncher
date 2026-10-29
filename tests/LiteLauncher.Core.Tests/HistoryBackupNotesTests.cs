using LiteLauncher.Core;
using LiteLauncher.Core.Manual;
using LiteLauncher.Core.Settings;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class HistoryBackupNotesTests
{
    [Fact]
    public void Sessions_shorter_than_a_minute_are_ignored_and_the_list_is_capped()
    {
        var list = new List<PlaySession>();
        Assert.False(PlayHistory.Add(list, "Steam:1", "A", DateTimeOffset.Now, 0));
        for (var i = 0; i < PlayHistory.MaxSessions + 10; i++) PlayHistory.Add(list, "Steam:1", "A", DateTimeOffset.Now.AddMinutes(i), 5);
        Assert.Equal(PlayHistory.MaxSessions, list.Count);
    }

    [Fact]
    public void Minutes_are_summed_per_local_day()
    {
        var today = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local);
        var sessions = new[]
        {
            new PlaySession { Key = "a", Name = "A", Start = new DateTimeOffset(today.AddHours(-2)), Minutes = 30 },
            new PlaySession { Key = "a", Name = "A", Start = new DateTimeOffset(today.AddDays(-1)), Minutes = 45 },
            new PlaySession { Key = "b", Name = "B", Start = new DateTimeOffset(today.AddDays(-20)), Minutes = 99 },
        };
        var week = PlayHistory.MinutesPerDay(sessions, today, 7);
        Assert.Equal(7, week.Length);
        Assert.Equal(30, week[6]);
        Assert.Equal(45, week[5]);
        Assert.Equal(75, PlayHistory.TotalMinutes(sessions, today, 7));
        Assert.Equal("b", PlayHistory.TotalsByGame(sessions)[0].Key);
    }

    [Fact]
    public void Clean_drops_damaged_sessions()
    {
        var cleaned = PlayHistory.Clean(new[]
        {
            new PlaySession { Key = "a", Minutes = 10, Start = DateTimeOffset.Now },
            new PlaySession { Key = "", Minutes = 10, Start = DateTimeOffset.Now },
            new PlaySession { Key = "b", Minutes = -3, Start = DateTimeOffset.Now },
            new PlaySession { Key = "c", Minutes = 10, Start = default },
        });
        Assert.Equal("a", Assert.Single(cleaned).Key);
    }

    [Fact]
    public void Reads_the_playtime_of_every_steam_game_at_once()
    {
        const string vdf = """
            "UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "apps" {
              "440" { "Playtime" "125" } "570" { "Playtime" "0" } "abc" { "Playtime" "9" } } } } } }
            """;
        var all = SteamPlaytime.ParseAllLocalConfig(vdf);
        Assert.Equal(125, Assert.Single(all).Value);
    }

    [Fact]
    public void Backup_never_contains_keys_and_import_never_brings_them_in()
    {
        var settings = new AppSettings { Accent = "#FF0000", SteamWebApiKey = "SECRET1", SteamGridDbApiKey = "SECRET2", HomeStyle = "Cinema" };
        settings.ManualGames.Add(new ManualGame { Id = "1", Name = "G", ExePath = "C:\\g.exe" });
        var json = SettingsBackup.Export(settings);
        Assert.DoesNotContain("SECRET", json);

        var imported = SettingsBackup.Import(json)!;
        Assert.Equal("Cinema", imported.HomeStyle);
        Assert.Single(imported.ManualGames);

        var target = new AppSettings { SteamWebApiKey = "KEEP" };
        SettingsBackup.ApplyTo(target, imported);
        Assert.Equal("#FF0000", target.Accent);
        Assert.Equal("KEEP", target.SteamWebApiKey);

        // a hand-edited file with a key in it
        var tampered = json.Replace("\"Accent\"", "\"SteamWebApiKey\": \"EVIL\", \"Accent\"");
        Assert.Null(SettingsBackup.Import(tampered)!.SteamWebApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"settings\":{}}")]
    [InlineData("{\"format\":\"LiteLauncher-settings\",\"version\":99,\"settings\":{}}")]
    public void Import_rejects_anything_that_is_not_a_backup(string? json) => Assert.Null(SettingsBackup.Import(json));

    [Fact]
    public void Release_notes_become_plain_text()
    {
        const string json = """{"tag_name":"v1.4.0","name":"Big one","html_url":"https://github.com/o/r/releases/tag/v1.4.0","body":"## What's new\r\n- **Fast** start\r\n- See [docs](https://x.y)\r\n\r\n\r\n\r\n`code`"}""";
        var info = ReleaseNotes.Parse(json)!;
        Assert.Equal("v1.4.0", info.Tag);
        Assert.Contains("•  Fast start", info.Body);
        Assert.Contains("See docs", info.Body);
        Assert.DoesNotContain("**", info.Body);
        Assert.DoesNotContain("\n\n\n", info.Body);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v1\",\"html_url\":\"https://evil.example/x\",\"body\":\"hi\"}")]
    [InlineData("{\"tag_name\":\"v1\",\"html_url\":\"https://github.com/o/r\",\"body\":\"\"}")]
    [InlineData("[]")]
    public void Release_notes_ignore_bad_answers(string json) => Assert.Null(ReleaseNotes.Parse(json));

    [Fact]
    public void Diagnostics_hide_the_user_name_and_keep_the_tail_short()
    {
        var text = DiagnosticsReport.Redact(@"C:\Users\Bruno\AppData\x", "Bruno", @"C:\Users\Bruno");
        Assert.Equal(@"%USERPROFILE%\AppData\x", text);
        Assert.True(DiagnosticsReport.Tail(new string('x', 5000) + "\nlast", 100).EndsWith("last"));
        var report = DiagnosticsReport.Build(new[] { ("Steam", (IReadOnlyList<(string, string)>)new[] { ("Path", "C:\\s"), ("Games", "3") }) }, null);
        Assert.Contains("STEAM", report);
        Assert.Contains("none recorded", report);
    }
}
