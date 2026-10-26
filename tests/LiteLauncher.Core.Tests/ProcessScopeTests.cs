using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class ProcessScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Games")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Windows\System32")]
    public void Unsafe_roots_are_refused(string? path)
    {
        if (!OperatingSystem.IsWindows()) return; // drive-letter paths only make sense on Windows
        Assert.False(ProcessScope.IsSafeInstallRoot(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Hades")]
    [InlineData(@"D:\Games\Hades")]
    public void A_single_game_folder_is_accepted(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(ProcessScope.IsSafeInstallRoot(path));
    }

    [Fact]
    public void IsInside_matches_the_folder_and_its_children_only()
    {
        var folder = Path.Combine(Path.GetTempPath(), "games", "hades");
        Assert.True(ProcessScope.IsInside(folder, Path.Combine(folder, "bin", "hades.exe")));
        Assert.True(ProcessScope.IsInside(folder, folder));
        Assert.False(ProcessScope.IsInside(folder, folder + "-extra" + Path.DirectorySeparatorChar + "a.exe")); // sibling with same prefix
        Assert.False(ProcessScope.IsInside(folder, Path.Combine(Path.GetTempPath(), "games", "other.exe")));
        Assert.False(ProcessScope.IsInside(folder, ""));
    }

    [Fact]
    public void Resolve_prefers_install_path_then_falls_back_to_the_exe_folder()
    {
        if (!OperatingSystem.IsWindows()) return;

        var withPath = new Game("Epic", "a", "A") { InstallPath = @"D:\Games\A", ExecutablePath = @"D:\Games\A\bin\a.exe" };
        var exeOnly = new Game("Epic", "b", "B") { ExecutablePath = @"D:\Games\B\bin\b.exe" };
        var nothing = new Game("Epic", "c", "C") { InstallPath = @"D:\" };

        Assert.Equal(@"D:\Games\A", ProcessScope.Resolve(withPath));
        Assert.Equal(@"D:\Games\B\bin", ProcessScope.Resolve(exeOnly));
        Assert.Null(ProcessScope.Resolve(nothing));
    }
}
