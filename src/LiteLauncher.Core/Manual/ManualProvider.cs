using System.Diagnostics;

namespace LiteLauncher.Core.Manual;

/// <summary>Games added by hand: the launcher just starts the .exe the user picked.</summary>
public sealed class ManualProvider : IGameProvider
{
    public const string ProviderName = "Manual";
    public const int MaxNameLength = 80;

    private readonly Func<IReadOnlyList<ManualGame>> _games;

    public ManualProvider(Func<IReadOnlyList<ManualGame>> games) => _games = games;

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Game>();
        foreach (var g in _games())
        {
            if (string.IsNullOrWhiteSpace(g.ExePath) || !File.Exists(g.ExePath)) continue; // removed/uninstalled: hide it
            list.Add(new Game(ProviderName, g.Id, g.Name)
            {
                ExecutablePath = g.ExePath,
                InstallPath = Path.GetDirectoryName(g.ExePath),
                OriginalName = g.Name,
            });
        }
        return Task.FromResult<IReadOnlyList<Game>>(list);
    }

    public void Launch(Game game)
    {
        if (game.Source != ProviderName)
            throw new ArgumentException("Not a manually added game.", nameof(game));

        var exe = game.ExecutablePath;
        if (!IsValidExecutable(exe))
            throw new FileNotFoundException("The game's .exe was not found.", exe);

        Process.Start(new ProcessStartInfo(exe!)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        })?.Dispose();
    }

    /// <summary>An existing .exe file (nothing else is ever started from a manual entry).</summary>
    public static bool IsValidExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    /// <summary>A readable default name: the product name inside the .exe, else the file name.</summary>
    public static string SuggestName(string exePath)
    {
        try
        {
            var product = FileVersionInfo.GetVersionInfo(exePath).ProductName?.Trim();
            if (!string.IsNullOrWhiteSpace(product) && product.Length <= MaxNameLength) return product;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        var name = Path.GetFileNameWithoutExtension(exePath).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }

    /// <summary>Adds a game to the list. Returns null when the path is invalid or the same .exe is already there.</summary>
    public static ManualGame? Add(IList<ManualGame> games, string exePath, string name)
    {
        if (!IsValidExecutable(exePath)) return null;
        var full = Path.GetFullPath(exePath);
        if (games.Any(g => string.Equals(g.ExePath, full, StringComparison.OrdinalIgnoreCase))) return null;

        name = (name ?? "").Trim();
        if (name.Length == 0) name = SuggestName(full);
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        var game = new ManualGame { Id = Guid.NewGuid().ToString("N"), Name = name, ExePath = full };
        games.Add(game);
        return game;
    }
}
