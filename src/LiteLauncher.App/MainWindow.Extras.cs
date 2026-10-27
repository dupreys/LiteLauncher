using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;

namespace LiteLauncher.App;

// Settings > General extras (start with Windows, update check) and downloading missing Steam artwork.
public partial class MainWindow
{
    private UpdateInfo? _update;
    private bool _checkingUpdate;
    private readonly HashSet<string> _artTried = new();

    private static string ArtCacheDir => Path.Combine(SettingsStore.DefaultDirectory, "covers");

    /// <summary>Called from InitSettingsUi while the controls are being filled in.</summary>
    private void LoadExtrasUi()
    {
        StartWithWindowsToggle.IsEnabled = StartupRegistration.Supported;
        StartWithWindowsToggle.IsChecked = StartupRegistration.Supported && StartupRegistration.IsEnabled();
        UpdateCheckToggle.IsChecked = App.Settings.CheckForUpdates;
        VersionText.Text = AppInfo.Display;
        ShowUpdateState(toast: false, manual: false);
    }

    /// <summary>Returns true when the toggle was one of ours.</summary>
    private bool HandleExtrasToggle(System.Windows.Controls.Primitives.ToggleButton toggle, bool on)
    {
        if (ReferenceEquals(toggle, StartWithWindowsToggle))
        {
            if (!StartupRegistration.Set(on))
            {
                ShowToast("Windows did not allow changing startup.");
                _loadingUi = true;
                toggle.IsChecked = StartupRegistration.IsEnabled();
                _loadingUi = false;
            }
            return true;
        }

        if (ReferenceEquals(toggle, UpdateCheckToggle))
        {
            App.Settings.CheckForUpdates = on;
            QueueSave();
            return true;
        }
        return false;
    }

    // ---- Updates -------------------------------------------------------------------------

    /// <summary>At most once a day, a few seconds after start so it never competes with loading the library.</summary>
    private async Task AutoCheckForUpdateAsync()
    {
        var last = App.Settings.LastUpdateCheck;
        if (!App.Settings.CheckForUpdates || (last is not null && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(20))) return;

        await Task.Delay(TimeSpan.FromSeconds(8));
        await CheckForUpdateAsync(manual: false);
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_checkingUpdate) return;

        if (string.IsNullOrEmpty(AppInfo.Repository) || AppInfo.Current <= new Version(0, 0, 0))
        {
            if (manual) ShowToast("Update checks only work in installed releases, not in development builds.");
            return;
        }

        _checkingUpdate = true;
        UpdateLabel.Text = "Checking for updates…";
        try
        {
            _update = await UpdateChecker.CheckAsync(Http, AppInfo.Repository, AppInfo.Current);
            App.Settings.LastUpdateCheck = DateTimeOffset.UtcNow;
            QueueSave();
        }
        finally
        {
            _checkingUpdate = false;
        }
        ShowUpdateState(toast: true, manual);
    }

    private void ShowUpdateState(bool toast, bool manual)
    {
        if (_update is { } info)
        {
            UpdateLabel.Text = $"Version {info.Version.Major}.{info.Version.Minor}.{Math.Max(info.Version.Build, 0)} is available: open the download page";
            if (toast) ShowToast($"LiteLauncher {info.Tag} is available. See Settings > General.");
        }
        else
        {
            UpdateLabel.Text = "Check for updates now";
            if (toast && manual) ShowToast("You have the latest version (or GitHub could not be reached).");
        }
    }

    private async void OnUpdateButtonClick(object sender, RoutedEventArgs e)
    {
        if (_update is { } info)
        {
            try
            {
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception)
            {
                ShowToast("Could not open the browser.");
            }
            return;
        }
        await CheckForUpdateAsync(manual: true);
    }

    // ---- Missing artwork ---------------------------------------------------------------------

    /// <summary>Uses artwork downloaded earlier when Steam's own cache has none.</summary>
    private static Game WithCachedArt(Game game)
    {
        var cover = game.CoverPath ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover);
        var hero = game.BackdropPath ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Backdrop);
        return cover == game.CoverPath && hero == game.BackdropPath ? game : game with { CoverPath = cover, BackdropPath = hero };
    }

    /// <summary>Fetches covers/backgrounds that are still missing, one game at a time, then asks for a refresh to show them.</summary>
    private async Task DownloadMissingArtAsync(IReadOnlyList<Game> games)
    {
        var fetched = false;
        foreach (var game in games)
        {
            if (game.Source != "Steam" || (game.CoverPath is not null && game.BackdropPath is not null)) continue;
            if (!_artTried.Add(GameSorter.Key(game))) continue; // once per run; failures are retried next start

            if (game.CoverPath is null && await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover) is not null) fetched = true;
            if (game.BackdropPath is null && await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Backdrop) is not null) fetched = true;
        }
        if (fetched) ScheduleRefresh();
    }
}
