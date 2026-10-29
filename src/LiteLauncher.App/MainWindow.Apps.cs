using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Apps;

namespace LiteLauncher.App;

// Apps tab: the programs that appear in the Windows Start menu, so any of them can be opened from the launcher.
// They are not games: no play history, no overlay, no game menu. A just opens the app.
public partial class MainWindow
{
    private readonly ObservableCollection<GameItem> _apps = new();
    private ICollectionView _appsView = null!;
    private bool _appsLoading;
    private DateTime _appsLoadedAt = DateTime.MinValue;
    private readonly Dictionary<string, GameItem> _appsById = new();
    private CancellationTokenSource? _appIconsCancel;

    private static bool IsApp(GameItem item) => item.Game.Source == StartApps.SourceName;

    private void InitApps()
    {
        _appsView = CollectionViewSource.GetDefaultView(_apps);
        _appsView.Filter = o => o is GameItem g && MatchesSearch(g);
        _appsView.SortDescriptions.Add(new SortDescription(nameof(GameItem.Name), ListSortDirection.Ascending));
    }

    /// <summary>Called when the user opens the Apps tab: reads the list (at most every few minutes) and then the icons.</summary>
    private void OnAppsTabOpened()
    {
        if (_appsLoading || DateTime.UtcNow - _appsLoadedAt < TimeSpan.FromMinutes(5)) return;
        _ = LoadAppsAsync();
    }

    private List<StartApp> _startApps = new();
    private readonly Dictionary<string, System.Windows.Media.ImageSource> _appIcons = new();

    private async Task LoadAppsAsync()
    {
        _appsLoading = true;
        try
        {
            var list = await StartApps.ReadAsync();
            if (list.Count == 0 && _startApps.Count > 0) return; // keep what is shown if Windows gave nothing this time
            _appsLoadedAt = DateTime.UtcNow;

            _startApps = StartApps.Filter(list, App.Settings.AppsShown)
                .Where(a => !string.Equals(a.Name, "LiteLauncher", StringComparison.OrdinalIgnoreCase)).ToList();
            RebuildApps();

            if (_filter == LibraryFilter.Apps && Keyboard.FocusedElement is null or Window) FocusFirstResult();
            var missing = _apps.Where(i => i.Cover is null).Select(i => i.Game.Id).ToList();
            if (missing.Count > 0) _ = LoadAppIconsAsync(missing);
        }
        finally
        {
            _appsLoading = false;
            UpdateCount();
        }
    }

    private Game BuildAppGame(StartApp app) =>
        WithCachedArt(GameOverrides.Apply(new Game(StartApps.SourceName, app.AppId, app.Name), App.Settings.GameOverrides));

    /// <summary>
    /// Brings the Apps list in line with the Start menu apps and the user's names and pictures (Manage), reusing
    /// the tiles that did not change. A picture the user chose (Manage) wins over Windows' icon.
    /// </summary>
    private void RebuildApps()
    {
        var games = _startApps.Select(BuildAppGame).ToList();
        var existing = _apps.ToDictionary(i => i.Game.Id);
        var target = new List<GameItem>(games.Count);
        foreach (var game in games)
        {
            var reusable = existing.TryGetValue(game.Id, out var item)
                           && item.Game.Name == game.Name && item.Game.CoverPath == game.CoverPath;
            target.Add(reusable ? item! : new GameItem(game));
        }

        Reconcile(_apps, target);
        _appsById.Clear();
        foreach (var item in _apps)
        {
            _appsById[item.Game.Id] = item;
            if (item.Game.CoverPath is not null) item.EnsureCoverLoaded();
            else if (_appIcons.TryGetValue(item.Game.Id, out var icon)) item.SetCover(icon);
        }

        UpdateCount();
    }

    private async Task LoadAppIconsAsync(List<string> ids)
    {
        _appIconsCancel?.Cancel();
        var cancel = _appIconsCancel = new CancellationTokenSource();

        try
        {
            await AppIcons.LoadAsync(ids, (id, icon) =>
            {
                if (icon is null) return;
                // Composing the tile uses WPF drawing, so it happens on the UI thread, between other work.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var tile = AppIcons.ComposeTile(icon);
                    _appIcons[id] = tile;
                    if (_appsById.TryGetValue(id, out var item) && item.Game.CoverPath is null) item.SetCover(tile);
                }));
            }, cancel.Token);
        }
        catch (Exception ex)
        {
            CrashLog.Write("App icons", ex);
        }
    }

    private void OpenApp(GameItem item)
    {
        try
        {
            StartApps.Open(item.Game.Id);
            App.Sounds.Play(SoundKind.Launch);
            ShowToast(Loc.F("Opening {0}…", item.Name));
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException)
        {
            ShowToast(Loc.F("Could not open {0}", item.Name));
        }
    }
}
