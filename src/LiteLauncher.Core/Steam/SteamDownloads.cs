namespace LiteLauncher.Core.Steam;

public enum DownloadStatus { Waiting, Downloading, Paused }

/// <param name="BytesDone">Downloaded so far.</param>
/// <param name="BytesTotal">To download in total (0 while Steam is still preparing).</param>
public sealed record DownloadInfo(string AppId, string Name, long BytesDone, long BytesTotal, DownloadStatus Status, bool IsUpdate)
{
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;
}

/// <summary>
/// Steam itself downloads and installs games; LiteLauncher asks it to (steam://install/ID) and then follows the
/// progress Steam writes into each game's appmanifest_*.acf file.
/// </summary>
public static class SteamDownloads
{
    // StateFlags bits used by Steam
    private const int FullyInstalled = 4;
    private const int UpdateRequired = 2;
    private const int UpdateRunning = 256;
    private const int UpdatePaused = 512;
    private const int UpdateStarted = 1024;

    public static string InstallUri(string appId)
    {
        if (string.IsNullOrEmpty(appId) || !appId.All(char.IsAsciiDigit))
            throw new ArgumentException("Not a Steam app id.", nameof(appId));
        return $"steam://install/{appId}";
    }

    /// <returns>The download in progress described by a manifest, or null when the game is simply installed (or not downloading).</returns>
    public static DownloadInfo? ParseManifest(string acfText)
    {
        var state = VdfParser.Parse(acfText)["AppState"];
        if (state is null) return null;

        var appId = state.GetString("appid");
        if (string.IsNullOrWhiteSpace(appId) || !appId.All(char.IsAsciiDigit)) return null;

        var flags = state.GetLong("StateFlags");
        var installed = (flags & FullyInstalled) != 0;
        var running = (flags & (UpdateRunning | UpdateStarted)) != 0;
        var paused = (flags & UpdatePaused) != 0;

        // An installed game only counts while it is really updating; a pending update nobody started does not.
        var active = installed ? running || (paused && state.GetLong("BytesToDownload") > 0)
                               : (flags & (UpdateRequired | UpdateRunning | UpdatePaused | UpdateStarted)) != 0;
        if (!active) return null;

        var name = state.GetString("name");
        if (string.IsNullOrWhiteSpace(name)) name = "App " + appId;
        return new DownloadInfo(appId, name, Math.Max(state.GetLong("BytesDownloaded"), state.GetLong("BytesStaged")), state.GetLong("BytesToDownload"),
                                paused ? DownloadStatus.Paused : DownloadStatus.Downloading, installed);
    }

    /// <summary>Every download in progress, across all Steam library folders.</summary>
    public static IReadOnlyList<DownloadInfo> Read(string steamPath)
    {
        var list = new Dictionary<string, DownloadInfo>();
        try
        {
            foreach (var library in SteamLibraryReader.GetLibraryFolders(steamPath))
            {
                var steamapps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        if (ParseManifest(File.ReadAllText(manifest)) is { } info)
                        {
                            list[info.AppId] = info;
                        }
                    }
                    catch (IOException) { } // Steam is writing it right now: next poll
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} B" : $"{value:0.#} {units[unit]}";
    }
}
