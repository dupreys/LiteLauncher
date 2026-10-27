using LiteLauncher.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LiteLauncher.App;

// Top-bar extras: Wi-Fi/Ethernet and volume icons, and the power menu (button next to the clock, Select, F9).
public partial class MainWindow
{
    private enum PowerAction { Sleep, Restart, ShutDown }

    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private IInputElement? _powerReturnFocus;
    private PowerAction _pendingPower;

    private bool PowerOpen => PowerPanel.Visibility == Visibility.Visible;

    // ---- Network and volume icons ----------------------------------------------------------

    private void InitStatusIcons()
    {
        _statusTimer.Tick += (_, _) => UpdateStatusIcons();
        _statusTimer.Start();

        // Plugging a cable or joining Wi-Fi changes the icon right away instead of at the next tick.
        NetworkAddressChangedEventHandler onNetworkChange = (_, _) => Dispatcher.BeginInvoke(new Action(UpdateStatusIcons));
        NetworkChange.NetworkAddressChanged += onNetworkChange;
        Activated += (_, _) => UpdateStatusIcons(); // volume may have been changed while a game was in front

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            NetworkChange.NetworkAddressChanged -= onNetworkChange;
        };

        UpdateStatusIcons();
    }

    private void UpdateStatusIcons()
    {
        var network = SystemInfo.GetNetwork();
        NetWifi.Visibility = network == NetworkKind.Wifi ? Visibility.Visible : Visibility.Collapsed;
        NetEthernet.Visibility = network == NetworkKind.Ethernet ? Visibility.Visible : Visibility.Collapsed;
        NetNone.Visibility = network == NetworkKind.None ? Visibility.Visible : Visibility.Collapsed;
        NetworkIcon.ToolTip = network switch
        {
            NetworkKind.Wifi => Loc.T("Wi-Fi connected"),
            NetworkKind.Ethernet => Loc.T("Ethernet connected"),
            _ => Loc.T("No network connection"),
        };

        if (SystemVolume.TryGet(out var level, out var muted))
        {
            VolumeIcon.Visibility = Visibility.Visible;
            var silent = muted || level <= 0.001f;
            VolWaves.Visibility = silent ? Visibility.Collapsed : Visibility.Visible;
            VolMuted.Visibility = silent ? Visibility.Visible : Visibility.Collapsed;
            VolumeIcon.ToolTip = muted ? Loc.T("Sound muted") : Loc.F("Volume {0}%", Math.Round(level * 100));
        }
        else
        {
            VolumeIcon.Visibility = Visibility.Collapsed; // no audio device
        }
    }

    // ---- Power menu ---------------------------------------------------------------------------

    private void OnPowerClick(object sender, RoutedEventArgs e) => TogglePower();

    private void OnPowerDimClick(object sender, MouseButtonEventArgs e) => ClosePower();

    private void TogglePower()
    {
        if (PowerOpen) ClosePower();
        else OpenPower();
    }

    private void OpenPower()
    {
        if (PowerOpen || QuickOpen || OverlayVisible) return; // the launch cover is above this menu
        VirtualKeyboard.Hide();
        _powerReturnFocus = Keyboard.FocusedElement;
        ShowPowerMain();
        PowerPanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(PowerSleepButton);
    }

    private void ClosePower()
    {
        if (!PowerOpen) return;
        PowerPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_powerReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void ShowPowerMain()
    {
        PowerTitle.Text = Loc.T("Power");
        PowerSubtitle.Text = Loc.T("What should the PC do?");
        PowerMain.Visibility = Visibility.Visible;
        PowerConfirm.Visibility = Visibility.Collapsed;
    }

    private void OnPowerChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<PowerAction>(tag, out var action)) return;

        if (action == PowerAction.Sleep)
        {
            RunPower(action); // reversible and quick: no confirmation needed
            return;
        }

        // Restart and shut down close everything that is open, so they ask first (Cancel is pre-selected).
        _pendingPower = action;
        var restart = action == PowerAction.Restart;
        PowerTitle.Text = Loc.T(restart ? "Restart?" : "Shut down?");
        PowerSubtitle.Text = Loc.T("Open games and apps will be closed. Unsaved progress may be lost.");
        PowerConfirmLabel.Text = Loc.T(restart ? "Restart now" : "Shut down now");
        PowerMain.Visibility = Visibility.Collapsed;
        PowerConfirm.Visibility = Visibility.Visible;
        Keyboard.Focus(PowerConfirmCancelButton);
    }

    private void OnPowerConfirmClick(object sender, RoutedEventArgs e) => RunPower(_pendingPower);

    private void OnPowerCancelClick(object sender, RoutedEventArgs e) => ClosePower();

    private void OnPowerBackClick(object sender, RoutedEventArgs e)
    {
        ShowPowerMain();
        Keyboard.Focus(_pendingPower == PowerAction.Restart ? PowerRestartButton : PowerShutdownButton);
    }

    private void RunPower(PowerAction action)
    {
        ClosePower();
        if (_saveTimer.IsEnabled) SaveSettingsNow(); // don't lose a setting changed a second ago

        try
        {
            switch (action)
            {
                case PowerAction.Sleep:
                    if (!SystemInfo.Sleep()) ShowToast("Windows could not enter sleep mode.");
                    break;
                case PowerAction.Restart:
                    RunShutdownExe("/r /t 0");
                    break;
                case PowerAction.ShutDown:
                    RunShutdownExe("/s /t 0");
                    break;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Windows did not accept the power command.");
        }
    }

    private static void RunShutdownExe(string arguments) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();

    // ---- Input ----

    /// <summary>F9 toggles the menu; Esc closes it (one step back inside the confirmation).</summary>
    private bool HandlePowerKey(Key key)
    {
        if (key == Key.F9)
        {
            TogglePower();
            return true;
        }

        if (!PowerOpen) return false;
        if (key is Key.Escape or Key.Apps)
        {
            PowerBack();
            return true;
        }
        return false;
    }

    private bool HandlePowerGamepad(GamepadButton button)
    {
        if (button == GamepadButton.Select)
        {
            TogglePower();
            return true;
        }

        if (!PowerOpen) return false;
        switch (button)
        {
            case GamepadButton.B:
                PowerBack();
                return true;
            case GamepadButton.Start:
            case GamepadButton.Y:
                return true; // nothing else may react while the menu is up
            default:
                return false; // D-pad and A work as usual
        }
    }

    private void PowerBack()
    {
        if (PowerConfirm.Visibility == Visibility.Visible) OnPowerBackClick(this, new RoutedEventArgs());
        else ClosePower();
    }
}
