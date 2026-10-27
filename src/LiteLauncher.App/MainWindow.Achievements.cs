using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiteLauncher.Core;

namespace LiteLauncher.App;

// Game menu -> Achievements: the full list for the open game (icon, name, description, unlock date).
public partial class MainWindow
{
    private AchievementResult? _achievementsResult; // for the game shown in the menu; null while loading

    private bool AchievementsOpen => AchievementsPanel.Visibility == Visibility.Visible;

    private void OnDetailsAchievementsClick(object sender, RoutedEventArgs e)
    {
        if (DetailsItem() is not { } item) return;

        if (_achievementsResult is null)
        {
            ShowToast("Achievements are still loading…");
            return;
        }

        if (_achievementsResult.Status != AchievementStatus.Ok)
        {
            ShowToast(Achievements.Describe(_achievementsResult)); // sign-in, privacy, none, or Epic
            return;
        }

        OpenAchievements(item, _achievementsResult);
    }

    private void OpenAchievements(GameItem item, AchievementResult result)
    {
        VirtualKeyboard.Hide();
        AchievementsTitle.Text = item.Name;
        AchievementsSummary.Text = $"{Achievements.Describe(result)}  ·  {result.Fraction:P0}";
        AchievementsListFill.Width = new GridLength(result.Fraction, GridUnitType.Star);
        AchievementsListRest.Width = new GridLength(1 - result.Fraction, GridUnitType.Star);

        AchievementsList.Children.Clear();
        foreach (var achievement in result.Items) AchievementsList.Children.Add(BuildAchievementRow(achievement));

        AchievementsScroll.ScrollToTop();
        AchievementsPanel.Visibility = Visibility.Visible;
        AchievementsPanel.UpdateLayout();
        App.Sounds.Play(SoundKind.Select);

        // First row so the D-pad can scroll right away; "Back" is one step up.
        Keyboard.Focus(AchievementsList.Children.Count > 0 ? (IInputElement)AchievementsList.Children[0] : AchievementsBackButton);
    }

    private void CloseAchievements()
    {
        AchievementsPanel.Visibility = Visibility.Collapsed;
        AchievementsList.Children.Clear();
        Keyboard.Focus(DetailsAchievementsButton);
    }

    private void OnAchievementsCloseClick(object sender, RoutedEventArgs e) => CloseAchievements();

    private Button BuildAchievementRow(AchievementItem achievement)
    {
        var icon = new Border
        {
            Width = 56,
            Height = 56,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = achievement.Unlocked ? 1 : 0.6,
        };
        icon.SetResourceReference(Border.BackgroundProperty, "Surface");
        if (LoadIcon(achievement.IconUrl) is { } source)
            icon.Child = new Image { Source = source, Stretch = Stretch.UniformToFill };

        var title = new TextBlock
        {
            Text = achievement.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var description = new TextBlock
        {
            // Hidden achievements have no text until you earn them.
            Text = achievement.Description ?? (achievement.Unlocked ? "" : "Hidden achievement"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");

        var status = new TextBlock
        {
            Text = achievement.Unlocked
                ? achievement.UnlockedAt is { } at
                    ? "✓  " + at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                    : "✓  Unlocked"
                : "Locked",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, achievement.Unlocked ? "Accent" : "TextMuted");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(description);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(status, 2);
        layout.Children.Add(icon);
        layout.Children.Add(text);
        layout.Children.Add(status);

        return new Button
        {
            Style = (Style)FindResource("XboxRow"),
            Content = layout,
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Icons come from Steam's CDN; they load in the background and are simply left out on failure.</summary>
    private static ImageSource? LoadIcon(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.DownloadFailed += (_, _) => { }; // a missing icon is not worth a crash
            bitmap.DecodeFailed += (_, _) => { };
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = 112;
            bitmap.EndInit();
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
