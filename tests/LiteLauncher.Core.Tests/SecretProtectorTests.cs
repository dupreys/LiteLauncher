using LiteLauncher.Core.Settings;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class SecretProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ll-secret-" + Guid.NewGuid().ToString("N"));

    public SecretProtectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Keys_survive_a_save_and_load()
    {
        var store = new SettingsStore(Path.Combine(_dir, "s.json"));
        store.Save(new AppSettings { SteamWebApiKey = "STEAMKEY123", SteamGridDbApiKey = "GRIDKEY456" });

        var loaded = store.Load();
        Assert.Equal("STEAMKEY123", loaded.SteamWebApiKey);
        Assert.Equal("GRIDKEY456", loaded.SteamGridDbApiKey);
    }

    [Fact]
    public void On_windows_the_file_does_not_contain_the_keys()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI only exists on Windows

        var path = Path.Combine(_dir, "s.json");
        new SettingsStore(path).Save(new AppSettings { SteamWebApiKey = "STEAMKEY123", SteamGridDbApiKey = "GRIDKEY456" });

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("STEAMKEY123", text);
        Assert.DoesNotContain("GRIDKEY456", text);
        Assert.Contains(SecretProtector.Prefix, text);
    }

    [Fact]
    public void Plain_text_keys_from_older_versions_still_load()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, """{"SteamWebApiKey":"OLDKEY","Accent":"#112233"}""");

        var loaded = new SettingsStore(path).Load();
        Assert.Equal("OLDKEY", loaded.SteamWebApiKey);
        Assert.Equal("#112233", loaded.Accent);
    }

    [Fact]
    public void A_key_that_cannot_be_decrypted_is_dropped_not_fatal()
    {
        var path = Path.Combine(_dir, "other-pc.json");
        File.WriteAllText(path, """{"SteamGridDbApiKey":"dpapi:AAAA","Accent":"#112233"}""");

        var loaded = new SettingsStore(path).Load();
        Assert.Null(loaded.SteamGridDbApiKey);
        Assert.Equal("#112233", loaded.Accent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_secrets_stay_empty(string? value) => Assert.Equal(value, SecretProtector.Protect(value));
}

public class HomeStyleSettingTests
{
    [Theory]
    [InlineData("Xbox", "Xbox")]
    [InlineData("Simple", "Simple")]
    [InlineData("Cinema", "Cinema")]
    [InlineData("nonsense", "Xbox")]
    [InlineData("", "Xbox")]
    public void Home_style_falls_back_to_xbox(string saved, string expected)
    {
        var settings = new AppSettings { HomeStyle = saved };
        settings.Normalize();
        Assert.Equal(expected, settings.HomeStyle);
    }
}
