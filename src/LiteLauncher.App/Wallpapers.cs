using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiteLauncher.App;

/// <summary>Ready-made wallpapers, drawn in code (no image files): a gradient with two soft glows.</summary>
public static class Wallpapers
{
    public sealed record Preset(string Id, string Name, string Top, string Bottom, string GlowA, Point CenterA, string GlowB, Point CenterB);

    public static readonly Preset[] Presets =
    {
        new("aurora", "Aurora", "#06151A", "#04080F", "#3300E5A8", new(0.25, 0.30), "#337B5CFF", new(0.80, 0.20)),
        new("sunset", "Sunset", "#2A0F2E", "#0C0612", "#66FF7A3C", new(0.50, 0.95), "#44FF3C8E", new(0.15, 0.25)),
        new("ocean", "Ocean", "#04162B", "#020A14", "#552B8CFF", new(0.70, 0.35), "#3300D1D1", new(0.20, 0.85)),
        new("nebula", "Nebula", "#12072A", "#05030F", "#558A3CFF", new(0.30, 0.35), "#44FF4FA3", new(0.85, 0.75)),
        new("ember", "Ember", "#240A06", "#0A0403", "#66FF5A1F", new(0.75, 0.90), "#33FFB020", new(0.20, 0.30)),
        new("forest", "Forest", "#07190F", "#030906", "#4422C55E", new(0.25, 0.80), "#3300A38C", new(0.80, 0.25)),
        new("graphite", "Graphite", "#20242B", "#090A0D", "#33FFFFFF", new(0.50, 0.15), "#223C4A63", new(0.85, 0.85)),
        new("rose", "Rose", "#2B0B1B", "#0D0409", "#55FF5C9E", new(0.30, 0.25), "#33FF9A5C", new(0.80, 0.80)),
    };

    public static Preset? Find(string? value) =>
        value is not null && value.StartsWith("preset:", StringComparison.Ordinal)
            ? Presets.FirstOrDefault(p => p.Id == value["preset:".Length..])
            : null;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Draws a preset at the given size (a frozen bitmap, so the normal fit/fill logic applies to it).</summary>
    public static BitmapSource Render(Preset preset, int width, int height)
    {
        var rect = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(C(preset.Top), C(preset.Bottom), 90), null, rect);
            foreach (var (glow, centre) in new[] { (preset.GlowA, preset.CenterA), (preset.GlowB, preset.CenterB) })
            {
                var colour = C(glow);
                var brush = new RadialGradientBrush(colour, Color.FromArgb(0, colour.R, colour.G, colour.B))
                {
                    Center = centre,
                    GradientOrigin = centre,
                    RadiusX = 0.65,
                    RadiusY = 0.9,
                };
                dc.DrawRectangle(brush, null, rect);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
