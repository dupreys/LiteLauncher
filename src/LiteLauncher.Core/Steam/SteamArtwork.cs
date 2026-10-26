namespace LiteLauncher.Core.Steam;

/// <summary>
/// Finds artwork that the Steam client has already cached locally, so the
/// launcher needs no network access and no API key.
/// </summary>
public static class SteamArtwork
{
    // Portrait capsule first (best fit for a console-style tile), then wider fallbacks.
    private static readonly string[] CoverNames =
    {
        "library_600x900", "library_capsule", "header", "library_header", "capsule_616x353",
    };

    // Wide hero art for the full-screen background.
    private static readonly string[] BackdropNames =
    {
        "library_hero", "page_bg_generated_v6b", "library_header", "header",
    };

    public static string? FindCover(string steamPath, string appId) => Find(steamPath, appId, CoverNames);

    public static string? FindBackdrop(string steamPath, string appId) => Find(steamPath, appId, BackdropNames);

    private static string? Find(string steamPath, string appId, string[] names)
    {
        var cache = Path.Combine(steamPath, "appcache", "librarycache");
        if (!Directory.Exists(cache)) return null;

        // Older layout: files directly in librarycache, e.g. 730_library_600x900.jpg
        foreach (var name in names)
        {
            var flat = Path.Combine(cache, $"{appId}_{name}.jpg");
            if (File.Exists(flat)) return flat;
        }

        // Newer layout: librarycache/<appid>/<hash>/library_600x900.jpg
        var folder = Path.Combine(cache, appId);
        if (!Directory.Exists(folder)) return null;

        foreach (var name in names)
        {
            var match = Search(folder, $"{name}.jpg") ?? Search(folder, $"{name}*.jpg");
            if (match is not null) return match;
        }

        return null;
    }

    private static string? Search(string folder, string pattern)
    {
        try
        {
            // Skip the pre-blurred variants (e.g. library_hero_blur.jpg).
            return Directory
                .EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .FirstOrDefault(f => !Path.GetFileName(f).Contains("blur", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
