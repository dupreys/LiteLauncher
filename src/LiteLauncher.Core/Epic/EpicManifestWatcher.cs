namespace LiteLauncher.Core.Epic;

/// <summary>
/// Raises <see cref="Changed"/> when the Epic launcher adds, rewrites or removes an install manifest
/// (install finished, update, uninstall). Events come from a pool thread and are not debounced.
/// </summary>
public sealed class EpicManifestWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public EpicManifestWatcher(string? manifestDir)
    {
        if (manifestDir is null || !Directory.Exists(manifestDir)) return;
        _watcher = new FileSystemWatcher(manifestDir, "*.item")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, _) => Changed?.Invoke();
        _watcher.Changed += (_, _) => Changed?.Invoke();
        _watcher.Deleted += (_, _) => Changed?.Invoke();
        _watcher.Renamed += (_, _) => Changed?.Invoke();
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? Changed;

    public void Dispose() => _watcher?.Dispose();
}
