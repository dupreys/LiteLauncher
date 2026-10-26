namespace LiteLauncher.Core;

/// <summary>What the user changed about a game inside LiteLauncher. The store's own data is never modified.</summary>
public sealed class GameOverride
{
    public string? Name { get; set; }
    public string? CoverPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? LogoPath { get; set; }

    public bool IsEmpty => Name is null && CoverPath is null && BackdropPath is null && LogoPath is null;
}

public static class GameOverrides
{
    public const int MaxNameLength = 80;

    /// <summary>Returns the game with the user's name, cover and background applied (when set).</summary>
    public static Game Apply(Game game, IReadOnlyDictionary<string, GameOverride>? overrides)
    {
        if (overrides is null || !overrides.TryGetValue(GameSorter.Key(game), out var o) || o.IsEmpty) return game;

        return game with
        {
            OriginalName = game.OriginalName ?? game.Name,
            Name = string.IsNullOrWhiteSpace(o.Name) ? game.Name : o.Name.Trim(),
            CoverPath = o.CoverPath is not null && File.Exists(o.CoverPath) ? o.CoverPath : game.CoverPath,
            BackdropPath = o.BackdropPath is not null && File.Exists(o.BackdropPath) ? o.BackdropPath : game.BackdropPath,
            LogoPath = o.LogoPath is not null && File.Exists(o.LogoPath) ? o.LogoPath : game.LogoPath,
        };
    }

    /// <summary>Cleans a name typed by the user. Returns null for "use the store's name".</summary>
    public static string? NormalizeName(string? name, string storeName)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == storeName) return null;
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }
}
