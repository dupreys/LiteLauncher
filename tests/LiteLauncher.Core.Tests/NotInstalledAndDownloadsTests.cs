using LiteLauncher.Core;
using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class NotInstalledAndDownloadsTests
{
    [Fact]
    public void Reads_owned_games_from_the_web_api_answer()
    {
        const string json = """{"response":{"game_count":3,"games":[{"appid":440,"name":"Team Fortress 2"},{"appid":228980,"name":"Steamworks Common Redistributables"},{"appid":0,"name":"bad"},{"appid":570}]}}""";
        var list = SteamOwnedGames.ParseApi(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("440", list[0].AppId);
        Assert.Empty(SteamOwnedGames.ParseApi("nope"));
    }

    [Fact]
    public void Reads_owned_games_from_the_public_profile_xml()
    {
        const string xml = "<gamesList><games><game><appID>10</appID><name>Counter-Strike</name></game><game><appID>x</appID><name>Bad</name></game></games></gamesList>";
        var list = SteamOwnedGames.ParseXml(xml);
        var game = Assert.Single(list);
        Assert.Equal("Counter-Strike", game.Name);
        Assert.Empty(SteamOwnedGames.ParseXml("<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><a>&x;</a>"));
    }

    [Fact]
    public void Not_installed_leaves_out_installed_and_non_games()
    {
        var owned = new[] { new OwnedGame("1", "Zeta"), new OwnedGame("2", "Alpha"), new OwnedGame("3", "Installed"), new OwnedGame("228980", "Steamworks Common Redistributables") };
        var result = SteamOwnedGames.NotInstalled(owned, new HashSet<string> { "3" }, null);
        Assert.Equal(new[] { "Alpha", "Zeta" }, result.Select(g => g.Name));
        Assert.All(result, g => Assert.True(g.NotInstalled));
    }

    private static string Acf(long flags, long done = 0, long total = 0) => $$"""
        "AppState" { "appid" "570" "name" "Dota 2" "StateFlags" "{{flags}}" "BytesDownloaded" "{{done}}" "BytesToDownload" "{{total}}" }
        """;

    [Fact]
    public void A_new_install_in_progress_is_a_download()
    {
        var d = SteamDownloads.ParseManifest(Acf(1026, 250, 1000))!;
        Assert.Equal("570", d.AppId);
        Assert.Equal(0.25, d.Fraction, 3);
        Assert.False(d.IsUpdate);
        Assert.Equal(DownloadStatus.Downloading, d.Status);
    }

    [Fact]
    public void Paused_installed_and_idle_manifests_are_told_apart()
    {
        Assert.Equal(DownloadStatus.Paused, SteamDownloads.ParseManifest(Acf(2 | 512, 1, 10))!.Status);
        Assert.Null(SteamDownloads.ParseManifest(Acf(4)));          // installed, nothing going on
        Assert.Null(SteamDownloads.ParseManifest(Acf(4 | 2)));      // update pending but not started
        Assert.True(SteamDownloads.ParseManifest(Acf(4 | 1024, 5, 10))!.IsUpdate);
    }

    [Fact]
    public void Install_uri_accepts_only_numeric_ids()
    {
        Assert.Equal("steam://install/570", SteamDownloads.InstallUri("570"));
        Assert.Throws<ArgumentException>(() => SteamDownloads.InstallUri("570 -applaunch calc"));
        Assert.Equal("1.5 KB", SteamDownloads.FormatBytes(1536));
    }

    [Fact]
    public void SteamGridDb_art_only_comes_from_its_own_https_hosts()
    {
        Assert.True(SteamGridDbArt.IsAllowedUrl("https://cdn2.steamgriddb.com/grid/a.png"));
        Assert.False(SteamGridDbArt.IsAllowedUrl("http://cdn2.steamgriddb.com/grid/a.png"));
        Assert.False(SteamGridDbArt.IsAllowedUrl("https://evilsteamgriddb.com/a.png"));
        var picked = SteamGridDbArt.Pick(new[]
        {
            new ArtImage("https://evil.example/a.png", "", 600, 900),
            new ArtImage("https://cdn2.steamgriddb.com/wide.png", "", 920, 430),
            new ArtImage("https://cdn2.steamgriddb.com/tall.png", "", 600, 900),
        }, StoreArtKind.Cover);
        Assert.EndsWith("tall.png", picked!.Url);
    }

    [Fact]
    public void Only_epic_and_manual_games_get_automatic_steamgriddb_art()
    {
        Assert.True(SteamGridDbArt.Applies(new Game("Epic", "x", "X")));
        Assert.True(SteamGridDbArt.Applies(new Game("Manual", "x", "X")));
        Assert.False(SteamGridDbArt.Applies(new Game("Steam", "1", "X")));
    }
}
