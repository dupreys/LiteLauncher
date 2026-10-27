using System.Diagnostics;

namespace LiteLauncher.Core.Steam;

public sealed class SteamProvider : IGameProvider
{
    public const string ProviderName = "Steam";

    private readonly string? _steamPath;

    public SteamProvider() : this(SteamLocator.FindSteamPath()) { }

    public SteamProvider(string? steamPath) => _steamPath = steamPath;

    public string Name => ProviderName;

    public bool IsAvailable => _steamPath is not null;

    public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        if (_steamPath is null)
            return Task.FromResult<IReadOnlyList<Game>>(Array.Empty<Game>());

        return Task.Run(() => SteamLibraryReader.ReadInstalledGames(_steamPath), cancellationToken);
    }

    /// <summary>
    /// Launches through the Steam client (steam://rungameid/...) so overlay,
    /// cloud saves and DRM keep working exactly like a launch from Steam itself.
    /// If Steam is not running it is started minimized in the background ("-silent"), so its window does not
    /// cover LiteLauncher and the launcher is what you see again when the game closes.
    /// </summary>
    public void Launch(Game game)
    {
        if (game.Source != ProviderName || !game.Id.All(char.IsDigit))
            throw new ArgumentException("Not a Steam game.", nameof(game));

        var exe = _steamPath is null ? null : Path.Combine(_steamPath, "steam.exe");
        if (exe is not null && File.Exists(exe) && !IsRunning("steam"))
        {
            Process.Start(new ProcessStartInfo(exe, $"-silent -applaunch {game.Id}") { UseShellExecute = false })?.Dispose();
            return;
        }

        Process.Start(new ProcessStartInfo($"steam://rungameid/{game.Id}") { UseShellExecute = true })?.Dispose();
    }

    private static bool IsRunning(string processName)
    {
        var found = Process.GetProcessesByName(processName);
        var running = found.Length > 0;
        foreach (var p in found) p.Dispose();
        return running;
    }
}
