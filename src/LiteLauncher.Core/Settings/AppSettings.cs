using LiteLauncher.Core.Manual;
using LiteLauncher.Core;

namespace LiteLauncher.Core.Settings;

/// <summary>User preferences, persisted as JSON in %APPDATA%\LiteLauncher\settings.json.</summary>
public sealed class AppSettings
{
    // Theme
    public string Accent { get; set; } = "#4C9AFF";
    public string Background { get; set; } = "#0F1115";
    public bool ShowBackdrop { get; set; } = true;

    /// <summary>No longer used (the banner style always shows the whole image); kept so old settings files still load.</summary>
    public bool FitBackdrop { get; set; } = true;

    /// <summary>When off, the background colour is ignored and the launcher is pure OLED black.</summary>
    public bool UseBackgroundColor { get; set; } = true;

    /// <summary>Full-screen style: the whole picture centred over a blurred copy that fills the screen. When off, the banner style (picture along the top, fading down) is used.</summary>
    public bool FullBackdrop { get; set; } = true;

    /// <summary>Fixed wallpaper instead of the game art: "" (none), "preset:id" (ready-made) or "file:path" (the user's own picture, copied into the app data folder).</summary>
    public string Wallpaper { get; set; } = "";

    /// <summary>How dark the game-art background is, 0 (none) to 0.9.</summary>
    public double BackdropDim { get; set; } = 0.35;

    /// <summary>Layout of the Home screen: "Xbox" (tiles), "Simple" (one row of games, like the Steam home) or "Cinema".</summary>
    public string HomeStyle { get; set; } = "Xbox";

    // Sound (system volume itself is not stored; Windows owns it)
    public bool UiSounds { get; set; } = true;
    public double UiVolume { get; set; } = 0.5;

    // General
    public bool StartFullscreen { get; set; }

    /// <summary>Interface language code ("en", "pt-BR", "es"); null means follow the Windows language.</summary>
    public string? Language { get; set; }

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

    /// <summary>Finished play sessions (newest last), for the play time screen. Capped, see <see cref="PlayHistory.MaxSessions"/>.</summary>
    public List<PlaySession> PlaySessions { get; set; } = new();

    /// <summary>The version whose release notes were last shown ("1.2.3"), so they appear once after an update.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Games the user added by hand (an .exe outside Steam and Epic).</summary>
    public List<ManualGame> ManualGames { get; set; } = new();

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

        PlaySessions = PlayHistory.Clean(PlaySessions);
        Wallpaper = (Wallpaper ?? "").Trim();
        if (Wallpaper.Length > 0 && !Wallpaper.StartsWith("preset:", StringComparison.Ordinal) && !Wallpaper.StartsWith("file:", StringComparison.Ordinal)) Wallpaper = "";
        if (HomeStyle is not ("Xbox" or "Simple" or "Cinema")) HomeStyle = "Xbox";
        SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        if (LibrarySort is not ("Recent" or "Name" or "Size")) LibrarySort = "Recent";
        GameOverrides ??= new();
        PlayRecords ??= new();
        ManualGames = (ManualGames ?? new())
            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Id) && !string.IsNullOrWhiteSpace(g.ExePath))
            .GroupBy(g => g.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        Favorites = (Favorites ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in GameOverrides.Where(p => p.Value is null || p.Value.IsEmpty).Select(p => p.Key).ToList())
            GameOverrides.Remove(key);
    }
}
