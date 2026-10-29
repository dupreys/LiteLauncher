namespace LiteLauncher.Core;

/// <summary>
/// A game from any provider. <see cref="Source"/> + <see cref="Id"/> is the unique key.
/// </summary>
public sealed record Game(
    string Source,
    string Id,
    string Name,
    DateTimeOffset? LastPlayed = null,
    long SizeOnDisk = 0)
{
    /// <summary>Absolute path to a cover image on disk, if one was found.</summary>
    public string? CoverPath { get; init; }

    /// <summary>Absolute path to a wide background image (hero art), if one was found.</summary>
    public string? BackdropPath { get; init; }

    /// <summary>Transparent game logo shown instead of the title, if one was chosen.</summary>
    public string? LogoPath { get; init; }

    /// <summary>The name the store reports, before any rename by the user.</summary>
    public string? OriginalName { get; init; }

    /// <summary>The game's main executable, when the store tells us (used to know when it is running).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Folder the game is installed in (used to find its processes when it must be force-closed).</summary>
    public string? InstallPath { get; init; }

    /// <summary>True for a game the user owns but has not installed (listed under "Not installed"; it can be downloaded).</summary>
    public bool NotInstalled { get; init; }

    // Epic catalog ids (needed for launch and store links)
    public string? CatalogNamespace { get; init; }
    public string? CatalogItemId { get; init; }
}
