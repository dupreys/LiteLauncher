using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LiteLauncher.Core.Steam;

/// <summary>
/// Which Steam game is running right now. The Steam client publishes it in the registry
/// (HKCU\Software\Valve\Steam\RunningAppID), so this works however the game was started:
/// from this launcher, from Steam itself, or from a shortcut.
/// </summary>
public static class SteamRunningApp
{
    /// <summary>The running app id, or 0 when no Steam game is running (or this isn't Windows).</summary>
    public static int GetRunningAppId() => OperatingSystem.IsWindows() ? Read() : 0;

    [SupportedOSPlatform("windows")]
    private static int Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("RunningAppID") is int id && id > 0 ? id : 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
