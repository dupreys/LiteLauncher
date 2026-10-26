namespace LiteLauncher.Core.Steam;

/// <summary>
/// Raises <see cref="Changed"/> whenever Steam adds, removes or updates a game manifest in any library
/// folder (install finished, game launched or closed, uninstall), or when the list of library folders changes.
/// Events arrive on a thread-pool thread and are not debounced: the caller should coalesce them.
/// </summary>
public sealed class SteamLibraryWatcher : IDisposable
{
    private readonly string _steamPath;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _gate = new();
    private bool _disposed;

    public SteamLibraryWatcher(string steamPath)
    {
        _steamPath = steamPath;
        Rebuild();
    }

    public event Action? Changed;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            DisposeWatchers();
        }
    }

    private void Rebuild()
    {
        lock (_gate)
        {
            if (_disposed) return;
            DisposeWatchers();

            foreach (var folder in SteamLibraryReader.GetLibraryFolders(_steamPath))
            {
                var steamapps = Path.Combine(folder, "steamapps");
                if (!Directory.Exists(steamapps)) continue;

                try
                {
                    var watcher = new FileSystemWatcher(steamapps)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false,
                    };
                    watcher.Changed += (_, e) => Handle(e.Name);
                    watcher.Created += (_, e) => Handle(e.Name);
                    watcher.Deleted += (_, e) => Handle(e.Name);
                    watcher.Renamed += (_, e) => { Handle(e.OldName); Handle(e.Name); };
                    watcher.Error += (_, _) => { }; // buffer overflow: the next event or the poll will catch up
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    // Folder vanished (unplugged drive) or isn't watchable: skip it.
                }
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    private void Handle(string? name)
    {
        if (name is null) return;

        var isLibraryList = name.Equals("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase);
        var isManifest = name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith(".acf", StringComparison.OrdinalIgnoreCase);
        if (!isLibraryList && !isManifest) return;

        // A new library folder means a new place to watch. Done off this callback so a watcher
        // is never disposed from inside its own event.
        if (isLibraryList) _ = Task.Run(Rebuild);

        Changed?.Invoke();
    }
}
