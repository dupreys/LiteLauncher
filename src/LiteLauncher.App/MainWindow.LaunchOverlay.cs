using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Launch overlay: a full-screen card shown from the moment you pick a game until it closes.
//   Countdown -> a few seconds where B/Esc cancels, then the game is handed to Steam
//   Starting  -> waiting for Steam to report the game as running (B/Esc closes the overlay)
//   Playing   -> stays behind the game; when Steam reports it closed, the overlay closes and Home comes back
public partial class MainWindow
{
    private enum OverlayPhase { None, Countdown, Starting, Playing }

    private const double CountdownSeconds = 3;
    private const double OverlayBarWidth = 520; // keep equal to the progress track width in the XAML
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private OverlayPhase _overlayPhase = OverlayPhase.None;
    private GameItem? _overlayItem;
    private DateTime _overlayPhaseStart;
    private DateTime _launchBegan;
    private static readonly TimeSpan KeepClientsMinimizedFor = TimeSpan.FromSeconds(90);

    private readonly DispatcherTimer _forceTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _forceArmed;

    // Force close makes sense once the game was handed to the store: it may be frozen, or never have opened.
    private bool OverlayForceable => _overlayPhase is OverlayPhase.Starting or OverlayPhase.Playing;

    // The overlay can be tucked away into a chip in the top bar (see MinimizeOverlay) while the game keeps running,
    // so you can use the launcher (and later other apps) at the same time.
    private bool _overlayMinimized;

    private bool OverlayVisible => _overlayPhase != OverlayPhase.None && !_overlayMinimized;

    private bool OverlayHomeable => _overlayPhase is OverlayPhase.Starting or OverlayPhase.Playing;

    private bool OverlayCancelable => _overlayPhase is OverlayPhase.Countdown or OverlayPhase.Starting;

