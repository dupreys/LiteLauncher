using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiteLauncher.Core;

namespace LiteLauncher.App;

/// <summary>View model for one tile. The cover loads lazily, off the UI thread.</summary>
public sealed class GameItem : INotifyPropertyChanged
{
    private ImageSource? _cover;

    private bool _isFavorite;

    public GameItem(Game game)
    {
        Game = game;
        _isFavorite = App.Settings.Favorites.Contains(GameSorter.Key(game));
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    public Game Game { get; }

    public string Name => Game.Name;

    /// <summary>Width the cover is decoded at (smaller for the long list of games that are not installed).</summary>
    public int CoverDecodeWidth { get; init; } = 400;

    public bool NotInstalled => Game.NotInstalled;

    /// <summary>Start menu apps are shown as square icon tiles, without a caption.</summary>
    public bool IsAppTile => Game.Source == "Apps";

    public Visibility CaptionVisibility => IsAppTile ? Visibility.Collapsed : Visibility.Visible;

    public double TileOpacity => Game.NotInstalled ? 0.7 : 1.0;

    public Visibility InstallBadgeVisibility => Game.NotInstalled ? Visibility.Visible : Visibility.Collapsed;

    public long SizeOnDisk => Game.SizeOnDisk;

    public string Initial => Game.Name.Length > 0 ? Game.Name[..1].ToUpperInvariant() : "?";

    public ImageSource? Cover
    {
        get => _cover;
        private set { _cover = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover))); }
    }

    /// <summary>Sets the picture directly (used for app icons, which Windows draws for us).</summary>
    public void SetCover(ImageSource cover)
    {
        _coverRequested = true;
        Cover = cover;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _coverRequested;

    /// <summary>Starts decoding the cover in the background the first time it is called.</summary>
    public void EnsureCoverLoaded()
    {
        if (_coverRequested || Game.CoverPath is null) return;
        _coverRequested = true;
        _ = LoadCoverAsync(Game.CoverPath);
    }

    private async Task LoadCoverAsync(string path) => Cover = await ImageLoader.LoadAsync(path, decodeWidth: CoverDecodeWidth);
}

internal static class ImageLoader
{
    /// <summary>Decodes an image on a pool thread at a reduced size and freezes it for cross-thread use.</summary>
    public static Task<ImageSource?> LoadAsync(string path, int decodeWidth) => Task.Run<ImageSource?>(() =>
    {
        try
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = decodeWidth; // decode small: keeps RAM low for big libraries
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // we cache ourselves; this avoids stale pictures
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null; // corrupt or half-written image: show the placeholder instead
        }
    });
}
