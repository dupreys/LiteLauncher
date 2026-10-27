using System.Diagnostics;
using System.Text.Json;

namespace LiteLauncher.Core.Epic;

/// <summary>
/// Installed Epic Games Store games, read from the launcher's own install manifests
/// (%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item). No Epic account or API is needed.
/// </summary>
public sealed class EpicProvider : IGameProvider
{
    public const string ProviderName = "Epic";

    private readonly string? _manifestDir;

    public EpicProvider() : this(DefaultManifestDirectory()) { }

    public EpicProvider(string? manifestDir) =>
        _manifestDir = manifestDir is not null && Directory.Exists(manifestDir) ? manifestDir : null;

    public string Name => ProviderName;

    public bool IsAvailable => _manifestDir is not null;

    public static string? DefaultManifestDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrEmpty(programData)
            ? null
            : Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        if (_manifestDir is null) return Task.FromResult<IReadOnlyList<Game>>(Array.Empty<Game>());
        return Task.Run(() => ReadInstalledGames(_manifestDir), cancellationToken);
    }

    public static IReadOnlyList<Game> ReadInstalledGames(string manifestDir)
    {
        var games = new List<Game>();
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            try
            {
                if (ParseManifest(File.ReadAllText(file)) is { } game) games.Add(game);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // being rewritten by the Epic launcher; the next refresh picks it up
            }
        }
        return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Turns one manifest into a game, or null for DLC, half-finished installs and broken files.</summary>
    public static Game? ParseManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool Bool(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

            var appName = Str("AppName");
            var name = Str("DisplayName");
            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(name)) return null;
            if (Bool("bIsIncompleteInstall")) return null;

            var main = Str("MainGameAppName");
            if (!string.IsNullOrEmpty(main) && !string.Equals(main, appName, StringComparison.OrdinalIgnoreCase))
                return null; // DLC

            var size = root.TryGetProperty("InstallSize", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            var location = Str("InstallLocation");
            var exe = Str("LaunchExecutable");

            return new Game(ProviderName, appName, name, null, size)
            {
                CatalogNamespace = Str("CatalogNamespace"),
                CatalogItemId = Str("CatalogItemId"),
                InstallPath = location,
                ExecutablePath = location is not null && exe is not null ? Path.Combine(location, exe) : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Launches through the Epic launcher so ownership checks, cloud saves and overlays still work.</summary>
    public void Launch(Game game)
    {
        if (game.Source != ProviderName) throw new ArgumentException("Not an Epic game.", nameof(game));

        var uri = LaunchUri(game);
        var exe = FindLauncherExe();
        if (exe is not null && !IsRunning("EpicGamesLauncher"))
        {
            // Start the launcher minimized first so its window does not cover LiteLauncher, then give it the game
            // once it has had a moment to start (a launch request sent too early is dropped).
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* the overlay times out and says so */ }
            });
            return;
        }

        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>The Epic Games Launcher program, in its usual install folders; null when not found.</summary>
    public static string? FindLauncherExe()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var bits in new[] { "Win64", "Win32" })
            {
                var path = Path.Combine(root, "Epic Games", "Launcher", "Portal", "Binaries", bits, "EpicGamesLauncher.exe");
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    private static bool IsRunning(string processName)
    {
        var found = Process.GetProcessesByName(processName);
        var running = found.Length > 0;
        foreach (var p in found) p.Dispose();
        return running;
    }

    public static string LaunchUri(Game game)
    {
        var id = game.CatalogNamespace is not null && game.CatalogItemId is not null
            ? $"{Uri.EscapeDataString(game.CatalogNamespace)}%3A{Uri.EscapeDataString(game.CatalogItemId)}%3A{Uri.EscapeDataString(game.Id)}"
            : Uri.EscapeDataString(game.Id);
        return $"com.epicgames.launcher://apps/{id}?action=launch&silent=true";
    }
}
