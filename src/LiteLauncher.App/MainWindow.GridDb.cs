using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Manage → SteamGridDB: browse community covers, backgrounds and logos and apply one with A/click.
public partial class MainWindow
{
    private readonly Dictionary<string, int?> _gridGameIds = new();
    private CancellationTokenSource? _gridCts;
    private ArtKind _gridKind = ArtKind.Cover;

    private bool GridDbOpen => GridDbPanel.Visibility == Visibility.Visible;

    private void OnGridDbClick(object sender, RoutedEventArgs e)
    {
        if (App.Settings.SteamGridDbApiKey is null)
        {
            ShowToast("Add your SteamGridDB API key in Settings → Integrations first.");
            return;
        }
        GridDbPanel.Visibility = Visibility.Visible;
        _ = LoadGridAsync(ArtKind.Cover);
    }

    private void OnGridTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        if (tag == "Screenshots") _ = LoadScreenshotsAsync();
        else if (tag == "Wallhaven") _ = LoadWallhavenAsync();
        else if (Enum.TryParse<ArtKind>(tag, out var kind)) _ = LoadGridAsync(kind);
    }

    private void OnGridCloseClick(object sender, RoutedEventArgs e) => CloseGridDb();

    private void CloseGridDb()
    {
        _gridCts?.Cancel();
        GridDbPanel.Visibility = Visibility.Collapsed;
        GridResults.Children.Clear();
        Keyboard_FocusManage();
    }

    private void Keyboard_FocusManage() => System.Windows.Input.Keyboard.Focus(DetailsManageButton);

    private Button TabFor(ArtKind kind) => kind switch
    {
        ArtKind.Cover => GridTabCover,
        ArtKind.Background => GridTabBackground,
        _ => GridTabLogo,
    };

    private async Task LoadGridAsync(ArtKind kind)
    {
        if (DetailsItem() is not { } item || App.Settings.SteamGridDbApiKey is not { } apiKey) return;

        _gridCts?.Cancel();
        var cts = _gridCts = new CancellationTokenSource();
        _gridKind = kind;
        foreach (var k in Enum.GetValues<ArtKind>()) TabFor(k).FontWeight = k == kind ? FontWeights.Bold : FontWeights.Normal;
        GridTabShots.FontWeight = FontWeights.Normal;
        GridTabWall.FontWeight = FontWeights.Normal;
        GridResults.Children.Clear();
        GridStatus.Text = Loc.T("Searching SteamGridDB…");
        System.Windows.Input.Keyboard.Focus(TabFor(kind));

        try
        {
            var key = GameSorter.Key(item.Game);
            if (!_gridGameIds.TryGetValue(key, out var gameId))
                _gridGameIds[key] = gameId = await SteamGridDb.FindGameIdAsync(Http, apiKey, item.Game, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (gameId is null)
            {
                GridStatus.Text = Loc.T("This game was not found on SteamGridDB.");
                return;
            }

            var images = await SteamGridDb.GetImagesAsync(Http, apiKey, gameId.Value, kind, cts.Token);
            if (cts.IsCancellationRequested) return;
            GridStatus.Text = images.Count == 0
                ? Loc.F("No {0} on SteamGridDB for this game yet.", Loc.T(KindName(kind)))
                : Loc.F("{0} {1} · pick one to use it", images.Count, Loc.T(KindName(kind)));

            foreach (var image in images.Take(60)) GridResults.Children.Add(MakeArtButton(image, kind));
        }
        catch (UnauthorizedAccessException)
        {
            GridStatus.Text = Loc.T("SteamGridDB rejected the API key. Check it in Settings → Integrations.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (!cts.IsCancellationRequested) GridStatus.Text = Loc.T("Could not reach SteamGridDB. Check your internet connection.");
        }
    }

    /// <summary>Steam store screenshots (1920x1080): wallpapers that fill the whole screen. Applied as the game's background.</summary>
    private async Task LoadScreenshotsAsync()
    {
        if (DetailsItem() is not { } item) return;

        _gridCts?.Cancel();
        var cts = _gridCts = new CancellationTokenSource();
        _gridKind = ArtKind.Background;
        foreach (var k in Enum.GetValues<ArtKind>()) TabFor(k).FontWeight = FontWeights.Normal;
        GridTabShots.FontWeight = FontWeights.Bold;
        GridTabWall.FontWeight = FontWeights.Normal;
        GridResults.Children.Clear();
        System.Windows.Input.Keyboard.Focus(GridTabShots);

        if (item.Game.Source != SteamProvider.ProviderName)
        {
            GridStatus.Text = Loc.T("Screenshots are only available for Steam games.");
            return;
        }

        GridStatus.Text = Loc.T("Loading…");
        var shots = await SteamScreenshots.FetchAsync(Http, item.Game.Id, cts.Token);
        if (cts.IsCancellationRequested) return;

        GridStatus.Text = shots.Count == 0
            ? Loc.T("No screenshots found for this game on Steam.")
            : Loc.F("{0} screenshots (16:9, fill the whole screen) · pick one to use it as the background", shots.Count);

        foreach (var shot in shots.Take(40)) GridResults.Children.Add(MakeArtButton(shot, ArtKind.Background, wide: true));
    }

    /// <summary>16:9 wallpapers from wallhaven.cc (no key), searched by the game's name. Applied as the game's background.</summary>
    private async Task LoadWallhavenAsync()
    {
        if (DetailsItem() is not { } item) return;

        _gridCts?.Cancel();
        var cts = _gridCts = new CancellationTokenSource();
        _gridKind = ArtKind.Background;
        foreach (var k in Enum.GetValues<ArtKind>()) TabFor(k).FontWeight = FontWeights.Normal;
        GridTabShots.FontWeight = FontWeights.Normal;
        GridTabWall.FontWeight = FontWeights.Bold;
        GridResults.Children.Clear();
        GridStatus.Text = Loc.T("Searching Wallhaven…");
        System.Windows.Input.Keyboard.Focus(GridTabWall);

        try
        {
            var images = await Wallhaven.SearchAsync(Http, item.Game.OriginalName ?? item.Game.Name, cts.Token);
            if (cts.IsCancellationRequested) return;
            GridStatus.Text = images.Count == 0
                ? Loc.T("No 16:9 wallpapers found on Wallhaven for this game.")
                : Loc.F("{0} wallpapers from Wallhaven (16:9) · pick one to use it as the background", images.Count);

            foreach (var image in images) GridResults.Children.Add(MakeArtButton(image, ArtKind.Background, wide: true));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (!cts.IsCancellationRequested) GridStatus.Text = Loc.T("Could not reach Wallhaven. Check your internet connection.");
        }
    }

    private static string KindName(ArtKind kind) => kind switch
    {
        ArtKind.Cover => "covers",
        ArtKind.Background => "backgrounds",
        _ => "logos",
    };

    private Button MakeArtButton(ArtImage art, ArtKind kind, bool wide = false)
    {
        var (w, h) = kind switch
        {
            ArtKind.Cover => (116.0, 174.0),
            ArtKind.Background => wide ? (240.0, 135.0) : (300.0, 98.0),
            _ => (220.0, 98.0),
        };
        var image = new Image { Stretch = kind == ArtKind.Logo ? Stretch.Uniform : Stretch.UniformToFill, Margin = new Thickness(kind == ArtKind.Logo ? 10 : 0) };
        var button = new Button
        {
            Style = (Style)FindResource("Tile"),
            Width = w,
            Height = h,
            Margin = new Thickness(0, 0, 12, 12),
            Content = image,
            ToolTip = art.Width > 0 ? $"{art.Width} × {art.Height}" : null,
        };
        button.Click += async (_, _) => await ApplyGridArtAsync(art, kind);
        _ = LoadThumbAsync(image, art.ThumbUrl);
        return button;
    }

    private static async Task LoadThumbAsync(Image target, string url) =>
        target.Source = await RemoteImage.LoadAsync(Http, url, decodeWidth: 320);

    private async Task ApplyGridArtAsync(ArtImage art, ArtKind kind)
    {
        if (_detailsKey is null) return;
        GridStatus.Text = Loc.T("Downloading…");

        var extension = Path.GetExtension(new Uri(art.Url).AbsolutePath);
        if (extension.Length is 0 or > 5) extension = ".png";
        var safeKey = string.Concat(_detailsKey.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var folder = Path.Combine(SettingsStore.DefaultDirectory, "games");
        var name = kind.ToString().ToLowerInvariant();
        var destination = Path.Combine(folder, $"{safeKey}_{name}_{DateTime.UtcNow.Ticks}{extension}");

        try
        {
            var bytes = await Http.GetByteArrayAsync(art.Url);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(destination, bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            GridStatus.Text = Loc.T("The download failed. Try another picture.");
            return;
        }

        if (await ImageLoader.LoadAsync(destination, decodeWidth: 64) is null)
        {
            DeleteQuietly(destination);
            GridStatus.Text = Loc.T("That picture format is not supported here. Try another one.");
            return;
        }

        string? old = null;
        if (App.Settings.GameOverrides.TryGetValue(_detailsKey, out var current))
            old = kind switch { ArtKind.Cover => current.CoverPath, ArtKind.Background => current.BackdropPath, _ => current.LogoPath };

        await UpdateOverrideAsync(o =>
        {
            switch (kind)
            {
                case ArtKind.Cover: o.CoverPath = destination; break;
                case ArtKind.Background: o.BackdropPath = destination; break;
                default: o.LogoPath = destination; break;
            }
        });
        DeleteQuietly(old);
        GridStatus.Text = kind switch
        {
            ArtKind.Cover => Loc.T("Cover applied. Pick another, or press B to go back."),
            ArtKind.Background => Loc.T("Background applied. Pick another, or press B to go back."),
            _ => Loc.T("Logo applied. Pick another, or press B to go back."),
        };
        ShowToast(kind switch
        {
            ArtKind.Cover => "Cover updated from SteamGridDB",
            ArtKind.Background => "Background updated from SteamGridDB",
            _ => "Logo updated from SteamGridDB",
        });
    }
}
