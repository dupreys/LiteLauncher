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
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private OverlayPhase _overlayPhase = OverlayPhase.None;
    private GameItem? _overlayItem;
    private DateTime _overlayPhaseStart;

    private readonly DispatcherTimer _forceTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _forceArmed;

    // Force close makes sense once the game was handed to the store: it may be frozen, or never have opened.
    private bool OverlayForceable => _overlayPhase is OverlayPhase.Starting or OverlayPhase.Playing;

    private bool OverlayCancelable => _overlayPhase is OverlayPhase.Countdown or OverlayPhase.Starting;

    private void ShowLaunchOverlay(GameItem item)
    {
        if (_overlayPhase != OverlayPhase.None) return;
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
        DisarmForce();
        switch (phase)
        {
            case OverlayPhase.Countdown:
                OverlayHint.Text = "You can still cancel the launch.";
                OverlayCancelLabel.Text = "B   ✕  Cancel launch";
                break;
            case OverlayPhase.Starting:
                OverlayStatus.Text = "Starting game…";
                OverlayHint.Text = "Waiting for the store to open the game.";
                OverlayCancelLabel.Text = "B   ✕  Close";
                break;
            case OverlayPhase.Playing:
                OverlayHint.Text = "LiteLauncher returns to Home when you close the game. Hold Select + Start on the controller to come back here.";
                OverlayProgress.Width = 240;
                break;
        }

        // The Cancel button disappears in Playing; keep focus on a button that is still there.
        if (phase == OverlayPhase.Playing && IsActive) Keyboard.Focus(OverlayForceButton);
    }

    private void OnOverlayTick()
    {
        var elapsed = DateTime.UtcNow - _overlayPhaseStart;
        switch (_overlayPhase)
        {
            case OverlayPhase.Countdown:
                var left = Math.Max(0, CountdownSeconds - elapsed.TotalSeconds);
                OverlayStatus.Text = $"Game starts in {Math.Ceiling(left):0} sec.";
                OverlayProgress.Width = 240 * Math.Min(1, elapsed.TotalSeconds / CountdownSeconds);
                if (left <= 0)
                {
                    if (_overlayItem is not null && StartGameNow(_overlayItem)) SetOverlayPhase(OverlayPhase.Starting);
                    else CloseLaunchOverlay(returnHome: false);
                }
                break;

            case OverlayPhase.Starting:
                // Indeterminate: a short bar sweeping back and forth.
                OverlayProgress.Width = 60 + 180 * Math.Abs(Math.Sin(elapsed.TotalSeconds * 1.5));
                if (elapsed > StartTimeout)
                {
                    CloseLaunchOverlay(returnHome: false);
                    ShowToast("The game did not start. Check the store app for messages.");
                }
                break;

            case OverlayPhase.Playing:
                OverlayStatus.Text = $"Playing now · {elapsed:h\\:mm\\:ss}";
                break;
        }
    }

    /// <summary>Called on every running-game poll (about every 2 seconds) with Steam's running app id.</summary>
    private void UpdateLaunchOverlay(int runningAppId)
    {
        if (_overlayItem is null) return;
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
        DisarmForce();
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
        if (returnHome) BringLauncherToFront();

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

    private void OnOverlayCancelClick(object sender, RoutedEventArgs e) => CancelLaunchOverlay();

    // ---- Force close: the first press asks, the second press (within 4 s) ends the game's processes ----

    private void DisarmForce()
    {
        _forceTimer.Stop();
        _forceArmed = false;
        OverlayForceLabel.Text = "A   ⏻  Force close game";
    }

    private async void OnOverlayForceClick(object sender, RoutedEventArgs e)
    {
        if (_overlayItem is not { } item || !OverlayForceable) return;

        if (!_forceArmed)
        {
            _forceArmed = true;
            OverlayForceLabel.Text = "A   ⏻  Press again to confirm";
            _forceTimer.Stop();
            _forceTimer.Start();
            App.Sounds.Play(SoundKind.Move);
            return;
        }

        DisarmForce();
        OverlayForceButton.IsEnabled = false;
        OverlayStatus.Text = "Closing the game…";

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
            ? $"{item.Name} was closed."
            : $"No running process of {item.Name} was found. If it is still open, close it from Task Manager.");
    }

    /// <summary>Controller combo (hold Select + Start): works while the game is in front, to get back to this screen.</summary>
    private void OnGamepadCombo()
    {
        if (!OverlayForceable) return;
        BringLauncherToFront();
        Keyboard.Focus(OverlayForceButton);
    }

    /// <summary>While the overlay is up it owns the keyboard: Esc/Backspace/Enter cancel, everything else is ignored.</summary>
    private bool HandleOverlayKey(Key key)
    {
        if (_overlayPhase == OverlayPhase.None) return false;
        switch (key)
        {
            case Key.Escape or Key.Back:
                CancelLaunchOverlay();
                break;
            case Key.Enter or Key.Space:
                ActivateFocused(); // presses whichever overlay button has focus
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                Move(key switch
                {
                    Key.Left => FocusNavigationDirection.Left,
                    Key.Right => FocusNavigationDirection.Right,
                    Key.Up => FocusNavigationDirection.Up,
                    _ => FocusNavigationDirection.Down,
                });
                break;
        }
        return key != Key.F11; // keep fullscreen toggle working
    }

    private bool HandleOverlayGamepad(GamepadButton button)
    {
        if (_overlayPhase == OverlayPhase.None) return false;
        switch (button)
        {
            case GamepadButton.A:
                ActivateFocused();
                break;
            case GamepadButton.B:
                CancelLaunchOverlay();
                break;
            case GamepadButton.Left or GamepadButton.Right or GamepadButton.Up or GamepadButton.Down:
                Move(button switch
                {
                    GamepadButton.Left => FocusNavigationDirection.Left,
                    GamepadButton.Right => FocusNavigationDirection.Right,
                    GamepadButton.Up => FocusNavigationDirection.Up,
                    _ => FocusNavigationDirection.Down,
                });
                break;
        }
        return true;
    }
}
