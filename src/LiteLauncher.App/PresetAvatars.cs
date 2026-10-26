using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiteLauncher.App;

/// <summary>
/// Ready-made profile pictures. They are drawn in code (gradient + icon glyph), so the project ships no
/// image files. Picking one writes a PNG exactly like a picture chosen from disk, so the rest of the
/// app treats it the same way.
/// </summary>
internal static class PresetAvatars
{
    public sealed record Preset(string Id, string Name, string From, string To, string Glyph);

    // Glyphs come from Segoe MDL2 Assets (Windows 10) / Segoe Fluent Icons (Windows 11).
    public static readonly Preset[] All =
    {
        new("controller", "Controller", "#107C10", "#0B4F0B", "\uE7FC"),
        new("star",       "Star",       "#F7B500", "#C2410C", "\uE734"),
        new("bolt",       "Lightning",  "#2563EB", "#1E1B4B", "\uE945"),
        new("heart",      "Heart",      "#E11D48", "#7F1D1D", "\uEB51"),
        new("globe",      "Explorer",   "#0D9488", "#134E4A", "\uE909"),
        new("music",      "Music",      "#DB2777", "#4C1D95", "\uE8D6"),
        new("trophy",     "Trophy",     "#CA8A04", "#713F12", "\uE7C1"),
        new("rocket",     "Flight",     "#475569", "#0F172A", "\uE709"),
    };

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Draws the preset at <paramref name="size"/> pixels. Must run on a UI (STA) thread.</summary>
    public static BitmapSource Render(Preset preset, int size = ProfilePicture.Size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var gradient = new LinearGradientBrush(
                ThemeManager.Parse(preset.From, Colors.Gray),
                ThemeManager.Parse(preset.To, Colors.Black),
                new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(gradient, null, new Rect(0, 0, size, size));

            var text = new FormattedText(
                preset.Glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size * 0.45,
                Brushes.White,
                1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes the preset as a PNG. Returns false if the file could not be written.</summary>
    public static bool TrySave(Preset preset, string destination)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(preset)));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var stream = File.Create(destination);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
