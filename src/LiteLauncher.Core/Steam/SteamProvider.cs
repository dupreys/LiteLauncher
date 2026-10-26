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
    /// If Steam isn't running, Windows starts it first.
    /// </summary>
    public void Launch(Game game)
    {
        if (game.Source != ProviderName || !game.Id.All(char.IsDigit))
            throw new ArgumentException("Not a Steam game.", nameof(game));

        Process.Start(new ProcessStartInfo($"steam://rungameid/{game.Id}") { UseShellExecute = true });
    }
}
