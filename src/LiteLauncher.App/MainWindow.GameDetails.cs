using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Game menu: Start (controller), the Menu key or right-click on a game opens it.
public partial class MainWindow
{
    private string? _detailsKey;          // "Source:Id" of the game shown
    private IInputElement? _detailsReturnFocus;
    private CancellationTokenSource? _detailsCts;

    private bool DetailsOpen => GameDetails.Visibility == Visibility.Visible;

    private static GameItem? GameFromElement(object? element) =>
        element is ContentControl { Content: GameItem item } ? item : null;

    private GameItem? DetailsItem() =>
        _detailsKey is null ? null : _items.FirstOrDefault(i => GameSorter.Key(i.Game) == _detailsKey);

    private void OpenGameDetails(GameItem item)
    {
        VirtualKeyboard.Hide();
        _detailsReturnFocus = Keyboard.FocusedElement;
        _detailsKey = GameSorter.Key(item.Game);

        ManagePanel.Visibility = Visibility.Collapsed;
        GridDbPanel.Visibility = Visibility.Collapsed;
        AchievementsPanel.Visibility = Visibility.Collapsed;
        AchievementsList.Children.Clear();
        DetailsLogo.Visibility = Visibility.Collapsed;
        DetailsTitle.Visibility = Visibility.Visible;
        DetailsManageLabel.Text = "Manage   ▾";
        FillDetails(item);
        GameDetails.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(DetailsPlayButton);
        _ = LoadStoreInfoAsync(item.Game);
    }

    /// <summary>Everything we know locally; store info is added when it arrives.</summary>
    private void FillDetails(GameItem item)
    {
        var game = item.Game;
        item.EnsureCoverLoaded();
        DetailsSource.Text = game.Source.ToUpperInvariant();
        DetailsTitle.Text = item.Name;
        DetailsInitial.Text = item.Initial;
        DetailsCover.Source = item.Cover;
        DetailsSize.Text = GameInfo.FormatSize(game.SizeOnDisk);
        DetailsLastPlayed.Text = game.LastPlayed is { } played
            ? played.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            : "Never";
        _ = LoadDetailsImagesAsync(item);
    }

