namespace LiteLauncher.Core.Steam;

/// <summary>
/// Reads installed games straight from Steam's on-disk metadata
/// (libraryfolders.vdf + appmanifest_*.acf). No Steam Web API, no login.
/// </summary>
public static class SteamLibraryReader
{
    private const int StateFullyInstalled = 4; // StateFlags bit

    // Steam entries that live in steamapps but aren't games.
    private static readonly HashSet<string> IgnoredAppIds = new()
    {
        "228980", // Steamworks Common Redistributables
    };

    private static readonly string[] IgnoredNamePrefixes =
    {
        "Proton", "Steam Linux Runtime", "Steamworks Common Redistributables",
    };

    public static IReadOnlyList<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var root = VdfParser.Parse(File.ReadAllText(vdf));
            var container = root["libraryfolders"] ?? root;

            foreach (var entry in container.Children.Values)
            {
                // New format: "0" { "path" "D:\\SteamLibrary" ... }
                // Old format: "1" "D:\\SteamLibrary"
                var path = entry.GetString("path") ?? entry.Value;
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!Directory.Exists(path)) continue;
                if (folders.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                folders.Add(path);
            }
        }

        return folders;
    }

    public static IReadOnlyList<Game> ReadInstalledGames(string steamPath)
    {
        var games = new Dictionary<string, Game>();

        foreach (var library in GetLibraryFolders(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var game = TryReadManifest(manifest, steamPath);
                if (game is not null) games[game.Id] = game;
            }
        }

        return games.Values
            .OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Steam keeps games in "&lt;library&gt;\steamapps\common\&lt;installdir&gt;", next to the manifest.</summary>
    private static string? InstallDir(string manifestPath, string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || installDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var steamapps = Path.GetDirectoryName(manifestPath);
        return steamapps is null ? null : Path.Combine(steamapps, "common", installDir);
    }

    private static Game? TryReadManifest(string manifestPath, string steamPath)
    {
        try
        {
            var state = VdfParser.Parse(File.ReadAllText(manifestPath))["AppState"];
            if (state is null) return null;

            var appId = state.GetString("appid");
            var name = state.GetString("name");
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) return null;

            if ((state.GetLong("StateFlags") & StateFullyInstalled) == 0) return null;
            if (IgnoredAppIds.Contains(appId)) return null;
            if (IgnoredNamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;

            var lastPlayedRaw = state.GetLong("LastPlayed");
            DateTimeOffset? lastPlayed = lastPlayedRaw > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lastPlayedRaw)
                : null;

            return new Game(SteamProvider.ProviderName, appId, name, lastPlayed, state.GetLong("SizeOnDisk"))
            {
                CoverPath = SteamArtwork.FindCover(steamPath, appId),
                BackdropPath = SteamArtwork.FindBackdrop(steamPath, appId),
                InstallPath = InstallDir(manifestPath, state.GetString("installdir")),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // manifest locked by Steam mid-write; it'll show up on next refresh
        }
    }
}