    private void ShowLaunchOverlay(GameItem item)
    {
        if (_overlayPhase != OverlayPhase.None)
        {
            if (_overlayMinimized) RestoreOverlay(); // a game is already running: show it instead of starting another
            return;
        }
        VirtualKeyboard.Hide();

        _overlayItem = item;
        item.EnsureCoverLoaded();
        OverlayCover.Source = item.Cover;
        OverlayInitial.Text = item.Initial;
        OverlayTitle.Text = item.Name;
        OverlaySource.Text = $"LITELAUNCHER · {item.Game.Source.ToUpperInvariant()}";
        _ = LoadOverlayBackdropAsync(item);

        if (_overlayTimer.Tag is null)
        {
            _forceTimer.Tick += (_, _) => DisarmForce();
            _overlayTimer.Tick += (_, _) => OnOverlayTick();
            _overlayTimer.Tag = true;
        }

        LaunchOverlay.Visibility = Visibility.Visible;
        LaunchOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        OverlaySpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever });

        SetOverlayPhase(OverlayPhase.Countdown);
        _overlayTimer.Start();
        Keyboard.Focus(OverlayCancelButton);
    }

    private async Task LoadOverlayBackdropAsync(GameItem item)
    {
        var path = item.Game.BackdropPath ?? item.Game.CoverPath;
        var image = path is null ? null : await ImageLoader.LoadAsync(path, decodeWidth: 1280);
        ConfigureBackdrop(OverlayBackdrop, image);
        OverlayBackdrop.Source = image;
        if (OverlayCover.Source is null) OverlayCover.Source = item.Cover; // cover may have finished meanwhile
    }

    private void SetOverlayPhase(OverlayPhase phase)
    {
        _overlayPhase = phase;
        _overlayPhaseStart = DateTime.UtcNow;

        OverlayCancelButton.Visibility = OverlayCancelable ? Visibility.Visible : Visibility.Collapsed;
        OverlayForceButton.Visibility = OverlayForceable ? Visibility.Visible : Visibility.Collapsed;
        OverlayResumeButton.Visibility = phase == OverlayPhase.Playing ? Visibility.Visible : Visibility.Collapsed;
        OverlayHomeButton.Visibility = OverlayHomeable ? Visibility.Visible : Visibility.Collapsed;
        OverlayHomeLabel.Text = Loc.T(phase == OverlayPhase.Playing ? "B   ⌂  Home" : "⌂  Home");
        if (_overlayItem is { } shown)
            OverlaySource.Text = (phase == OverlayPhase.Playing ? Loc.T("NOW PLAYING") : "LITELAUNCHER") + $" · {shown.Game.Source.ToUpperInvariant()}";
        DisarmForce();
        switch (phase)
        {
            case OverlayPhase.Countdown:
                OverlayHint.Text = Loc.T("You can still cancel the launch.");
                OverlayCancelLabel.Text = Loc.T("B   ✕  Cancel launch");
                break;
            case OverlayPhase.Starting:
                _launchBegan = DateTime.UtcNow;
                OverlayStatus.Text = Loc.T("Starting game…");
                OverlayHint.Text = Loc.T("Waiting for the store to open the game.");
                OverlayCancelLabel.Text = Loc.T("B   ✕  Close");
                break;
            case OverlayPhase.Playing:
                OverlayHint.Text = Loc.T("Press A to go back to the game, or B to go Home and keep it running. LiteLauncher returns to Home when you close the game. With the game in front, hold Select + Start on the controller to come back here.");
                OverlayProgress.Width = OverlayBarWidth;
                break;
        }

        // The Cancel button disappears in Playing; keep focus on a button that is still there.
        if (phase == OverlayPhase.Playing && IsActive && !_overlayMinimized) Keyboard.Focus(OverlayResumeButton);
    }

    private void OnOverlayTick()
    {
        var elapsed = DateTime.UtcNow - _overlayPhaseStart;
        switch (_overlayPhase)
        {
            case OverlayPhase.Countdown:
                var left = Math.Max(0, CountdownSeconds - elapsed.TotalSeconds);
                OverlayStatus.Text = Loc.F("Game starts in {0} sec.", Math.Ceiling(left).ToString("0"));
                OverlayProgress.Width = OverlayBarWidth * Math.Min(1, elapsed.TotalSeconds / CountdownSeconds);
                if (left <= 0)
                {
                    if (_overlayItem is not null && StartGameNow(_overlayItem)) SetOverlayPhase(OverlayPhase.Starting);
                    else CloseLaunchOverlay(returnHome: false);
                }
                break;

            case OverlayPhase.Starting:
                // Indeterminate: a short bar sweeping back and forth.
                OverlayProgress.Width = OverlayBarWidth * (0.25 + 0.75 * Math.Abs(Math.Sin(elapsed.TotalSeconds * 1.5)));
                if (elapsed > StartTimeout)
                {
                    CloseLaunchOverlay(returnHome: false);
                    ShowToast("The game did not start. Check the store app for messages.");
                }
                break;

            case OverlayPhase.Playing:
                OverlayStatus.Text = Loc.F("Playing now · {0}", elapsed.ToString(@"h\:mm\:ss"));
                if (++_muteTicks % 7 == 0) _ = UpdateGameMuteAsync();
                break;
        }
    }

    // The game is silenced while the launcher (overlay or Home) is in front and gets its sound back as soon as the
    // game's own window is in front again. Only the game's audio session is touched, not the system volume.
    private int _muteTicks;
    private int _muteRefresh;
    private bool _muteBusy;
    private bool _gameMuted;

    /// <param name="force">true/false silences/restores the game right now (used when the screen changes); null decides from the window in front.</param>
    private async Task UpdateGameMuteAsync(bool? force = null)
    {
        if ((_muteBusy && force is null) || _overlayItem is not { } item || _overlayPhase != OverlayPhase.Playing) return;
        _muteBusy = true;
        try
        {
            var foreground = GameProcessKiller.ForegroundProcessId();
            var self = (uint)Environment.ProcessId;
            var muted = _gameMuted;
            var refresh = ++_muteRefresh % 6 == 0; // new audio sessions can appear after the game started
            _gameMuted = await Task.Run(() =>
            {
                var pids = GameProcessKiller.GamePids(item.Game);
                // Programs with the game's own file name count too, in case the game runs from outside its install folder.
                if (!string.IsNullOrEmpty(item.Game.ExecutablePath))
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(item.Game.ExecutablePath)))
                    {
                        pids.Add((uint)p.Id);
                        p.Dispose();
                    }
                bool? want = force ?? (foreground == self ? true : pids.Contains(foreground) ? false : null);
                if (want is not { } mute) return muted; // some other program is in front: leave things as they are
                if (mute != muted || refresh || force is not null) AppAudio.SetMute(pids, mute);
                return mute;
            });
        }
        finally
        {
            _muteBusy = false;
        }
    }

    private void RestoreGameAudio(Game game)
    {
        if (!_gameMuted) return;
        _gameMuted = false;
        _ = Task.Run(() => AppAudio.SetMute(GameProcessKiller.GamePids(game), false));
    }

    /// <summary>Called on every running-game poll (about every 2 seconds) with Steam's running app id.</summary>
    private void UpdateLaunchOverlay(int runningAppId)
    {
        if (_overlayItem is null) return;

        // Steam/Epic like to pop their own window up while a game starts; keep them minimized for the first stretch.
        if (_overlayPhase is OverlayPhase.Starting or OverlayPhase.Playing && DateTime.UtcNow - _launchBegan < KeepClientsMinimizedFor)
            StoreWindows.MinimizeClients();

        var isOurGame = IsGameRunning(_overlayItem.Game, runningAppId);

        if (_overlayPhase == OverlayPhase.Starting && isOurGame)
        {
            SetOverlayPhase(OverlayPhase.Playing);
        }
        else if (_overlayPhase == OverlayPhase.Playing && !isOurGame)
        {
            CloseLaunchOverlay(returnHome: true);
        }
    }

    /// <summary>Steam publishes the running game; for other stores we look for the game's process.</summary>
    private static bool IsGameRunning(Game game, int steamRunningAppId)
    {
        if (game.Source == SteamProvider.ProviderName)
            return steamRunningAppId > 0 && game.Id == steamRunningAppId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(game.ExecutablePath)) return false;
        var processes = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(game.ExecutablePath));
        var running = processes.Length > 0;
        foreach (var p in processes) p.Dispose();
        return running;
    }

    private void CloseLaunchOverlay(bool returnHome)
    {
        _overlayTimer.Stop();
        if (_overlayItem is { } closing) RestoreGameAudio(closing.Game);
        DisarmForce();
        _overlayMinimized = false;
        NowPlayingChip.Visibility = Visibility.Collapsed;
        _overlayPhase = OverlayPhase.None;
        _overlayItem = null;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) =>
        {
            if (_overlayPhase != OverlayPhase.None) return; // a new launch started during the fade
            LaunchOverlay.Visibility = Visibility.Collapsed;
            OverlaySpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        };
        LaunchOverlay.BeginAnimation(OpacityProperty, fade);

        ShowView(AppView.Home, sound: false);
        if (returnHome)
        {
            BringLauncherToFront();

            // The store client sometimes grabs focus again a moment after the game exits (cloud sync, "game closed"
            // windows): take it back once more so you always land on the LiteLauncher home screen.
            var retake = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            retake.Tick += (_, _) =>
            {
                retake.Stop();
                if (_overlayPhase == OverlayPhase.None) BringLauncherToFront();
            };
            retake.Start();
        }

        // Once everything has settled, make sure the keyboard/controller focus is back on the Home screen.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_overlayPhase == OverlayPhase.None && Keyboard.FocusedElement is not UIElement { IsVisible: true } || Keyboard.FocusedElement is Window)
                FocusView(_current);
        }));
    }

    /// <summary>Windows won't let a background app steal focus directly; a brief Topmost flip does it politely.</summary>
    private void BringLauncherToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void CancelLaunchOverlay()
    {
        if (!OverlayCancelable) return;
        var wasCountdown = _overlayPhase == OverlayPhase.Countdown;
        App.Sounds.Play(SoundKind.Move);
        CloseLaunchOverlay(returnHome: false);
        ShowToast(wasCountdown ? "Launch cancelled" : "Overlay closed");
    }

    /// <summary>Hides the overlay but keeps the game session: a chip in the top bar brings it back.</summary>
    private void MinimizeOverlay()
    {
        if (!OverlayHomeable || _overlayMinimized || _overlayItem is not { } item) return;

        _overlayMinimized = true;
        App.Sounds.Play(SoundKind.Back);
        NowPlayingText.Text = item.Name;
        NowPlayingChip.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) =>
        {
            if (_overlayMinimized) LaunchOverlay.Visibility = Visibility.Collapsed;
        };
        LaunchOverlay.BeginAnimation(OpacityProperty, fade);

        ShowView(AppView.Home, sound: false);
        _ = UpdateGameMuteAsync(force: true); // the game goes quiet as soon as you are back on Home
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_overlayMinimized) FocusView(AppView.Home);
        }));
    }

    private void RestoreOverlay()
    {
        if (!_overlayMinimized || _overlayItem is null) return;

        _overlayMinimized = false;
        NowPlayingChip.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Select);
        VirtualKeyboard.Hide();

        LaunchOverlay.Visibility = Visibility.Visible;
        LaunchOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        Keyboard.Focus(_overlayPhase == OverlayPhase.Playing ? OverlayResumeButton : OverlayCancelButton);
        _ = UpdateGameMuteAsync(force: true);
    }

    private void OnOverlayHomeClick(object sender, RoutedEventArgs e) => MinimizeOverlay();

    private void OnNowPlayingClick(object sender, RoutedEventArgs e) => RestoreOverlay();

    private void OnOverlayCancelClick(object sender, RoutedEventArgs e) => CancelLaunchOverlay();

    // ---- Force close: the first press asks, the second press (within 4 s) ends the game's processes ----

    private void DisarmForce()
    {
        _forceTimer.Stop();
        _forceArmed = false;
        OverlayForceLabel.Text = Loc.T("A   ⏻  Force close game");
    }

    private async void OnOverlayForceClick(object sender, RoutedEventArgs e)
    {
        if (_overlayItem is not { } item || !OverlayForceable) return;

        if (!_forceArmed)
        {
            _forceArmed = true;
            OverlayForceLabel.Text = Loc.T("A   ⏻  Press again to confirm");
            _forceTimer.Stop();
            _forceTimer.Start();
            App.Sounds.Play(SoundKind.Move);
            return;
        }

        DisarmForce();
        OverlayForceButton.IsEnabled = false;
        OverlayStatus.Text = Loc.T("Closing the game…");

        int ended;
        try
        {
            ended = await Task.Run(() => GameProcessKiller.Kill(item.Game));
        }
        finally
        {
            OverlayForceButton.IsEnabled = true;
        }

        if (!ReferenceEquals(_overlayItem, item)) return; // the overlay closed on its own meanwhile

        CloseLaunchOverlay(returnHome: true);
        ShowToast(ended > 0
            ? Loc.F("{0} was closed.", item.Name)
            : Loc.F("No running process of {0} was found. If it is still open, close it from Task Manager.", item.Name));
    }

    /// <summary>Controller combo (hold Select + Start): works while the game is in front, to get back to this screen.</summary>
    private void OnGamepadCombo()
    {
        if (!OverlayForceable) return;
        BringLauncherToFront();
        _ = UpdateGameMuteAsync(force: true);
        if (_overlayMinimized)
        {
            Keyboard.Focus(NowPlayingChip);
            return;
        }
        Keyboard.Focus(_overlayPhase == OverlayPhase.Playing ? OverlayResumeButton : OverlayForceButton);
    }

    private void OnOverlayResumeClick(object sender, RoutedEventArgs e)
    {
        if (_overlayItem is not { } item || _overlayPhase != OverlayPhase.Playing) return;

        if (GameProcessKiller.BringToFront(item.Game))
        {
            _ = UpdateGameMuteAsync(force: false); // back in the game: its sound returns
            return;
        }

        // No window of its own to bring forward (some games hide it): asking Steam to start it again just focuses it.
        if (item.Game.Source == "Steam" && OpenSteamUrl($"steam://rungameid/{item.Game.Id}")) return;
        ShowToast("Could not find the game window. Use Alt+Tab to switch to it.");
    }

    /// <summary>While the overlay is up it owns the keyboard: Esc/Backspace/Enter cancel, everything else is ignored.</summary>
    private bool HandleOverlayKey(Key key)
    {
        if (!OverlayVisible) return false;
        switch (key)
        {
            case Key.Escape or Key.Back:
                if (OverlayCancelable) CancelLaunchOverlay();
                else MinimizeOverlay();
                break;
            case Key.Enter or Key.Space:
                ActivateFocused(); // presses whichever overlay button has focus
                break;
            case Key.Left or Key.Right:
                MoveOverlayFocus(key == Key.Left ? -1 : 1);
                break;
        }
        return key != Key.F11; // keep fullscreen toggle working
    }

    /// <summary>
    /// Left/Right inside the overlay: steps through the visible buttons in order. (Geometric focus search could jump to
    /// the Home screen behind the overlay, which the focus trap then blocked, leaving the selection stuck.)
    /// </summary>
    private void MoveOverlayFocus(int step)
    {
        var buttons = new[] { OverlayCancelButton, OverlayResumeButton, OverlayHomeButton, OverlayForceButton }
            .Where(b => b.Visibility == Visibility.Visible && b.IsEnabled)
            .ToList();
        if (buttons.Count == 0) return;

        var index = buttons.FindIndex(b => b.IsKeyboardFocused);
        var next = index < 0 ? 0 : Math.Clamp(index + step, 0, buttons.Count - 1);
        if (next != index) Keyboard.Focus(buttons[next]);
    }

    private bool HandleOverlayGamepad(GamepadButton button)
    {
        if (!OverlayVisible) return false;
        switch (button)
        {
            case GamepadButton.A:
                ActivateFocused();
                break;
            case GamepadButton.B:
                if (OverlayCancelable) CancelLaunchOverlay();
                else MinimizeOverlay();
                break;
            case GamepadButton.Left or GamepadButton.Right:
                MoveOverlayFocus(button == GamepadButton.Left ? -1 : 1);
                break;
        }
        return true;
    }
}
