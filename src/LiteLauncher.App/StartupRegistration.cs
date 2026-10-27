using System.IO;
using System.Security;
using Microsoft.Win32;

namespace LiteLauncher.App;

/// <summary>
/// "Start with Windows" for the current user (the Run key; no administrator needed). It uses the same entry as the
/// installer's startup option, so the two never disagree. The Microsoft Store/MSIX build does not use this.
/// </summary>
internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiteLauncher";

    public static bool Supported => !SystemInfo.IsPackaged() && !string.IsNullOrEmpty(Environment.ProcessPath);

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Turns the entry on or off; false when Windows refused.</summary>
    public static bool Set(bool enabled)
    {
        if (!Supported) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null) return false;

            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --fullscreen", RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
