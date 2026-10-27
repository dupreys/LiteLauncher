using LiteLauncher.Core;

namespace LiteLauncher.Core.Settings;

/// <summary>User preferences, persisted as JSON in %APPDATA%\LiteLauncher\settings.json.</summary>
public sealed class AppSettings
{
    // Theme
    public string Accent { get; set; } = "#4C9AFF";
    public string Background { get; set; } = "#0F1115";
    public bool ShowBackdrop { get; set; } = true;

    /// <summary>Show the whole game art (letterboxed, fading into the background) instead of cropping it to fill the screen.</summary>
    public bool FitBackdrop { get; set; } = true;

    /// <summary>How dark the game-art background is, 0 (none) to 0.9.</summary>
    public double BackdropDim { get; set; } = 0.35;

    // Sound (system volume itself is not stored; Windows owns it)
    public bool UiSounds { get; set; } = true;
    public double UiVolume { get; set; } = 0.5;

    // General
    public bool StartFullscreen { get; set; }

    /// <summary>Look for a newer GitHub release at startup (at most once a day).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>How the library is ordered: "Recent", "Name" or "Size".</summary>
    public string LibrarySort { get; set; } = "Recent";

    // Steam account (public info only: never a password or token)
    public string? SteamId { get; set; }
    public string? PersonaName { get; set; }
    public string? AvatarUrl { get; set; }

    // Local profile overrides (only used inside the launcher; Steam itself is never changed)
    public const int MaxNameLength = 24;

    /// <summary>Name shown instead of the Steam name. Null or empty means "use the Steam name".</summary>
    public string? CustomName { get; set; }

    /// <summary>Path of the picture the user chose (a copy kept in the app data folder).</summary>
    public string? CustomAvatarPath { get; set; }

    // Optional keys the user creates on the services' own sites (stored only on this PC)
    /// <summary>Steam Web API key: needed for the online friends list.</summary>
    public string? SteamWebApiKey { get; set; }

    /// <summary>SteamGridDB API key: needed to download covers, backgrounds and logos.</summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>Last played and minutes LiteLauncher recorded itself (Epic has no local play history), keyed by "Source:Id".</summary>
    public Dictionary<string, PlayRecord> PlayRecords { get; set; } = new();

    /// <summary>Games the user starred, as "Source:Id".</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>Per-game name/cover/background chosen by the user, keyed by "Source:Id".</summary>
    public Dictionary<string, GameOverride> GameOverrides { get; set; } = new();

    /// <summary>Clamps values that may have been hand-edited into something unusable.</summary>
    public void Normalize()
    {
        BackdropDim = Math.Clamp(double.IsNaN(BackdropDim) ? 0.35 : BackdropDim, 0, 0.9);
        UiVolume = Math.Clamp(double.IsNaN(UiVolume) ? 0.5 : UiVolume, 0, 1);
        if (string.IsNullOrWhiteSpace(Accent)) Accent = "#4C9AFF";
        if (string.IsNullOrWhiteSpace(Background)) Background = "#0F1115";

        CustomName = string.IsNullOrWhiteSpace(CustomName) ? null : CustomName.Trim();
        if (CustomName is { Length: > MaxNameLength }) CustomName = CustomName[..MaxNameLength];
        if (string.IsNullOrWhiteSpace(CustomAvatarPath)) CustomAvatarPath = null;

        SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        if (LibrarySort is not ("Recent" or "Name" or "Size")) LibrarySort = "Recent";
        GameOverrides ??= new();
        PlayRecords ??= new();
        Favorites = (Favorites ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in GameOverrides.Where(p => p.Value is null || p.Value.IsEmpty).Select(p => p.Key).ToList())
            GameOverrides.Remove(key);
    }
}
