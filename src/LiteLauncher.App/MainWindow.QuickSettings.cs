using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteLauncher.Core;

namespace LiteLauncher.App;

// Top bar you can reach with the controller (D-pad Up), and the quick panels behind its icons:
// volume (slider + mute) and network (list of Wi-Fi networks, connect, disconnect).
public partial class MainWindow
{
    private UIElement? _topBarReturn;          // where focus was before it went up to the top bar
    private IInputElement? _quickReturnFocus;  // the top-bar icon that opened the panel
    private CancellationTokenSource? _wifiCts;
    private string? _wifiPendingSsid;
    private bool _wifiFocusFirst;
    private bool _wifiBusy;
    private bool _quickLoading;

    private bool QuickOpen => QuickPanel.Visibility == Visibility.Visible;

    private bool InTopBar(object? element) => element is DependencyObject node && TopBar.IsAncestorOf(node);

    /// <summary>Clips a panel to its rounded corners (a plain Border only clips to a rectangle).</summary>
    private void OnRoundedPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } panel)
            panel.Clip = new RectangleGeometry(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight), 15, 15);
    }

    // ---- Reaching the top bar -----------------------------------------------------------------

    private void OnTopBarGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OldFocus is UIElement old && !InTopBar(old)) _topBarReturn = old;
    }

    /// <summary>
    /// D-pad Up on the top row of any screen goes to the top bar (Power first); the next Up does nothing.
    /// Returns false when there is something above the focused element, so normal navigation applies.
    /// </summary>
    private bool TryFocusTopBar()
    {
        if (ActiveModal() is not null || _overlayPhase != OverlayPhase.None) return false;
        if (Keyboard.FocusedElement is not UIElement focused || InTopBar(focused)) return false;

        var above = focused.PredictFocus(FocusNavigationDirection.Up);
        if (above is not null && !InTopBar(above)) return false;

        VirtualKeyboard.Hide();
        Keyboard.Focus(PowerButton);
        return true;
    }

    private void ReturnFromTopBar()
    {
        App.Sounds.Play(SoundKind.Back);
        if (_topBarReturn is { IsVisible: true, IsEnabled: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private bool HandleTopBarKey(Key key)
    {
        if (!InTopBar(Keyboard.FocusedElement)) return false;
        if (key is Key.Down or Key.Escape)
        {
            ReturnFromTopBar();
            return true;
        }
        return false;
    }

    private bool HandleTopBarGamepad(GamepadButton button)
    {
        if (!InTopBar(Keyboard.FocusedElement)) return false;
        switch (button)
        {
            case GamepadButton.Down:
            case GamepadButton.B:
                ReturnFromTopBar();
                return true;
            case GamepadButton.Up:
            case GamepadButton.Y:
                return true; // nothing above the top bar, and no search from here
            default:
                return false; // Left/Right move between the icons, A opens the focused one
        }
    }

    // ---- Opening and closing -------------------------------------------------------------------

    private void OnVolumeClick(object sender, RoutedEventArgs e) => OpenQuick(volume: true);

    private void OnNetworkClick(object sender, RoutedEventArgs e) => OpenQuick(volume: false);

    private void OnQuickDimClick(object sender, MouseButtonEventArgs e) => CloseQuick();

    private void OpenQuick(bool volume)
    {
        if (QuickOpen || PowerOpen || _overlayPhase != OverlayPhase.None) return;

        VirtualKeyboard.Hide();
        _quickReturnFocus = Keyboard.FocusedElement;
        VolumePanel.Visibility = volume ? Visibility.Visible : Visibility.Collapsed;
        WifiPanel.Visibility = volume ? Visibility.Collapsed : Visibility.Visible;
        QuickPanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);

        if (volume)
        {
            LoadQuickVolume();
            Keyboard.Focus(QuickVolumeSlider.IsEnabled ? QuickVolumeSlider : QuickPanel);
        }
        else
        {
            HideWifiPassword(restoreFocus: false);
            _wifiFocusFirst = true;
            Keyboard.Focus(WifiScanButton);
            _ = RefreshWifiAsync();
        }
    }

    private void CloseQuick()
    {
        if (!QuickOpen) return;
        _wifiCts?.Cancel();
        VirtualKeyboard.Hide();
        QuickPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        UpdateStatusIcons();

        if (_quickReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    // ---- Volume ------------------------------------------------------------------------------------

    private void LoadQuickVolume()
    {
        _quickLoading = true;
        if (SystemVolume.TryGet(out var level, out var muted))
        {
            QuickVolumeSlider.IsEnabled = true;
            QuickMuteToggle.IsEnabled = true;
            QuickVolumeSlider.Value = Math.Round(level * 100);
            QuickMuteToggle.IsChecked = muted;
            QuickVolText.Text = $"{Math.Round(level * 100)}%";
        }
        else
        {
            QuickVolumeSlider.IsEnabled = false; // no audio device
            QuickMuteToggle.IsEnabled = false;
            QuickVolText.Text = "—";
        }
        _quickLoading = false;
    }

    private void OnQuickVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_quickLoading) return;

        SystemVolume.TrySetLevel((float)(e.NewValue / 100));
        QuickVolText.Text = $"{Math.Round(e.NewValue)}%";
        if (QuickMuteToggle.IsChecked == true && e.NewValue > 0) QuickMuteToggle.IsChecked = false; // raising the volume unmutes
        UpdateStatusIcons();
    }

    private void OnQuickMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_quickLoading) return;
        SystemVolume.TrySetMute(QuickMuteToggle.IsChecked == true);
        UpdateStatusIcons();
    }

    // ---- Wi-Fi ---------------------------------------------------------------------------------------

    private async Task RefreshWifiAsync()
    {
        _wifiCts?.Cancel();
        var cts = _wifiCts = new CancellationTokenSource();

        WifiStatus.Text = "Looking for networks…";
        WifiList.Children.Clear();
        WifiDisconnectButton.IsEnabled = false;
        var kind = SystemInfo.GetNetwork();

        var interfaces = NetshRunner.RunAsync("wlan show interfaces", cts.Token);
        var profiles = NetshRunner.RunAsync("wlan show profiles", cts.Token);
        var networks = NetshRunner.RunAsync("wlan show networks mode=bssid", cts.Token);
        try
        {
            await Task.WhenAll(interfaces, profiles, networks);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || !QuickOpen) return;

        var (interfacesCode, interfacesText) = await interfaces;
        var (_, profilesText) = await profiles;
        var (networksCode, networksText) = await networks;

        if (interfacesCode != 0 || networksCode != 0)
        {
            // No adapter, Wi-Fi switched off, or the WLAN service is not running.
            WifiStatus.Text = kind == NetworkKind.Ethernet
                ? "Connected by Ethernet cable. No Wi-Fi adapter is available."
                : "No Wi-Fi adapter is available (or Wi-Fi is turned off in Windows).";
            FinishWifiLoad();
            return;
        }

        var connected = WifiNetsh.ParseConnectedSsid(interfacesText);
        var list = WifiNetsh.ParseNetworks(networksText, connected, WifiNetsh.ParseProfiles(profilesText));

        WifiStatus.Text = connected is not null ? $"Connected to {connected}"
            : kind == NetworkKind.Ethernet ? "Connected by Ethernet cable. Wi-Fi is not connected."
            : "Not connected to a Wi-Fi network.";
        WifiDisconnectButton.IsEnabled = connected is not null;

        foreach (var network in list) WifiList.Children.Add(BuildWifiRow(network));
        if (list.Count == 0)
        {
            var empty = new TextBlock { Text = "No networks found nearby.", FontSize = 14, Margin = new Thickness(2, 0, 0, 6) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            WifiList.Children.Add(empty);
        }

        FinishWifiLoad();
    }

    private void FinishWifiLoad()
    {
        if (!_wifiFocusFirst) return;
        _wifiFocusFirst = false;

        // Only move focus if the user has not already gone elsewhere.
        if (!QuickOpen || WifiPasswordPanel.Visibility == Visibility.Visible) return;
        if (!ReferenceEquals(Keyboard.FocusedElement, WifiScanButton)) return;

        WifiList.UpdateLayout();
        if (WifiList.Children.OfType<Button>().FirstOrDefault() is { } first) Keyboard.Focus(first);
    }

    private Button BuildWifiRow(WifiNetwork network)
    {
        var name = new TextBlock
        {
            Text = network.Ssid,
            FontSize = 16,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var detail = network.Connected ? "Connected" : network.Known ? "Saved" : "";
        if (network.Signal is { } signal) detail = detail.Length == 0 ? $"{signal}%" : $"{detail} · {signal}%";
        var status = new TextBlock
        {
            Text = detail,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, network.Connected ? "Accent" : "TextMuted");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(status, 1);
        layout.Children.Add(name);
        layout.Children.Add(status);

        var row = new Button
        {
            Style = (Style)FindResource("MenuRow"),
            Content = layout,
            Tag = network,
            FontSize = 16,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.Click += OnWifiRowClick;
        return row;
    }

    private void OnWifiRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WifiNetwork network }) _ = ConnectWifiAsync(network);
    }

    private async Task ConnectWifiAsync(WifiNetwork network)
    {
        if (_wifiBusy) return;
        if (network.Connected)
        {
            ShowToast($"Already connected to {network.Ssid}");
            return;
        }
        if (network.Ssid.Contains('"'))
        {
            ShowToast("Networks with quotes in the name must be joined in Windows settings.");
            return;
        }

        if (network.Known) await JoinWifiAsync(network.Ssid);
        else ShowWifiPassword(network.Ssid);
    }

    /// <summary>Asks Windows to join a saved network and waits a few seconds to see whether it worked.</summary>
    private async Task<bool> JoinWifiAsync(string ssid)
    {
        _wifiBusy = true;
        var joined = false;
        try
        {
            WifiStatus.Text = $"Connecting to {ssid}…";
            await NetshRunner.RunAsync($"wlan connect name=\"{ssid}\"");

            for (var attempt = 0; attempt < 10 && QuickOpen; attempt++)
            {
                await Task.Delay(1000);
                var (_, text) = await NetshRunner.RunAsync("wlan show interfaces");
                if (string.Equals(WifiNetsh.ParseConnectedSsid(text), ssid, StringComparison.Ordinal))
                {
                    joined = true;
                    break;
                }
            }
        }
        finally
        {
            _wifiBusy = false;
        }

        ShowToast(joined ? $"Connected to {ssid}" : $"Could not connect to {ssid}");
        UpdateStatusIcons();
        if (QuickOpen)
        {
            _wifiFocusFirst = true;
            Keyboard.Focus(WifiScanButton);
            await RefreshWifiAsync();
        }
        return joined;
    }

    private void OnWifiScanClick(object sender, RoutedEventArgs e)
    {
        _wifiFocusFirst = false;
        _ = RefreshWifiAsync();
    }

    private async void OnWifiDisconnectClick(object sender, RoutedEventArgs e)
    {
        await NetshRunner.RunAsync("wlan disconnect");
        await Task.Delay(800);
        UpdateStatusIcons();
        if (QuickOpen) await RefreshWifiAsync();
    }

    private void OnWifiSettingsClick(object sender, RoutedEventArgs e)
    {
        CloseQuick();
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            ShowToast("Could not open the Windows network settings.");
        }
    }

    // ---- Password step (networks Windows does not know yet) ---------------------------------------

    private void ShowWifiPassword(string ssid)
    {
        _wifiPendingSsid = ssid;
        WifiPasswordLabel.Text = $"Password for \"{ssid}\"";
        WifiPasswordBox.Text = "";
        WifiListPanel.Visibility = Visibility.Collapsed;
        WifiPasswordPanel.Visibility = Visibility.Visible;
        Keyboard.Focus(WifiPasswordBox);
    }

    private void HideWifiPassword(bool restoreFocus)
    {
        VirtualKeyboard.Hide();
        _wifiPendingSsid = null;
        WifiPasswordBox.Text = "";
        WifiPasswordPanel.Visibility = Visibility.Collapsed;
        WifiListPanel.Visibility = Visibility.Visible;
        if (restoreFocus) Keyboard.Focus(WifiScanButton);
    }

    private void OnWifiPasswordCancelClick(object sender, RoutedEventArgs e) => HideWifiPassword(restoreFocus: true);

    private async void OnWifiPasswordConnectClick(object sender, RoutedEventArgs e)
    {
        if (_wifiPendingSsid is not { } ssid || _wifiBusy) return;

        var password = WifiPasswordBox.Text;
        if (!WifiNetsh.IsValidPassphrase(password))
        {
            ShowToast("Wi-Fi passwords have 8 to 63 characters.");
            return;
        }

        HideWifiPassword(restoreFocus: false);
        Keyboard.Focus(WifiScanButton);

        // The profile (with the password) is only written to a temporary file for the moment netsh reads it.
        var file = Path.Combine(Path.GetTempPath(), $"litelauncher-wifi-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(file, WifiNetsh.BuildProfileXml(ssid, password));
            var (code, _) = await NetshRunner.RunAsync($"wlan add profile filename=\"{file}\" user=current");
            if (code != 0)
            {
                ShowToast("Windows did not accept this network.");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not prepare the network settings.");
            return;
        }
        finally
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // A wrong password must not leave a broken saved network behind.
        if (!await JoinWifiAsync(ssid)) await NetshRunner.RunAsync($"wlan delete profile name=\"{ssid}\"");
    }

    // ---- Input ----------------------------------------------------------------------------------------

    private bool HandleQuickKey(Key key)
    {
        if (!QuickOpen) return false;

        if (key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, WifiPasswordBox))
        {
            OnWifiPasswordConnectClick(WifiPasswordBox, new RoutedEventArgs());
            return true;
        }
        if (key is Key.Escape or Key.Apps)
        {
            QuickBack();
            return true;
        }
        return false;
    }

    private bool HandleQuickGamepad(GamepadButton button)
    {
        if (!QuickOpen) return false;
        switch (button)
        {
            case GamepadButton.B:
                QuickBack();
                return true;
            case GamepadButton.Start:
            case GamepadButton.Y:
                return true;
            default:
                return false; // D-pad, A, and Left/Right on the slider work as usual
        }
    }

    private void QuickBack()
    {
        if (VirtualKeyboard.IsVisible)
        {
            VirtualKeyboard.Hide();
            return;
        }
        if (WifiPasswordPanel.Visibility == Visibility.Visible) HideWifiPassword(restoreFocus: true);
        else CloseQuick();
    }
}
