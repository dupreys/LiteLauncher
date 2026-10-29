using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Steam games the account owns but has not installed: listed in the library's Steam tab, after the installed ones, dimmed.
// A (or Start) opens the game menu, where "Download" asks Steam to install it (see MainWindow.Downloads.cs).
public partial class MainWindow
{
    private readonly ObservableCollection<GameItem> _owned = new();
    private ICollectionView _ownedView = null!;

    // What the grid really holds of _owned: the Steam tab opens with the first batch and the rest is added a bit at a
    // time, so switching to it does not freeze the window while hundreds of cards are built.
    private readonly ObservableCollection<GameItem> _ownedShown = new();
    private int _ownedLimit;
    private bool _revealing;
    private const int RevealBatch = 24;
    private IReadOnlyList<OwnedGame> _ownedList = Array.Empty<OwnedGame>();
    private string? _ownedSteamId;
    private string? _ownedProblem;
    private DateTime _ownedTriedAt = DateTime.MinValue;
    private bool _ownedFetching;
    private bool _ownedArtRunning;

    private sealed record OwnedCache(string SteamId, List<OwnedGame> Games);

    private static string OwnedCachePath => Path.Combine(SettingsStore.DefaultDirectory, "owned_games.json");

    /// <summary>What the Steam tab shows: the installed games first, then the ones that are not installed (dimmed).</summary>
    private CompositeCollection _steamTab = null!;

