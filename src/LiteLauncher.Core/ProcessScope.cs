namespace LiteLauncher.Core;

/// <summary>Decides which folders and files belong to a game, so a force-close never touches anything else.</summary>
public static class ProcessScope
{
    /// <summary>
    /// True when <paramref name="installPath"/> is specific enough to mean "one game's folder":
    /// not empty, not a drive root, not a top-level folder such as C:\Games or C:\Program Files.
    /// </summary>
    public static bool IsSafeInstallRoot(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return false;

        string full;
        try { full = Path.GetFullPath(installPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return false;

        var rest = full[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var depth = rest.Length == 0 ? 0 : rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
        if (depth < 2) return false;

        // "C:\Windows\System32" and the like are deep but are never a game.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.IsNullOrEmpty(windows) || !IsInside(windows, full);
    }

    /// <summary>True when <paramref name="filePath"/> is the folder itself or lies below it.</summary>
    public static bool IsInside(string folder, string filePath)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(filePath)) return false;
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The folder to search: the game's install folder, else the folder of its executable if that is safe.</summary>
    public static string? Resolve(Game game)
    {
        if (IsSafeInstallRoot(game.InstallPath)) return game.InstallPath;
        var exeDir = string.IsNullOrEmpty(game.ExecutablePath) ? null : Path.GetDirectoryName(game.ExecutablePath);
        return IsSafeInstallRoot(exeDir) ? exeDir : null;
    }
}
