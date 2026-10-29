using System.Windows;
using System.Windows.Media;
using LiteLauncher.Core.Settings;

namespace LiteLauncher.App;

/// <summary>
/// Pushes the user's colours into the application resources. Every brush is consumed with
/// DynamicResource, so replacing the entries re-themes the open window instantly.
/// </summary>
public static class ThemeManager
{
    public static readonly (string Name, string Hex)[] AccentColors =
    {
        ("Blue", "#4C9AFF"), ("Green", "#52B043"), ("Purple", "#9B6CFF"), ("Red", "#E5484D"),
        ("Orange", "#FF8A3D"), ("Pink", "#F76DA8"), ("Cyan", "#2ED3C6"),
    };

    public static readonly (string Name, string Hex)[] BackgroundColors =
    {
        ("Graphite", "#0F1115"), ("Midnight", "#0B1020"), ("Plum", "#150B1E"),
        ("Forest", "#0A1612"), ("Crimson", "#1A0B0E"), ("OLED black", "#000000"),
    };

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;

        var accent = Parse(settings.Accent, Color.FromRgb(0x4C, 0x9A, 0xFF));
        var background = settings.UseBackgroundColor
            ? Parse(settings.Background, Color.FromRgb(0x0F, 0x11, 0x15))
            : Colors.Black; // colour switched off: OLED black

        resources["AccentColor"] = accent;
        resources["BgColor"] = background;
        resources["Accent"] = Frozen(accent);
        resources["Bg"] = Frozen(background);

        // Tiles and panels are the background lifted towards white, so they always match the chosen tint.
        resources["Surface"] = Frozen(Mix(background, Colors.White, 0.13));
        resources["SurfaceHi"] = Frozen(Mix(background, Colors.White, 0.22));
    }

    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            return hex is null ? fallback : (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));
}
