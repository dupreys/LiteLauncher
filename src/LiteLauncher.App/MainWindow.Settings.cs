using LiteLauncher.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Settings screen: sound, theme and Steam account.
public partial class MainWindow
{
    private static readonly HttpClient Http = CreateHttpClient();

    // True while values are being pushed into the controls, so their change events don't write back.
    private bool _loadingUi = true;

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Dictionary<string, UIElement> _panels = new();
    private ImageSource? _avatar;        // Steam picture
    private ImageSource? _customAvatar;  // picture chosen by the user
    private CancellationTokenSource? _signInCts;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LiteLauncher/0.2");
        return client;
    }

    private void InitSettingsUi()
    {
        _panels = new Dictionary<string, UIElement>
        {
            ["Sound"] = PanelSound,
            ["Theme"] = PanelTheme,
            ["Profile"] = PanelProfile,
            ["Account"] = PanelAccount,
            ["Integrations"] = PanelIntegrations,
            ["General"] = PanelGeneral,
        };
        _saveTimer.Tick += (_, _) => SaveSettingsNow();

        _loadingUi = true;
        var settings = App.Settings;

        BuildSwatches(AccentSwatches, ThemeManager.AccentColors, settings.Accent, hex =>
        {
            App.Settings.Accent = hex;
            OnThemeChanged();
        });
        BuildSwatches(BackgroundSwatches, ThemeManager.BackgroundColors, settings.Background, hex =>
        {
            App.Settings.Background = hex;
            OnThemeChanged();
        });
        BuildPresetAvatars();
        BuildWallpaperChoices();

        DimSlider.Value = settings.BackdropDim * 100;
        ShowBackdropToggle.IsChecked = settings.ShowBackdrop;
        UseBackgroundColorToggle.IsChecked = settings.UseBackgroundColor;
        UiSoundsToggle.IsChecked = settings.UiSounds;
        UiVolumeSlider.Value = settings.UiVolume * 100;
        FullscreenToggle.IsChecked = settings.StartFullscreen;
        LoadExtrasUi();
        NameBox.Text = settings.CustomName ?? string.Empty;
        SteamApiKeyBox.Text = settings.SteamWebApiKey ?? string.Empty;
        GridDbKeyBox.Text = settings.SteamGridDbApiKey ?? string.Empty;

        if (SystemVolume.TryGet(out var level, out var muted))
        {
            SysVolumeSlider.Value = Math.Round(level * 100);
            MuteToggle.IsChecked = muted;
        }
        else
        {
            SysVolumeSlider.IsEnabled = false; // no playback device
            MuteToggle.IsEnabled = false;
        }


        ShowSettingsPanel("Sound");
        UpdateSliderTexts();
        UpdateAccountUi();
        _loadingUi = false;
    }

    // ---- Persistence -------------------------------------------------------------------

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start(); // debounce: sliders fire many changes per second
    }

    private void SaveSettingsNow()
    {
        _saveTimer.Stop();
        try
        {
            App.Store.Save(App.Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not save settings.");
        }
    }

    // ---- Side menu ---------------------------------------------------------------------

    private void OnSideMenuFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Button { Tag: string name }) ShowSettingsPanel(name);
    }

    private void OnSideMenuClick(object sender, RoutedEventArgs e) => Move(FocusNavigationDirection.Right);

    private void ShowSettingsPanel(string name)
    {
        foreach (var (key, panel) in _panels)
            panel.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed;

        SettingsScroll.ScrollToTop(); // a long panel (Profile) must not open scrolled down

        foreach (var button in new[] { MenuSound, MenuTheme, MenuProfile, MenuAccount, MenuIntegrations, MenuGeneral })
            button.FontWeight = (string?)button.Tag == name ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // ---- Sound ---------------------------------------------------------------------------

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi) return;

        UpdateSliderTexts();
        var fraction = e.NewValue / 100;

        if (ReferenceEquals(sender, SysVolumeSlider))
        {
            SystemVolume.TrySetLevel((float)fraction);
        }
        else if (ReferenceEquals(sender, UiVolumeSlider))
        {
            App.Settings.UiVolume = fraction;
            App.Sounds.Volume = fraction;
            App.Sounds.Play(SoundKind.Move); // audible preview at the new volume
            QueueSave();
        }
        else if (ReferenceEquals(sender, DimSlider))
        {
            App.Settings.BackdropDim = fraction;
            ApplyBackdropSettings();
            QueueSave();
        }
    }

    private void UpdateSliderTexts()
    {
        SysVolText.Text = SysVolumeSlider.IsEnabled ? $"{SysVolumeSlider.Value:0}" : "n/a";
        UiVolText.Text = $"{UiVolumeSlider.Value:0}";
        DimText.Text = $"{DimSlider.Value:0}%";
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || sender is not ToggleButton toggle) return;
        var on = toggle.IsChecked == true;

        if (HandleExtrasToggle(toggle, on))
        {
            // handled in MainWindow.Extras.cs
        }
        else if (ReferenceEquals(toggle, MuteToggle))
        {
            SystemVolume.TrySetMute(on);
        }
        else if (ReferenceEquals(toggle, UiSoundsToggle))
        {
            App.Settings.UiSounds = on;
            App.Sounds.Enabled = on;
            if (on) App.Sounds.Play(SoundKind.Select);
            QueueSave();
        }
        else if (ReferenceEquals(toggle, ShowBackdropToggle))
        {
            App.Settings.ShowBackdrop = on;
            ApplyBackdropSettings();
            QueueSave();
        }
        else if (ReferenceEquals(toggle, UseBackgroundColorToggle))
        {
            App.Settings.UseBackgroundColor = on;
            ThemeManager.Apply(App.Settings);
            ApplyBackdropSettings();
            QueueSave();
        }
        else if (ReferenceEquals(toggle, FullscreenToggle))
        {
            App.Settings.StartFullscreen = on;
            QueueSave();
        }
    }

    // ---- Theme ---------------------------------------------------------------------------

    private void BuildSwatches(WrapPanel panel, (string Name, string Hex)[] colours, string selectedHex, Action<string> onPick)
    {
        panel.Children.Clear();
        var style = (Style)FindResource("Swatch");

        foreach (var (name, hex) in colours)
        {
            var button = new Button
            {
                Style = style,
                Background = new SolidColorBrush(ThemeManager.Parse(hex, Colors.Gray)),
                ToolTip = name,
                Tag = hex,
                Margin = new Thickness(0, 0, 10, 10),
            };
            button.Click += (_, _) =>
            {
                MarkSwatch(panel, hex);
                onPick(hex);
            };
            panel.Children.Add(button);
        }

        MarkSwatch(panel, selectedHex);
    }

    private static void MarkSwatch(WrapPanel panel, string hex)
    {
        foreach (var button in panel.Children.OfType<Button>())
            button.Content = string.Equals(button.Tag as string, hex, StringComparison.OrdinalIgnoreCase) ? "✓" : null;
    }

    private void OnThemeChanged()
    {
        ThemeManager.Apply(App.Settings);
        QueueSave();
    }

    // ---- Steam account -----------------------------------------------------------------------

    /// <summary>
    /// Refreshes every place that shows the profile. The name/picture shown in the launcher are the custom
    /// ones when set, otherwise the Steam ones; the "Steam account" panel always shows the real Steam identity.
    /// </summary>
    private void UpdateAccountUi()
    {
        var settings = App.Settings;
        var signedIn = !string.IsNullOrEmpty(settings.SteamId);
        var steamName = signedIn ? (string.IsNullOrWhiteSpace(settings.PersonaName) ? "Steam user" : settings.PersonaName!) : null;
        var customName = string.IsNullOrWhiteSpace(settings.CustomName) ? null : settings.CustomName.Trim();

        var name = customName ?? steamName ?? "Guest";
        var picture = _customAvatar ?? _avatar;
        var hasProfile = signedIn || customName is not null || _customAvatar is not null;

        // Top bar, home tile and the Profile panel
        PersonaText.Text = name;
        SteamBadge.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SubText.Text = Loc.F("{0} games", _items.Count)
                       + (signedIn ? string.Empty : "  ·  " + Loc.T("not signed in"))
                       + (_nowPlaying is null ? string.Empty : "  ·  " + Loc.F("Playing {0}", _nowPlaying));
        SetAvatar(picture, name, AvatarBrush, AvatarInitial);
        SetAvatar(picture, name, ProfileAvatarBrush, ProfileAvatarInitial);

        UpdateFriendsTile();

        ProfileNamePreview.Text = name;
        ProfileSubText.Text = Loc.T(signedIn ? "Signed in with Steam" : "Not signed in to Steam");
        RemovePictureButton.Visibility = _customAvatar is null ? Visibility.Collapsed : Visibility.Visible;

        // Steam account panel: the real Steam identity
        SetAvatar(_avatar, steamName ?? "Guest", PanelAvatarBrush, PanelAvatarInitial);
        AccountName.Text = steamName ?? Loc.T("Not signed in");
        AccountId.Text = signedIn ? Loc.F("Steam ID {0}", settings.SteamId) : string.Empty;
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        DetectButton.Visibility = _steamPath is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetAvatar(ImageSource? image, string name, ImageBrush brush, TextBlock initial)
    {
        brush.ImageSource = image;
        initial.Text = name.Length > 0 ? StringInfo.GetNextTextElement(name).ToUpperInvariant() : "?";
        initial.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Profile (custom name and picture) ----------------------------------------------------

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        NameHint.Visibility = NameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_loadingUi) return;

        var text = NameBox.Text.Trim();
        App.Settings.CustomName = text.Length == 0 ? null : text;
        UpdateAccountUi(); // the top bar follows as you type
        QueueSave();
    }

    private async void OnChoosePictureClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose a profile picture"),
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        var destination = Path.Combine(SettingsStore.DefaultDirectory, $"avatar_custom_{DateTime.UtcNow.Ticks}.png");
        var saved = await Task.Run(() => ProfilePicture.TrySave(dialog.FileName, destination));
        if (!saved)
        {
            ShowToast("That file could not be used as a picture.");
            return;
        }

        DeleteQuietly(App.Settings.CustomAvatarPath); // replace, don't pile up old copies
        App.Settings.CustomAvatarPath = destination;
        SaveSettingsNow();

        _customAvatar = await ImageLoader.LoadAsync(destination, decodeWidth: 256);
        UpdateAccountUi();
        ShowToast("Picture updated");
    }

    /// <summary>Fills the "Ready-made avatars" row. Uses the Swatch style so focus/controller navigation match the theme colours.</summary>
    private void BuildPresetAvatars()
    {
        PresetAvatarPanel.Children.Clear();
        var style = (Style)FindResource("Swatch");

        foreach (var preset in PresetAvatars.All)
        {
            var button = new Button
            {
                Style = style,
                Width = 64,
                Height = 64,
                Background = new ImageBrush(PresetAvatars.Render(preset, 96)) { Stretch = Stretch.UniformToFill },
                ToolTip = preset.Name,
                Tag = preset.Id,
                Margin = new Thickness(0, 0, 10, 10),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{preset.Name} avatar");
            button.Click += async (_, _) => await ApplyPresetAvatarAsync(preset);
            PresetAvatarPanel.Children.Add(button);
        }
    }

    private async Task ApplyPresetAvatarAsync(PresetAvatars.Preset preset)
    {
        var destination = Path.Combine(SettingsStore.DefaultDirectory, $"avatar_custom_{DateTime.UtcNow.Ticks}.png");
        if (!PresetAvatars.TrySave(preset, destination)) // rendering needs the UI thread, the file is tiny
        {
            ShowToast("The avatar could not be saved.");
            return;
        }

        DeleteQuietly(App.Settings.CustomAvatarPath);
        App.Settings.CustomAvatarPath = destination;
        SaveSettingsNow();

        _customAvatar = await ImageLoader.LoadAsync(destination, decodeWidth: 256);
        UpdateAccountUi();
        ShowToast(Loc.F("Avatar set: {0}", preset.Name));
    }

    private void OnRemovePictureClick(object sender, RoutedEventArgs e)
    {
        DeleteQuietly(App.Settings.CustomAvatarPath);
        App.Settings.CustomAvatarPath = null;
        _customAvatar = null;
        SaveSettingsNow();

        UpdateAccountUi();
        Keyboard.Focus(ChoosePictureButton); // the focused button just disappeared
        ShowToast("Custom picture removed");
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leftover file is harmless */ }
    }

    // ---- Steam sign-in ---------------------------------------------------------------------------

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (_signInCts is not null)
        {
            _signInCts.Cancel(); // pressing the button again while waiting cancels
            return;
        }

        var cts = _signInCts = new CancellationTokenSource();
        SignInLabel.Text = Loc.T("Cancel sign-in");
        AccountStatus.Text = Loc.T("The Steam sign-in page opened in your browser. Finish signing in there…");

        try
        {
            var steamId = await SteamOpenId.SignInAsync(
                Http,
                url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
                TimeSpan.FromMinutes(3),
                cts.Token);

            if (steamId is null)
            {
                AccountStatus.Text = Loc.T("Sign-in was cancelled or could not be verified.");
            }
            else
            {
                await ApplyAccountAsync(steamId, fallbackName: null);
                Activate(); // bring the launcher back in front of the browser
            }
        }
        catch (Exception ex) when (ex is Win32Exception or HttpListenerException or InvalidOperationException)
        {
            AccountStatus.Text = Loc.F("Sign-in failed: {0}", ex.Message);
        }
        finally
        {
            cts.Dispose();
            _signInCts = null;
            SignInLabel.Text = Loc.T("Sign in with Steam");
        }
    }

    private async void OnDetectClick(object sender, RoutedEventArgs e)
    {
        var account = _steamPath is null ? null : SteamLoginUsers.ReadMostRecent(_steamPath);
        if (account is null)
        {
            AccountStatus.Text = Loc.T("No signed-in Steam account was found on this PC.");
            return;
        }

        await ApplyAccountAsync(account.SteamId64, account.PersonaName);
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        App.Settings.SteamId = null;
        App.Settings.PersonaName = null;
        App.Settings.AvatarUrl = null;
        _avatar = null;
        SaveSettingsNow();

        UpdateAccountUi();
        RebuildOwned();
        AccountStatus.Text = Loc.T("Signed out.");
        Keyboard.Focus(SignInButton);
    }

    private async Task ApplyAccountAsync(string steamId, string? fallbackName)
    {
        AccountStatus.Text = Loc.T("Loading your Steam profile…");

        var profile = await SteamProfile.FetchAsync(Http, steamId);

        App.Settings.SteamId = steamId;
        App.Settings.PersonaName = profile?.PersonaName ?? fallbackName ?? "Steam user";
        App.Settings.AvatarUrl = profile?.AvatarUrl;
        SaveSettingsNow();

        _avatar = await LoadAvatarAsync(steamId, profile?.AvatarUrl);
        UpdateAccountUi();
        _ownedTriedAt = DateTime.MinValue;
        _ = FetchOwnedIfNeededAsync(force: true);

        AccountStatus.Text = profile is null
            ? Loc.T("Signed in. Your public profile could not be loaded (private profile or offline), so the picture may be missing.")
            : Loc.T("Signed in.");
    }

    private async Task LoadSavedAvatarAsync()
    {
        var settings = App.Settings;

        if (!string.IsNullOrEmpty(settings.CustomAvatarPath))
            _customAvatar = await ImageLoader.LoadAsync(settings.CustomAvatarPath, decodeWidth: 256);

        if (!string.IsNullOrEmpty(settings.SteamId))
            _avatar = await LoadAvatarAsync(settings.SteamId, settings.AvatarUrl);

        UpdateAccountUi();
    }

    /// <summary>Uses the cached avatar when it is under a week old, otherwise downloads it again.</summary>
    private static async Task<ImageSource?> LoadAvatarAsync(string steamId, string? url)
    {
        var file = Path.Combine(SettingsStore.DefaultDirectory, $"avatar_{steamId}.jpg");

        try
        {
            var stale = !File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(7);
            if (stale && url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await Http.GetByteArrayAsync(url);
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                await File.WriteAllBytesAsync(file, bytes);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // Offline: fall through and use whatever is cached.
        }

        return await ImageLoader.LoadAsync(file, decodeWidth: 128);
    }

    // ---- General -------------------------------------------------------------------------

    private async void OnRescanClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        ShowToast(Loc.F("Library rescanned: {0} games", _items.Count));
    }
}
