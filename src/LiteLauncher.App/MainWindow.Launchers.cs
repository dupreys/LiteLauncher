using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Epic;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// The "Launchers" home tile (Steam + Epic) and tracking of Epic games while they run.
public partial class MainWindow
{
    private EpicManifestWatcher? _epicWatcher;
    private readonly PlayTracker _epicTracker = new();
    private IInputElement? _launchersReturnFocus;

    private bool LaunchersOpen => LaunchersPanel.Visibility == Visibility.Visible;

    private void StartEpicTracking()
    {
        _epicWatcher = new EpicManifestWatcher(EpicProvider.DefaultManifestDirectory());
        _epicWatcher.Changed += () => Dispatcher.BeginInvoke(new Action(ScheduleRefresh));
        Closed += (_, _) => _epicWatcher?.Dispose();
    }

    /// <summary>Called on every running-game poll: notices Epic games starting/stopping and records play time.</summary>
    private void PollEpicGames()
    {
        var epicGames = _items.Where(i => i.Game.Source == EpicProvider.ProviderName && i.Game.ExecutablePath is not null).ToList();
        if (epicGames.Count == 0 && _epicTracker.Running.Count == 0) return;

        var running = epicGames.Where(i => IsGameRunning(i.Game, 0)).Select(i => GameSorter.Key(i.Game));
        var now = DateTimeOffset.Now;
        var (started, stopped) = _epicTracker.Update(running, now);

        foreach (var key in started)
        {
            _playedNow[key] = now;
            Record(key).LastPlayed = now;
            var item = _items.FirstOrDefault(i => GameSorter.Key(i.Game) == key);
            if (item is not null)
            {
                _nowPlaying = item.Name;
                ShowToast(Loc.F("Now playing {0}", item.Name));
            }
        }
        foreach (var (key, minutes) in stopped)
        {
            var record = Record(key);
            record.Minutes += minutes;
            record.LastPlayed = now;
            if (_runningAppId == 0) _nowPlaying = null;
        }

        if (started.Count > 0 || stopped.Count > 0)
        {
            SaveSettingsNow();
            ScheduleRefresh();
            UpdateAccountUi();
        }
    }

    private static PlayRecord Record(string key)
    {
        var records = App.Settings.PlayRecords;
        if (!records.TryGetValue(key, out var r)) records[key] = r = new PlayRecord();
        return r;
    }

    // ---- Launchers tile ----

    private void OnLaunchersTileClick(object sender, RoutedEventArgs e)
    {
        _launchersReturnFocus = Keyboard.FocusedElement;
        var steamCount = _items.Count(i => i.Game.Source == SteamProvider.ProviderName);
        var epicCount = _items.Count(i => i.Game.Source == EpicProvider.ProviderName);
        var steamOk = _providers.Any(p => p.Name == SteamProvider.ProviderName && p.IsAvailable);
        var epicOk = _providers.Any(p => p.Name == EpicProvider.ProviderName && p.IsAvailable);

        LauncherSteamSub.Text = steamOk ? Loc.F("{0} games installed", steamCount) : Loc.T("Not found on this PC");
        LauncherEpicSub.Text = epicOk ? Loc.F("{0} games installed", epicCount) : Loc.T("Not found on this PC");
        LaunchersPanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(LauncherSteamButton);
    }

    private void CloseLaunchers()
    {
        if (!LaunchersOpen) return;
        LaunchersPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_launchersReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void OnLaunchersDimClick(object sender, MouseButtonEventArgs e) => CloseLaunchers();

    // The chosen client opens and our window steps aside, so it is the one you see
    // (it used to open behind the launcher). Alt+Tab or the taskbar brings LiteLauncher back.
    private void OnOpenSteamLauncher(object sender, RoutedEventArgs e)
    {
        CloseLaunchers();
        if (OpenSteamUrl("steam://nav/games")) StepAsideForClient();
    }

    private void OnOpenEpicLauncher(object sender, RoutedEventArgs e)
    {
        CloseLaunchers();
        try
        {
            // Start the program itself (what the Start menu shortcut does): it opens on the Epic home page, or
            // brings the window forward when it is already running. The store link is only a fallback.
            var exe = EpicProvider.FindLauncherExe();
            var start = exe is not null
                ? new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! }
                : new ProcessStartInfo("com.epicgames.launcher://store") { UseShellExecute = true };
            Process.Start(start)?.Dispose();
            StepAsideForClient();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            ShowToast("The Epic Games Launcher is not installed on this PC.");
        }
    }

    private void StepAsideForClient()
    {
        // A short delay lets the client's window appear first, so Windows hands focus to it.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_overlayPhase == OverlayPhase.None) WindowState = WindowState.Minimized;
        };
        timer.Start();
    }

    private bool HandleLaunchersKey(Key key)
    {
        if (!LaunchersOpen) return false;
        if (key is Key.Escape or Key.Back)
        {
            CloseLaunchers();
            return true;
        }
        return false;
    }

    private bool HandleLaunchersGamepad(GamepadButton button)
    {
        if (!LaunchersOpen) return false;
        if (button is GamepadButton.B or GamepadButton.Start) { CloseLaunchers(); return true; }
        return button == GamepadButton.Y;
    }
}
