using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiteLauncher.Core;
using LiteLauncher.Core.Manual;

namespace LiteLauncher.App;

// Adding a game by hand: pick its .exe in a Windows file dialog, then name it in a panel that looks like the game menu.
public partial class MainWindow
{
    private string? _addGamePath;
    private IInputElement? _addGameReturnFocus;

    private bool AddGameOpen => AddGamePanel.Visibility == Visibility.Visible;

    private void OnAddGameClick(object sender, RoutedEventArgs e)
    {
        _addGameReturnFocus = Keyboard.FocusedElement;
        ChooseGameExe();
    }

    private void OnAddGameChooseAnotherClick(object sender, RoutedEventArgs e) => ChooseGameExe();

    /// <summary>Opens the Windows file dialog (folders of the PC) and shows the naming panel for the chosen .exe.</summary>
    private void ChooseGameExe()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose the game's .exe"),
            Filter = Loc.T("Programs") + "|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            if (!AddGameOpen) RestoreAddGameFocus();
            return;
        }

        if (!ManualProvider.IsValidExecutable(dialog.FileName))
        {
            ShowToast("That file is not a program (.exe).");
            if (!AddGameOpen) RestoreAddGameFocus();
            return;
        }

        var wasOpen = AddGameOpen;
        var previousSuggestion = _addGamePath is null ? null : ManualProvider.SuggestName(_addGamePath);
        _addGamePath = Path.GetFullPath(dialog.FileName);
        AddGamePath.Text = _addGamePath;
        AddGameError.Visibility = Visibility.Collapsed;

        // Keep what the user already typed when they only swapped the file.
        var typed = AddGameNameBox.Text.Trim();
        if (!wasOpen || typed.Length == 0 || typed == previousSuggestion)
            AddGameNameBox.Text = ManualProvider.SuggestName(_addGamePath);
        UpdateAddGamePreview();

        AddGamePanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        AddGameNameBox.Focus();
        Keyboard.Focus(AddGameNameBox);
        AddGameNameBox.SelectAll();
    }

    private void UpdateAddGamePreview()
    {
        var name = AddGameNameBox.Text.Trim();
        AddGameNameHint.Visibility = AddGameNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddGameTitle.Text = name.Length > 0 ? name : Loc.T("New game");
        AddGameInitial.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
    }

    private void OnAddGameNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateAddGamePreview();
        AddGameError.Visibility = Visibility.Collapsed;
    }

    private async void OnAddGameConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_addGamePath is null) return;
        var game = ManualProvider.Add(App.Settings.ManualGames, _addGamePath, AddGameNameBox.Text);
        if (game is null)
        {
            AddGameError.Text = Loc.T(File.Exists(_addGamePath)
                ? "This game is already in your library."
                : "That file no longer exists.");
            AddGameError.Visibility = Visibility.Visible;
            return;
        }

        SaveSettingsNow();
        CloseAddGame(playSound: false);
        App.Sounds.Play(SoundKind.Select);
        await RefreshAsync();
        ShowToast("Game added");
        RestoreAddGameFocus();
    }

    private void OnAddGameCancelClick(object sender, RoutedEventArgs e) => CloseAddGame();

    private void OnAddGameDimClick(object sender, MouseButtonEventArgs e) => CloseAddGame();

    private void CloseAddGame(bool playSound = true)
    {
        if (!AddGameOpen) return;
        VirtualKeyboard.Hide();
        AddGamePanel.Visibility = Visibility.Collapsed;
        _addGamePath = null;
        AddGameNameBox.Text = "";
        if (playSound)
        {
            App.Sounds.Play(SoundKind.Back);
            RestoreAddGameFocus();
        }
    }

    private void RestoreAddGameFocus()
    {
        if (_addGameReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private bool HandleAddGameKey(Key key)
    {
        if (!AddGameOpen) return false;
        if (key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, AddGameNameBox))
        {
            OnAddGameConfirmClick(AddGameNameBox, new RoutedEventArgs());
            return true;
        }
        if (key == Key.Escape)
        {
            if (VirtualKeyboard.IsVisible) VirtualKeyboard.Hide();
            else CloseAddGame();
            return true;
        }
        return false;
    }

    private bool HandleAddGameGamepad(GamepadButton button)
    {
        if (!AddGameOpen) return false;
        switch (button)
        {
            case GamepadButton.B:
                if (VirtualKeyboard.IsVisible) VirtualKeyboard.Hide();
                else CloseAddGame();
                return true;
            case GamepadButton.Start:
            case GamepadButton.Y:
                return true; // nothing to do here
            case GamepadButton.A when ReferenceEquals(Keyboard.FocusedElement, AddGameNameBox):
                if (VirtualKeyboard.IsVisible) VirtualKeyboard.Hide();
                else _ = VirtualKeyboard.ShowAsync();
                return true;
            default:
                return false; // D-pad and A work as usual; focus is kept inside the panel
        }
    }

    // ---- Removing a game that was added by hand (from its Manage section) ----

    private async void OnRemoveManualGameClick(object sender, RoutedEventArgs e)
    {
        if (DetailsItem() is not { } item || item.Game.Source != ManualProvider.ProviderName) return;
        var key = GameSorter.Key(item.Game);
        App.Settings.ManualGames.RemoveAll(g => g.Id == item.Game.Id);
        if (App.Settings.GameOverrides.Remove(key, out var removed))
        {
            DeleteQuietly(removed.CoverPath);
            DeleteQuietly(removed.BackdropPath);
            DeleteQuietly(removed.LogoPath);
        }
        App.Settings.PlayRecords.Remove(key);
        App.Settings.Favorites.Remove(key);
        SaveSettingsNow();
        CloseGameDetails();
        await RefreshAsync();
        ShowToast("Game removed from the launcher");
    }
}
