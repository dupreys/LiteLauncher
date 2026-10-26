using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public sealed class SteamLibraryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "litelauncher-" + Guid.NewGuid().ToString("N"));

    public SteamLibraryReaderTests() => Directory.CreateDirectory(Path.Combine(_root, "Steam", "steamapps"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string SteamPath => Path.Combine(_root, "Steam");

    private static string Manifest(string appId, string name, int stateFlags, long lastPlayed = 0) => $$"""
        "AppState"
        {
            "appid" "{{appId}}"
            "name" "{{name}}"
            "StateFlags" "{{stateFlags}}"
            "installdir" "{{name}}"
            "LastPlayed" "{{lastPlayed}}"
            "SizeOnDisk" "1000"
        }
        """;

    [Fact]
    public void Install_path_is_the_common_folder_next_to_the_manifest()
    {
        var apps = Path.Combine(SteamPath, "steamapps");
        File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), Manifest("1", "Hades", 4));

        var game = Assert.Single(SteamLibraryReader.ReadInstalledGames(SteamPath));

        Assert.Equal(Path.Combine(apps, "common", "Hades"), game.InstallPath);
    }

    [Fact]
    public void Reads_only_fully_installed_games_and_skips_tools()
    {
        var apps = Path.Combine(SteamPath, "steamapps");
        File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), Manifest("1", "Installed Game", 4));
        File.WriteAllText(Path.Combine(apps, "appmanifest_2.acf"), Manifest("2", "Downloading Game", 1026));
        File.WriteAllText(Path.Combine(apps, "appmanifest_228980.acf"), Manifest("228980", "Steamworks Common Redistributables", 4));

        var games = SteamLibraryReader.ReadInstalledGames(SteamPath);

        var game = Assert.Single(games);
        Assert.Equal("Installed Game", game.Name);
        Assert.Equal("Steam", game.Source);
    }

    [Fact]
    public void Follows_extra_library_folders_and_sorts_by_last_played()
    {
        var extra = Path.Combine(_root, "SteamLibrary");
        Directory.CreateDirectory(Path.Combine(extra, "steamapps"));

        var escapedExtra = extra.Replace("\\", "\\\\");
        var escapedSteam = SteamPath.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{escapedSteam}}" }
                "1" { "path" "{{escapedExtra}}" }
            }
            """);

        File.WriteAllText(Path.Combine(SteamPath, "steamapps", "appmanifest_10.acf"), Manifest("10", "Old", 4, lastPlayed: 1000));
        File.WriteAllText(Path.Combine(extra, "steamapps", "appmanifest_20.acf"), Manifest("20", "Recent", 4, lastPlayed: 2000));

        var games = SteamLibraryReader.ReadInstalledGames(SteamPath);

        Assert.Equal(new[] { "Recent", "Old" }, games.Select(g => g.Name));
    }

    [Fact]
    public void Finds_cover_in_old_and_new_cache_layouts()
    {
        var cache = Path.Combine(SteamPath, "appcache", "librarycache");
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "7_library_600x900.jpg"), new byte[] { 1 });

        var hashed = Path.Combine(cache, "8", "abc123");
        Directory.CreateDirectory(hashed);
        File.WriteAllBytes(Path.Combine(hashed, "library_600x900.jpg"), new byte[] { 1 });

        Assert.EndsWith("7_library_600x900.jpg", SteamArtwork.FindCover(SteamPath, "7"));
        Assert.EndsWith("library_600x900.jpg", SteamArtwork.FindCover(SteamPath, "8"));
        Assert.Null(SteamArtwork.FindCover(SteamPath, "9"));
    }
}
