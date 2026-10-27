using System.Reflection;

namespace LiteLauncher.App;

internal static class AppInfo
{
    /// <summary>The version set at build time (the release workflow passes the tag). 0.0.0 means a local development build.</summary>
    public static Version Current => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"owner/name" of the GitHub repository, stamped in by the release workflow; empty for local builds.</summary>
    public static string? Repository => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value;

    public static string Display
    {
        get
        {
            var v = Current;
            return v > new Version(0, 0, 0) ? $"LiteLauncher {v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "LiteLauncher (development build)";
        }
    }
}
