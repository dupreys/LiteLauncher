namespace LiteLauncher.Core;

/// <summary>
/// A source of games (Steam today; Epic, Xbox/Game Pass, GOG... later).
/// Adding a store means implementing this interface and registering it in the app.
/// </summary>
public interface IGameProvider
{
    string Name { get; }

    /// <summary>True when the store client/library is present on this machine.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default);

    void Launch(Game game);
}