    private void InitNotInstalled()
    {
        _ownedView = CollectionViewSource.GetDefaultView(_ownedShown);
        _ownedView.Filter = o => o is GameItem g && MatchesSearch(g);
        _steamTab = new CompositeCollection
        {
            new CollectionContainer { Collection = _view },
            new CollectionContainer { Collection = _ownedView },
        };

        // What was read last time shows up at once, even offline.
        try
        {
            if (File.Exists(OwnedCachePath) &&
                JsonSerializer.Deserialize<OwnedCache>(File.ReadAllText(OwnedCachePath)) is { } cache &&
                cache.SteamId == App.Settings.SteamId)
            {
                _ownedList = cache.Games;
                _ownedSteamId = cache.SteamId;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // a damaged cache is simply ignored
        }
    }

    /// <summary>Makes <paramref name="col"/> hold exactly <paramref name="target"/>, in order, touching as little as possible.</summary>
    private static void Reconcile(ObservableCollection<GameItem> col, IReadOnlyList<GameItem> target)
    {
        var keep = new HashSet<GameItem>(target);
        for (var i = col.Count - 1; i >= 0; i--)
            if (!keep.Contains(col[i])) col.RemoveAt(i);
        for (var i = 0; i < target.Count; i++)
        {
            if (i < col.Count && ReferenceEquals(col[i], target[i])) continue;
            var from = col.IndexOf(target[i]);
            if (from >= 0) col.Move(from, i);
            else col.Insert(i, target[i]);
        }
    }

    private void SyncShown()
    {
        var limit = _filter == LibraryFilter.Steam ? Math.Min(_ownedLimit, _owned.Count) : 0;
        Reconcile(_ownedShown, _owned.Take(limit).ToList());
    }

    /// <summary>Adds the next batch of not-installed cards while the Steam tab is open, between user input.</summary>
    private void RevealMoreOwned()
    {
        if (_revealing) return;
        _revealing = true;
        void Step()
        {
            if (_filter != LibraryFilter.Steam || _ownedLimit >= _owned.Count) { _revealing = false; return; }
            _ownedLimit += RevealBatch;
            SyncShown();
            foreach (var item in _ownedShown) item.EnsureCoverLoaded();
            UpdateCount();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(Step));
        }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Step));
    }

    /// <summary>Called when the user opens the Steam tab.</summary>
    private void OnSteamTabOpened()
    {
        _ownedLimit = 0;
        SyncShown();
        if (_ownedProblem is not null && SteamLoginUsers.IsSteamId64(App.Settings.SteamId))
            ShowToast("Could not list the Steam games you have not installed.");
        _ = FetchOwnedIfNeededAsync(force: _ownedProblem is not null);
        RebuildOwned();
        RevealMoreOwned();
        _ = DownloadOwnedArtAsync();
    }

    private async Task FetchOwnedIfNeededAsync(bool force = false)
    {
        if (_ownedFetching) return;
        var steamId = App.Settings.SteamId;
        if (!SteamLoginUsers.IsSteamId64(steamId))
        {
            RebuildOwned();
            return;
        }

        if (!force && _ownedSteamId == steamId && DateTime.UtcNow - _ownedTriedAt < TimeSpan.FromMinutes(20)) return;
        if (!force && _ownedSteamId != steamId && DateTime.UtcNow - _ownedTriedAt < TimeSpan.FromMinutes(2)) return;

        _ownedFetching = true;
        _ownedTriedAt = DateTime.UtcNow;
        try
        {
            var list = await SteamOwnedGames.FetchAsync(Http, steamId!, App.Settings.SteamWebApiKey);
            if (list is not null)
            {
                _ownedList = list;
                _ownedSteamId = steamId;
                _ownedProblem = null;
                try
                {
                    Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                    await File.WriteAllTextAsync(OwnedCachePath, JsonSerializer.Serialize(new OwnedCache(steamId!, list.ToList())));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // the cache is only a convenience
                }
            }
            else if (_ownedSteamId != steamId || _ownedList.Count == 0)
            {
                _ownedProblem = Loc.T("Could not read your Steam games. Make \"Game details\" public in your Steam profile privacy settings, or add a Steam Web API key in Settings → Integrations.");
            }
        }
        finally
        {
            _ownedFetching = false;
        }

        RebuildOwned();
        if (_filter == LibraryFilter.Steam)
        {
            RevealMoreOwned();
            _ = DownloadOwnedArtAsync();
        }
    }

    /// <summary>Owned games minus the installed ones, kept in the same collection objects where nothing changed.</summary>
    private void RebuildOwned()
    {
        var steamId = App.Settings.SteamId;
        var list = SteamLoginUsers.IsSteamId64(steamId) && _ownedSteamId == steamId ? _ownedList : Array.Empty<OwnedGame>();
        var installed = _items.Where(i => i.Game.Source == SteamProvider.ProviderName).Select(i => i.Game.Id).ToHashSet();

        var games = SteamOwnedGames.NotInstalled(list, installed, _steamPath)
            .Select(g => WithCachedArt(GameOverrides.Apply(g, App.Settings.GameOverrides)))
            .ToList();

        var existing = _owned.ToDictionary(i => GameSorter.Key(i.Game));
        var target = new List<GameItem>(games.Count);
        foreach (var game in games)
        {
            var reusable = existing.TryGetValue(GameSorter.Key(game), out var item)
                           && item.Game.Name == game.Name && item.Game.CoverPath == game.CoverPath;
            target.Add(reusable ? item! : new GameItem(game) { CoverDecodeWidth = 220 });
        }

        Reconcile(_owned, target);
        SyncShown();

        if (_filter == LibraryFilter.Steam)
        {
            foreach (var item in _ownedShown) item.EnsureCoverLoaded();
            RevealMoreOwned();
            UpdateCount();
        }
    }

    /// <summary>Covers for the not-installed games come from Steam's store CDN, a few at a time while the tab is in use.</summary>
    private async Task DownloadOwnedArtAsync()
    {
        if (_ownedArtRunning) return;
        _ownedArtRunning = true;
        try
        {
            var done = 0;
            foreach (var game in _owned.Select(i => i.Game).Where(g => g.CoverPath is null).ToList())
            {
                if (_filter != LibraryFilter.Steam) break; // the user left the Steam tab
                if (!_artTried.Add(GameSorter.Key(game))) continue;
                if (await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover) is not null && ++done % 8 == 0)
                    RebuildOwned();
            }
            if (done > 0) RebuildOwned();
        }
        finally
        {
            _ownedArtRunning = false;
        }
    }
}
