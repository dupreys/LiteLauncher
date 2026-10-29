using LiteLauncher.Core;
using LiteLauncher.Core.Manual;
using LiteLauncher.Core.Settings;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class ManualGameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ll-manual-" + Guid.NewGuid().ToString("N"));

    public ManualGameTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Exe(string name = "my_cool-game.exe")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Adds_a_game_with_the_given_name_and_rejects_duplicates()
    {
        var list = new List<ManualGame>();
        var exe = Exe();
        var game = ManualProvider.Add(list, exe, "  My Game  ");
        Assert.NotNull(game);
        Assert.Equal("My Game", game!.Name);
        Assert.Single(list);
        Assert.Null(ManualProvider.Add(list, exe, "Again"));
        Assert.Single(list);
    }

    [Fact]
    public void Empty_name_falls_back_to_the_file_name()
    {
        var list = new List<ManualGame>();
        var game = ManualProvider.Add(list, Exe(), "");
        Assert.Equal("my cool game", game!.Name);
    }

    [Theory]
    [InlineData("game.txt")]
    [InlineData("missing.exe")]
    public void Only_existing_exe_files_are_accepted(string name)
    {
        var path = Path.Combine(_dir, name);
        if (name.EndsWith(".txt")) File.WriteAllText(path, "x");
        Assert.False(ManualProvider.IsValidExecutable(path));
        Assert.Null(ManualProvider.Add(new List<ManualGame>(), path, "x"));
    }

    [Fact]
    public async Task Provider_lists_games_whose_exe_still_exists()
    {
        var present = new ManualGame { Id = "a", Name = "A", ExePath = Exe("a.exe") };
        var gone = new ManualGame { Id = "b", Name = "B", ExePath = Path.Combine(_dir, "b.exe") };
        var provider = new ManualProvider(() => new[] { present, gone });
        var games = await provider.GetGamesAsync();
        var game = Assert.Single(games);
        Assert.Equal("Manual", game.Source);
        Assert.Equal(present.ExePath, game.ExecutablePath);
        Assert.Equal(_dir, game.InstallPath);
    }

    [Fact]
    public void Launch_refuses_other_sources_and_missing_files()
    {
        var provider = new ManualProvider(() => Array.Empty<ManualGame>());
        Assert.Throws<ArgumentException>(() => provider.Launch(new Game("Steam", "1", "x")));
        Assert.Throws<FileNotFoundException>(() => provider.Launch(new Game("Manual", "1", "x") { ExecutablePath = Path.Combine(_dir, "no.exe") }));
    }

    [Fact]
    public void Normalize_drops_broken_entries()
    {
        var s = new AppSettings();
        s.ManualGames.Add(new ManualGame { Id = "1", Name = "ok", ExePath = "C:\\a.exe" });
        s.ManualGames.Add(new ManualGame { Id = "1", Name = "dup", ExePath = "C:\\b.exe" });
        s.ManualGames.Add(new ManualGame { Id = "", Name = "noid", ExePath = "C:\\c.exe" });
        s.ManualGames.Add(new ManualGame { Id = "2", Name = "nopath", ExePath = " " });
        s.Normalize();
        Assert.Equal("ok", Assert.Single(s.ManualGames).Name);
        Assert.True(s.UseBackgroundColor);
    }
}
