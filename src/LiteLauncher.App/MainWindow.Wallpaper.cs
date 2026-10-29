using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;

namespace LiteLauncher.App;

// Settings > Theme > Wallpaper: a fixed background (ready-made or the user's own picture) instead of the game art.
public partial class MainWindow
{
    private const string CustomWallpaperPrefix = "file:";

    private bool WallpaperActive => App.Settings.Wallpaper.Length > 0;

    private void BuildWallpaperChoices()
    {
        WallpaperPanel.Children.Clear();
        AddWallpaperChoice("", Loc.T("Game art"), null);
        foreach (var preset in Wallpapers.Presets)
            AddWallpaperChoice("preset:" + preset.Id, Loc.T(preset.Name), Wallpapers.Render(preset, 208, 117));
        if (App.Settings.Wallpaper.StartsWith(CustomWallpaperPrefix, StringComparison.Ordinal))
            AddWallpaperChoice(App.Settings.Wallpaper, Loc.T("Your image"), null);
        MarkWallpaperChoice();
    }

    private void AddWallpaperChoice(string value, string label, ImageSource? thumbnail)
    {
        var content = new Grid { Width = 104, Height = 58 };
        if (thumbnail is not null) content.Children.Add(new System.Windows.Controls.Image { Source = thumbnail, Stretch = Stretch.UniformToFill });
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 1 },
        };
        if (thumbnail is null) // "Game art": a plain tile with the label centred
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0);
            text.Effect = null;
        }
        content.Children.Add(text);
        content.Children.Add(new TextBlock
        {
            Name = "Mark",
            Text = "✓",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 6, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 1 },
            Visibility = Visibility.Collapsed,
        });

        var button = new Button
        {
            Style = (Style)FindResource("Tile"),
            Content = content,
            Tag = value,
            Margin = new Thickness(0, 0, 12, 12),
        };
        button.Click += OnWallpaperChoiceClick;
        WallpaperPanel.Children.Add(button);
    }

    private void MarkWallpaperChoice()
    {
        var current = App.Settings.Wallpaper;
        foreach (var button in WallpaperPanel.Children.OfType<Button>())
        {
            var selected = (string?)button.Tag == current;
            if (button.Content is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "Mark") is { } mark)
                mark.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnWallpaperChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value }) return;
        SetWallpaper(value);
    }

    private void SetWallpaper(string value)
    {
        var old = App.Settings.Wallpaper;
        if (old == value) return;

        App.Settings.Wallpaper = value;
        if (old.StartsWith(CustomWallpaperPrefix, StringComparison.Ordinal)) DeleteQuietly(old[CustomWallpaperPrefix.Length..]);

        if (old.StartsWith(CustomWallpaperPrefix, StringComparison.Ordinal) || value.StartsWith(CustomWallpaperPrefix, StringComparison.Ordinal))
        {
            BuildWallpaperChoices(); // the "Your image" tile appears, changes or goes away
            WallpaperPanel.UpdateLayout();
            if (WallpaperPanel.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == value) is { } again)
                Keyboard.Focus(again);
        }
        else
        {
            MarkWallpaperChoice();
        }
        _currentBackdropKey = null; // force the background to be set again
        ApplyBackdropSettings();
        QueueSave();
        App.Sounds.Play(SoundKind.Select);
    }

    private async void OnWallpaperFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose a wallpaper image"),
            Filter = Loc.T("Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
        };
        if (dialog.ShowDialog(this) != true) return;

        var folder = SettingsStore.DefaultDirectory;
        var destination = Path.Combine(folder, $"wallpaper_{DateTime.UtcNow.Ticks}{Path.GetExtension(dialog.FileName)}");
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

        SetWallpaper(CustomWallpaperPrefix + destination);
        ShowToast("Wallpaper updated");
    }

    private ImageSource? _presetWallpaper;
    private string? _presetWallpaperId;

    /// <summary>Applies the chosen wallpaper as the background. Returns false when there is none.</summary>
    private async Task<bool> ShowWallpaperAsync()
    {
        var value = App.Settings.Wallpaper;
        if (value.Length == 0) return false;

        var key = "wall:" + value;
        if (key == _currentBackdropKey) return true;
        _currentBackdropKey = key;
        var token = ++_backdropToken;

        ImageSource? image = null;
        if (Wallpapers.Find(value) is { } preset)
        {
            if (_presetWallpaperId != preset.Id || _presetWallpaper is null)
            {
                _presetWallpaper = Wallpapers.Render(preset, 1280, 720);
                _presetWallpaperId = preset.Id;
            }
            image = _presetWallpaper;
        }
        else if (value.StartsWith(CustomWallpaperPrefix, StringComparison.Ordinal))
        {
            var path = value[CustomWallpaperPrefix.Length..];
            if (!_backdropCache.TryGetValue(path, out image)) image = await LoadBackdropAsync(path);
        }

        if (token != _backdropToken) return true; // changed again while loading
        FadeBackdrop(image);
        return true;
    }
}
