using LiteLauncher.Core.Settings;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class WallpaperSettingTests
{
    [Theory]
    [InlineData("preset:aurora", "preset:aurora")]
    [InlineData("file:C:\\x\\a.png", "file:C:\\x\\a.png")]
    [InlineData("  ", "")]
    [InlineData("whatever", "")]
    public void Wallpaper_value_is_normalized(string input, string expected)
    {
        var s = new AppSettings { Wallpaper = input };
        s.Normalize();
        Assert.Equal(expected, s.Wallpaper);
    }
}
