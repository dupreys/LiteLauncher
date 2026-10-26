using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LiteLauncher.Core.Steam;

public static class SteamLocator
{
    /// <summary>Returns the Steam install directory, or null if Steam isn't found.</summary>
    public static string? FindSteamPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = FindInRegistry();
            if (fromRegistry is not null) return fromRegistry;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var root in new[] { programFilesX86, programFiles })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, "Steam");
            if (IsSteamDirectory(candidate)) return candidate;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindInRegistry()
    {
        // Per-user path is the most reliable (follows custom install locations).
        var candidates = new (RegistryHive Hive, string Key, string Value)[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        };

        foreach (var (hive, key, value) in candidates)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var sub = baseKey.OpenSubKey(key);
                if (sub?.GetValue(value) is string path && !string.IsNullOrWhiteSpace(path))
                {
                    // Registry stores forward slashes in SteamPath.
                    var normalized = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
                    if (IsSteamDirectory(normalized)) return normalized;
                }
            }
            catch
            {
                // Ignore access/registry errors and try the next location.
            }
        }

        return null;
    }

    private static bool IsSteamDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "steamapps"));
}
