using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiteLauncher.App;

internal static class ProfilePicture
{
    public const int Size = 256;

    /// <summary>
    /// Reads any image the framework can decode, crops it to a centred square, shrinks it to
    /// <see cref="Size"/> and writes a PNG copy. The copy means the picture keeps working even if the
    /// original is moved or deleted. Safe to call from a background thread.
    /// </summary>
    public static bool TrySave(string source, string destination)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(source);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();

            var side = Math.Min(bitmap.PixelWidth, bitmap.PixelHeight);
            if (side <= 0) return false;

            BitmapSource result = new CroppedBitmap(
                bitmap,
                new Int32Rect((bitmap.PixelWidth - side) / 2, (bitmap.PixelHeight - side) / 2, side, side));

            if (side > Size)
            {
                var scale = (double)Size / side;
                result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var stream = File.Create(destination);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
                                       or InvalidOperationException or ArgumentException or UriFormatException
                                       or FileFormatException)
        {
            return false; // not an image, corrupt, or no permission
        }
    }
}
