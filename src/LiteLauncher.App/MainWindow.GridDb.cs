using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;

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
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ArtKind>(tag, out var kind))
            _ = LoadGridAsync(kind);
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
        GridResults.Children.Clear();
        GridStatus.Text = "Searching SteamGridDB…";
        System.Windows.Input.Keyboard.Focus(TabFor(kind));

        try
        {
            var key = GameSorter.Key(item.Game);
            if (!_gridGameIds.TryGetValue(key, out var gameId))
                _gridGameIds[key] = gameId = await SteamGridDb.FindGameIdAsync(Http, apiKey, item.Game, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (gameId is null)
            {
                GridStatus.Text = "This game was not found on SteamGridDB.";
                return;
            }

            var images = await SteamGridDb.GetImagesAsync(Http, apiKey, gameId.Value, kind, cts.Token);
            if (cts.IsCancellationRequested) return;
            GridStatus.Text = images.Count == 0
                ? $"No {KindName(kind)} on SteamGridDB for this game yet."
                : $"{images.Count} {KindName(kind)} · pick one to use it";

            foreach (var image in images.Take(60)) GridResults.Children.Add(MakeArtButton(image, kind));
        }
        catch (UnauthorizedAccessException)
        {
            GridStatus.Text = "SteamGridDB rejected the API key. Check it in Settings → Integrations.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (!cts.IsCancellationRequested) GridStatus.Text = "Could not reach SteamGridDB. Check your internet connection.";
        }
    }

    private static string KindName(ArtKind kind) => kind switch
    {
        ArtKind.Cover => "covers",
        ArtKind.Background => "backgrounds",
        _ => "logos",
    };

    private Button MakeArtButton(ArtImage art, ArtKind kind)
    {
        var (w, h) = kind switch
        {
            ArtKind.Cover => (116.0, 174.0),
            ArtKind.Background => (300.0, 98.0),
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
        GridStatus.Text = "Downloading…";

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
            GridStatus.Text = "The download failed. Try another picture.";
            return;
        }

        if (await ImageLoader.LoadAsync(destination, decodeWidth: 64) is null)
        {
            DeleteQuietly(destination);
            GridStatus.Text = "That picture format is not supported here. Try another one.";
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
        GridStatus.Text = $"{kind} applied. Pick another, or press B to go back.";
        ShowToast($"{kind} updated from SteamGridDB");
    }
}
