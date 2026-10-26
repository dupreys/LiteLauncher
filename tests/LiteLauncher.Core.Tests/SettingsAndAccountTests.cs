using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public sealed class SettingsAndAccountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "litelauncher-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(Path.Combine(_dir, "nested", "settings.json"));
        store.Save(new AppSettings { Accent = "#E5484D", UiVolume = 0.8, SteamId = "76561198000000000", PersonaName = "Tester" });

        var loaded = store.Load();

        Assert.Equal("#E5484D", loaded.Accent);
        Assert.Equal(0.8, loaded.UiVolume);
        Assert.Equal("Tester", loaded.PersonaName);
    }

    [Fact]
    public void Missing_or_corrupt_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal("#4C9AFF", new SettingsStore(path).Load().Accent);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ this is not json");
        Assert.Equal("#4C9AFF", new SettingsStore(path).Load().Accent);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var settings = new AppSettings { BackdropDim = 5, UiVolume = -1 };
        settings.Normalize();

        Assert.Equal(0.9, settings.BackdropDim);
        Assert.Equal(0, settings.UiVolume);
    }

    [Fact]
    public void Custom_profile_is_trimmed_capped_and_persisted()
    {
        var settings = new AppSettings { CustomName = "  " + new string('x', 40) + "  ", CustomAvatarPath = " " };
        settings.Normalize();

        Assert.Equal(AppSettings.MaxNameLength, settings.CustomName!.Length);
        Assert.Null(settings.CustomAvatarPath);

        var blank = new AppSettings { CustomName = "   " };
        blank.Normalize();
        Assert.Null(blank.CustomName);

        var store = new SettingsStore(Path.Combine(_dir, "profile.json"));
        store.Save(new AppSettings { CustomName = "Bruno", CustomAvatarPath = @"C:\pics\me.png" });
        var loaded = store.Load();
        Assert.Equal("Bruno", loaded.CustomName);
        Assert.Equal(@"C:\pics\me.png", loaded.CustomAvatarPath);
    }

    [Fact]
    public void LoginUsers_prefers_most_recent_account()
    {
        const string vdf = """
            "users"
            {
                "76561198000000001" { "AccountName" "old" "PersonaName" "Old Me" "MostRecent" "0" "Timestamp" "999" }
                "76561198000000002" { "AccountName" "new" "PersonaName" "New Me" "MostRecent" "1" "Timestamp" "100" }
            }
            """;

        var account = SteamLoginUsers.Parse(vdf);

        Assert.NotNull(account);
        Assert.Equal("76561198000000002", account!.SteamId64);
        Assert.Equal("New Me", account.PersonaName);
    }

    [Fact]
    public void Profile_xml_is_parsed_and_non_https_avatars_are_dropped()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <profile>
              <steamID64>76561198000000002</steamID64>
              <steamID><![CDATA[ New Me ]]></steamID>
              <avatarFull><![CDATA[https://avatars.steamstatic.com/abc_full.jpg]]></avatarFull>
            </profile>
            """;

        var account = SteamProfile.ParseProfileXml(xml, "76561198000000002");

        Assert.Equal("New Me", account!.PersonaName);
        Assert.Equal("https://avatars.steamstatic.com/abc_full.jpg", account.AvatarUrl);

        var insecure = xml.Replace("https://", "http://");
        Assert.Null(SteamProfile.ParseProfileXml(insecure, "76561198000000002")!.AvatarUrl);

        Assert.Null(SteamProfile.ParseProfileXml("<response><error>private</error></response>", "76561198000000002"));
    }

    [Theory]
    [InlineData("https://steamcommunity.com/openid/id/76561198000000002", "76561198000000002")]
    [InlineData("https://evil.example/openid/id/76561198000000002", null)]
    [InlineData("https://steamcommunity.com/openid/id/123", null)]
    [InlineData(null, null)]
    public void OpenId_claimed_id_is_validated(string? claimed, string? expected)
    {
        Assert.Equal(expected, SteamOpenId.ParseClaimedId(claimed));
    }

    [Fact]
    public void OpenId_login_url_targets_steam_with_our_return_address()
    {
        var url = SteamOpenId.BuildLoginUrl("http://127.0.0.1:5000/steam-callback", "http://127.0.0.1:5000/");

        Assert.StartsWith("https://steamcommunity.com/openid/login?", url);
        Assert.Contains("openid.mode=checkid_setup", url);
        Assert.Contains(Uri.EscapeDataString("http://127.0.0.1:5000/steam-callback"), url);
    }

    [Fact]
    public void Backdrop_prefers_hero_and_skips_blurred_variant()
    {
        var steam = Path.Combine(_dir, "Steam");
        var folder = Path.Combine(steam, "appcache", "librarycache", "42", "hash");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "library_hero_blur.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder, "library_hero.jpg"), new byte[] { 1 });

        var path = SteamArtwork.FindBackdrop(steam, "42");

        Assert.EndsWith("library_hero.jpg", path);
    }
}