    private async Task LoadDetailsImagesAsync(GameItem item)
    {
        var key = GameSorter.Key(item.Game);
        var backdropPath = item.Game.BackdropPath ?? item.Game.CoverPath;
        var backdrop = backdropPath is null ? null : await ImageLoader.LoadAsync(backdropPath, decodeWidth: 1280);
        var cover = item.Game.CoverPath is null ? null : await ImageLoader.LoadAsync(item.Game.CoverPath, decodeWidth: 200);
        var logo = item.Game.LogoPath is null ? null : await ImageLoader.LoadAsync(item.Game.LogoPath, decodeWidth: 840);
        if (_detailsKey != key) return; // another game was opened meanwhile
        DetailsBackdrop.Source = backdrop;
        DetailsCover.Source = cover;
        DetailsLogo.Source = logo;
        DetailsLogo.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        DetailsTitle.Visibility = logo is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadStoreInfoAsync(Game game)
    {
        _detailsCts?.Cancel();
        var cts = _detailsCts = new CancellationTokenSource();
        var key = GameSorter.Key(game);

        DetailsMeta.Text = game.Source == SteamProvider.ProviderName ? "Loading store information…" : "";
        DetailsGenres.Text = "";
        DetailsDescription.Text = game.Source == SteamProvider.ProviderName
            ? ""
            : $"Installed from the {game.Source} store. Use \"More about the game\" to open its store page.";
        DetailsPlaytime.Text = "—";
        _ = LoadAchievementsAsync(game, key, cts.Token); // independent of the store info: shows up when it arrives

        var details = await GameInfo.FetchAsync(Http, game, _steamPath, App.Settings.SteamId, cts.Token);
        if (cts.IsCancellationRequested || _detailsKey != key) return;

        var meta = new List<string>();
        if (details.Developer is not null) meta.Add(details.Developer);
        if (details.ReleaseDate is not null) meta.Add(details.ReleaseDate);
        if (details.PlayersNow is { } players) meta.Add($"{players.ToString("N0", CultureInfo.CurrentCulture)} playing now");
        DetailsMeta.Text = meta.Count > 0 ? string.Join("   ·   ", meta)
            : game.Source == SteamProvider.ProviderName ? "Store information is not available right now." : "";
        DetailsGenres.Text = string.Join(" · ", details.Genres);
        if (details.Description is not null) DetailsDescription.Text = details.Description;
        var minutes = details.PlaytimeMinutes
                      ?? (App.Settings.PlayRecords.TryGetValue(key, out var record) && record.Minutes > 0 ? record.Minutes : null);
        DetailsPlaytime.Text = minutes is { } m ? GameInfo.FormatPlaytime(m) : "—";
    }

    private async Task LoadAchievementsAsync(Game game, string key, CancellationToken cancellationToken)
    {
        _achievementsResult = null;
        AchievementsBar.Visibility = Visibility.Collapsed;
        if (game.Source != SteamProvider.ProviderName)
        {
            _achievementsResult = new AchievementResult(AchievementStatus.NotSupported);
            DetailsAchievements.Text = Achievements.Describe(_achievementsResult);
            return;
        }

        DetailsAchievements.Text = "Loading…";
        var result = await Achievements.FetchAsync(Http, game, App.Settings.SteamId, App.Settings.SteamWebApiKey, cancellationToken);
        if (cancellationToken.IsCancellationRequested || _detailsKey != key) return;

        _achievementsResult = result;
        DetailsAchievements.Text = Achievements.Describe(result);
        if (result.Status != AchievementStatus.Ok) return;

        AchievementsFill.Width = new GridLength(result.Fraction, GridUnitType.Star);
        AchievementsRest.Width = new GridLength(1 - result.Fraction, GridUnitType.Star);
        AchievementsBar.Visibility = Visibility.Visible;
    }

    private void CloseGameDetails()
    {
        if (!DetailsOpen) return;
        _detailsCts?.Cancel();
        VirtualKeyboard.Hide();
        GameDetails.Visibility = Visibility.Collapsed;
        _detailsKey = null;
        DetailsBackdrop.Source = null;
        App.Sounds.Play(SoundKind.Back);
        if (_detailsReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    // ---- Buttons ----

    private void OnDetailsCloseClick(object sender, RoutedEventArgs e) => CloseGameDetails();

    private void OnDetailsDimClick(object sender, MouseButtonEventArgs e) => CloseGameDetails();

    private void OnDetailsPlayClick(object sender, RoutedEventArgs e)
    {
        if (DetailsItem() is not { } item) return;
        CloseGameDetails();
        LaunchGame(item);
    }

    private void OnDetailsStoreClick(object sender, RoutedEventArgs e)
    {
        if (DetailsItem() is not { } item) return;
        try
        {
            Process.Start(new ProcessStartInfo(GameInfo.StoreUrl(item.Game)) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // store client not installed: use the browser
            try { Process.Start(new ProcessStartInfo(GameInfo.StoreWebUrl(item.Game)) { UseShellExecute = true }); }
            catch (Win32Exception) { ShowToast("Could not open the store page."); }
        }
    }

    private void OnDetailsManageClick(object sender, RoutedEventArgs e)
    {
        var open = ManagePanel.Visibility != Visibility.Visible;
        ManagePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DetailsManageLabel.Text = open ? "Manage   ▴" : "Manage   ▾";
        if (open && DetailsItem() is { } item)
        {
            var original = item.Game.OriginalName ?? item.Game.Name;
            GameNameBox.Text = item.Name == original ? "" : item.Name;
            GameNameHint.Text = original;
            ResetGameButton.Visibility = App.Settings.GameOverrides.ContainsKey(_detailsKey!) ? Visibility.Visible : Visibility.Collapsed;
            Keyboard.Focus(GameNameBox);
        }
    }

    private void OnGameNameChanged(object sender, TextChangedEventArgs e) =>
        GameNameHint.Visibility = GameNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnSaveGameNameClick(object sender, RoutedEventArgs e)
    {
        if (DetailsItem() is not { } item) return;
        var original = item.Game.OriginalName ?? item.Game.Name;
        VirtualKeyboard.Hide();
        await UpdateOverrideAsync(o => o.Name = GameOverrides.NormalizeName(GameNameBox.Text, original));
        ShowToast("Name saved");
    }

    private Task OnChangeCoverClickAsync() => PickGameImageAsync("cover", (o, path) => o.CoverPath = path, "Cover updated");
    private Task OnChangeBackgroundClickAsync() => PickGameImageAsync("background", (o, path) => o.BackdropPath = path, "Background updated");
    private async void OnChangeCoverClick(object sender, RoutedEventArgs e) => await OnChangeCoverClickAsync();
    private async void OnChangeBackgroundClick(object sender, RoutedEventArgs e) => await OnChangeBackgroundClickAsync();

    private async Task PickGameImageAsync(string kind, Action<GameOverride, string> apply, string toast)
    {
        if (_detailsKey is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choose a {kind} image",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        // Keep a copy, so the picture survives if the original is moved.
        var safeKey = string.Concat(_detailsKey.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var folder = Path.Combine(SettingsStore.DefaultDirectory, "games");
        var destination = Path.Combine(folder, $"{safeKey}_{kind}_{DateTime.UtcNow.Ticks}{Path.GetExtension(dialog.FileName)}");
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Run(() => File.Copy(dialog.FileName, destination, overwrite: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("That picture could not be copied.");
            return;
        }

        if (await ImageLoader.LoadAsync(destination, decodeWidth: 64) is null)
        {
            DeleteQuietly(destination);
            ShowToast("That file could not be used as a picture.");
            return;
        }

        var old = App.Settings.GameOverrides.TryGetValue(_detailsKey, out var current)
            ? (kind == "cover" ? current.CoverPath : current.BackdropPath)
            : null;
        await UpdateOverrideAsync(o => apply(o, destination));
        DeleteQuietly(old);
        ShowToast(toast);
    }

    private async void OnResetGameClick(object sender, RoutedEventArgs e)
    {
        if (_detailsKey is null || !App.Settings.GameOverrides.Remove(_detailsKey, out var removed)) return;
        DeleteQuietly(removed.CoverPath);
        DeleteQuietly(removed.BackdropPath);
        DeleteQuietly(removed.LogoPath);
        SaveSettingsNow();
        GameNameBox.Text = "";
        ResetGameButton.Visibility = Visibility.Collapsed;
        await ReloadDetailsAsync();
        Keyboard.Focus(GameNameBox);
        ShowToast("Original name and pictures restored");
    }

    /// <summary>Edits this game's override, saves settings and refreshes the library and the open menu.</summary>
    private async Task UpdateOverrideAsync(Action<GameOverride> edit)
    {
        if (_detailsKey is null) return;
        var overrides = App.Settings.GameOverrides;
        if (!overrides.TryGetValue(_detailsKey, out var o)) overrides[_detailsKey] = o = new GameOverride();
        edit(o);
        if (o.IsEmpty) overrides.Remove(_detailsKey);
        SaveSettingsNow();
        ResetGameButton.Visibility = overrides.ContainsKey(_detailsKey) ? Visibility.Visible : Visibility.Collapsed;
        await ReloadDetailsAsync();
    }

    private async Task ReloadDetailsAsync()
    {
        await RefreshAsync();
        if (DetailsItem() is { } item) FillDetails(item);
    }

    // ---- Input ----

    /// <summary>Right-click on any game tile opens its menu.</summary>
    private void OnPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DetailsOpen) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null;
             node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
        {
            if (GameFromElement(node) is { } item)
            {
                OpenGameDetails(item);
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>Esc closes Manage first, then the menu. Returns true when the key was used.</summary>
    private bool HandleDetailsKey(Key key)
    {
        if (!DetailsOpen) return false;
        if (key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, GameNameBox))
        {
            OnSaveGameNameClick(GameNameBox, new RoutedEventArgs());
            return true;
        }
        if (key is Key.Escape or Key.Apps)
        {
            DetailsBack();
            return true;
        }
        return false;
    }

    private bool HandleDetailsGamepad(GamepadButton button)
    {
        if (!DetailsOpen) return false;
        switch (button)
        {
            case GamepadButton.B:
                DetailsBack();
                return true;
            case GamepadButton.Start:
                CloseGameDetails();
                return true;
            case GamepadButton.Y:
                return true; // no search inside the menu
            case GamepadButton.A when ReferenceEquals(Keyboard.FocusedElement, GameNameBox):
                if (VirtualKeyboard.IsVisible) VirtualKeyboard.Hide();
                else _ = VirtualKeyboard.ShowAsync();
                return true;
            default:
                return false; // D-pad and A work as usual; focus is kept inside the menu
        }
    }

    private void DetailsBack()
    {
        if (VirtualKeyboard.IsVisible)
        {
            VirtualKeyboard.Hide();
            return;
        }
        if (AchievementsOpen)
        {
            CloseAchievements();
            return;
        }
        if (GridDbOpen)
        {
            CloseGridDb();
            return;
        }
        if (ManagePanel.Visibility == Visibility.Visible)
        {
            OnDetailsManageClick(DetailsManageButton, new RoutedEventArgs());
            Keyboard.Focus(DetailsManageButton);
            return;
        }
        CloseGameDetails();
    }
}
