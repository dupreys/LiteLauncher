using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiteLauncher.Core;

namespace LiteLauncher.App;

// Home layouts (Settings > Theme > Home style):
//   Xbox   - big last-played tile, small recent tiles and four shortcut tiles (the original look)
//   Simple - only games: one row of covers with the selected game's name below, shortcuts as a text menu (like the Steam home)
//   Cinema - the selected game's name in large type, a compact row of covers below and the text menu
public partial class MainWindow
{
    private readonly record struct HomeLayout(
        double HeroLeft, double HeroTop, double HeroWidth, double HeroHeight, string HeroTemplate,
        double RecentLeft, double RecentTop, double RecentWidth, double TileHeight, string RecentTemplate,
        double TitleLeft, double TitleTop, double TitleSize, FontWeight TitleWeight,
        double SubTop, double SubSize, double NavLeft, double NavTop, double EmptyLeft, double EmptyTop);

    private static readonly HomeLayout XboxLayout =
        new(66, 170, 262, 262, "ArtNamed", 357, 293, 900, 134, "RecentTileXbox",
            0, 0, 0, FontWeights.Normal, 0, 0, 66, 636, 357, 330);

    // Simple: the last game played is a little bigger than the rest and the row scrolls through every installed game.
    private static readonly HomeLayout SimpleLayout =
        new(56, 262, 168, 252, "ArtOnly", 248, 283, 984, 210, "RecentTileTall",
            56, 534, 26, FontWeights.SemiBold, 570, 14, 56, 606, 56, 350);

    private static readonly HomeLayout CinemaLayout =
        new(66, 400, 150, 150, "ArtOnly", 240, 415, 992, 120, "RecentTileCinema",
            66, 200, 54, FontWeights.Light, 278, 18, 66, 606, 66, 450);

    /// <summary>
    /// Scrolls the row by hand so the selected tile (which grows a little when focused) always keeps a margin on both
    /// sides, instead of touching the edge of the scroll area and getting clipped.
    /// </summary>
    private void OnRecentBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is not FrameworkElement target || !RecentList.IsAncestorOf(target)) return;
        e.Handled = true;

        const double margin = 26;
        var left = target.TranslatePoint(new Point(0, 0), RecentList).X + RecentList.Margin.Left;
        var right = left + target.ActualWidth;
        var offset = RecentScroll.HorizontalOffset;
        var viewport = RecentScroll.ViewportWidth;

        if (left - margin < offset) offset = left - margin;
        else if (right + margin > offset + viewport) offset = right + margin - viewport;
        RecentScroll.ScrollToHorizontalOffset(Math.Max(0, offset));
    }

    private static readonly string[] HomeStyles = { "Xbox", "Simple", "Cinema" };

    /// <summary>Simple and Cinema list every installed game (scrolling sideways); Xbox shows the last few.</summary>
    private bool HomeShowsAllGames => App.Settings.HomeStyle != "Xbox";

    private void ApplyHomeStyle()
    {
        var style = App.Settings.HomeStyle;
        var xbox = style == "Xbox";
        var layout = style switch { "Simple" => SimpleLayout, "Cinema" => CinemaLayout, _ => XboxLayout };

        foreach (var tile in new UIElement[] { LibraryTile, SteamTile, SettingsTile, FriendsTile })
            tile.Visibility = xbox ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new UIElement[] { NavLibrary, NavLaunchers, NavSettings, NavFriends })
            nav.Visibility = xbox ? Visibility.Collapsed : Visibility.Visible;
        HomeGameTitle.Visibility = HomeGameSub.Visibility = xbox ? Visibility.Collapsed : Visibility.Visible;

        HeroTile.Width = layout.HeroWidth;
        HeroTile.Height = layout.HeroHeight;
        HeroTile.ContentTemplate = (DataTemplate)FindResource(layout.HeroTemplate);
        Canvas.SetLeft(HeroTile, layout.HeroLeft);
        Canvas.SetTop(HeroTile, layout.HeroTop);

        // The scroll area is a little taller than the tiles so the zoom of the selected one is not clipped.
        // ...and wider on both sides, for the same reason (the first and last tiles grow when selected).
        const double sidePad = 18;
        RecentScroll.Width = layout.RecentWidth + 2 * sidePad;
        RecentScroll.Height = layout.TileHeight + 24;
        RecentList.Margin = new Thickness(sidePad, 12, sidePad, 12);
        Canvas.SetLeft(RecentScroll, layout.RecentLeft - sidePad);
        Canvas.SetTop(RecentScroll, layout.RecentTop - 12);
        RecentList.ItemTemplate = (DataTemplate)FindResource(layout.RecentTemplate);
        RecentScroll.ScrollToHorizontalOffset(0);

        Canvas.SetLeft(EmptyText, layout.EmptyLeft);
        Canvas.SetTop(EmptyText, layout.EmptyTop);
        Canvas.SetLeft(HomeNav, layout.NavLeft);
        Canvas.SetTop(HomeNav, layout.NavTop);

        if (!xbox)
        {
            HomeGameTitle.FontSize = layout.TitleSize;
            HomeGameTitle.FontWeight = layout.TitleWeight;
            Canvas.SetLeft(HomeGameTitle, layout.TitleLeft);
            Canvas.SetTop(HomeGameTitle, layout.TitleTop);
            HomeGameSub.FontSize = layout.SubSize;
            Canvas.SetLeft(HomeGameSub, layout.TitleLeft);
            Canvas.SetTop(HomeGameSub, layout.SubTop);
        }

        UpdateHomeStyleLabel();
        UpdateHomeGameInfo(FocusedHomeGame() ?? HeroTile.Content as GameItem);
    }

    private GameItem? FocusedHomeGame() =>
        Keyboard.FocusedElement is ContentControl { Content: GameItem game } tile && HomeView.IsAncestorOf(tile) ? game : null;

    /// <summary>Name and details of the selected game, shown by the Simple and Cinema styles.</summary>
    private void UpdateHomeGameInfo(GameItem? item)
    {
        if (HomeGameTitle.Visibility != Visibility.Visible) return;

        HomeGameTitle.Text = item?.Name ?? "";
        if (item is null)
        {
            HomeGameSub.Text = "";
            return;
        }

        var text = item.Game.Source;
        if (item.Game.LastPlayed is { } played)
            text += " · " + Loc.F("Last played {0}", played.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture));
        HomeGameSub.Text = text;
    }

    private void UpdateHomeStyleLabel() =>
        HomeStyleLabel.Text = Loc.F("Home style: {0}", Loc.T(App.Settings.HomeStyle));

    private void OnHomeStyleClick(object sender, RoutedEventArgs e)
    {
        var next = HomeStyles[(Array.IndexOf(HomeStyles, App.Settings.HomeStyle) + 1) % HomeStyles.Length];
        App.Settings.HomeStyle = next;
        QueueSave();
        ApplyHomeStyle();
        if (HomeShowsAllGames) RequestLibraryCovers();
        RebuildHome(); // the list holds a different number of games in each style
        App.Sounds.Play(SoundKind.Select);
    }
}
