using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiteLauncher.App;

/// <summary>Start menu app icons, asked from the Windows shell (works for classic programs and Store apps alike).</summary>
internal static class AppIcons
{
    private const int ImageSize = 256;
    private const uint IconOnly = 0x4; // SIIGBF_ICONONLY
    private const uint BiggerSizeOk = 0x1; // SIIGBF_BIGGERSIZEOK
    private static readonly Guid ImageFactoryId = new("bcc18b79-ba16-42cb-b4b5-bc8ba5f2e9c5");

    /// <summary>Loads the icons on one STA thread, handing each result back as a frozen bitmap (null when Windows has none).</summary>
    public static Task LoadAsync(IReadOnlyList<string> appIds, Action<string, BitmapSource?> onIcon, CancellationToken cancel)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var id in appIds)
                {
                    if (cancel.IsCancellationRequested) break;
                    onIcon(id, TryLoad(id));
                }
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        })
        { IsBackground = true, Name = "AppIcons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static int _loaded, _failed;
    private static readonly List<string> _problems = new();

    /// <summary>One line for the diagnostics screen: how many icons Windows gave and why some did not come.</summary>
    public static string Report
    {
        get
        {
            lock (_problems)
                return $"{_loaded} loaded, {_failed} without icon" + (_problems.Count > 0 ? " — " + string.Join(" | ", _problems) : "");
        }
    }

    private static void Note(string appId, string why)
    {
        lock (_problems)
        {
            _failed++;
            if (_problems.Count < 4) _problems.Add($"{appId}: {why}");
        }
    }

    private static BitmapSource? TryLoad(string appId)
    {
        string why1;
        try
        {
            if (TryLoadWithImageFactory(appId, out var viaFactory, out why1)) { Count(); return viaFactory; }
        }
        catch (Exception ex)
        {
            why1 = ex.GetType().Name + ": " + ex.Message;
        }

        // Second way: ask the shell for the app's ordinary icon.
        try
        {
            if (TryLoadWithFileInfo(appId, out var viaInfo)) { Count(); return viaInfo; }
            Note(appId, "image factory (" + why1 + "); shell icon returned nothing");
        }
        catch (Exception ex)
        {
            Note(appId, "image factory (" + why1 + "); shell icon " + ex.GetType().Name + ": " + ex.Message);
            CrashLog.Write("App icon " + appId, ex);
        }
        return null;

        static void Count() { lock (_problems) _loaded++; }
    }

    private static bool TryLoadWithImageFactory(string appId, out BitmapSource? source, out string why)
    {
        source = null;
        why = "";
        var bitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, IntPtr.Zero, ImageFactoryId, out var factory);
            try
            {
                var hr = 0;
                // Icon only first; then whatever the shell has at a size it is happy with.
                foreach (var flags in new[] { IconOnly, IconOnly | BiggerSizeOk, 0u, BiggerSizeOk })
                {
                    hr = factory.GetImage(new Size32 { Width = ImageSize, Height = ImageSize }, flags, out bitmap);
                    if (hr == 0 && bitmap != IntPtr.Zero) break;
                    bitmap = IntPtr.Zero;
                }
                if (bitmap == IntPtr.Zero)
                {
                    why = "GetImage 0x" + hr.ToString("X8");
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }

            source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return true;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    private static bool TryLoadWithFileInfo(string appId, out BitmapSource? source)
    {
        source = null;
        var pidl = IntPtr.Zero;
        var icon = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(@"shell:AppsFolder\" + appId, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return false;

            // The shell's biggest icon (256 px) for this app; the 32 px one only when that is not available.
            var info = new ShFileInfo();
            SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiSysIconIndex | ShgfiPidl);
            var index = info.iIcon;
            foreach (var size in new[] { ShilJumbo, ShilExtraLarge })
            {
                var iid = ImageListId;
                if (SHGetImageList(size, ref iid, out var list) != 0 || list is null) continue;
                try
                {
                    if (list.GetIcon(index, IldTransparent, out icon) == 0 && icon != IntPtr.Zero) break;
                    icon = IntPtr.Zero;
                }
                finally
                {
                    Marshal.ReleaseComObject(list);
                }
            }

            if (icon == IntPtr.Zero)
            {
                info = new ShFileInfo();
                SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon | ShgfiPidl);
                icon = info.hIcon;
            }
            if (icon == IntPtr.Zero) return false;

            source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return true;
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private const uint ShgfiIcon = 0x100, ShgfiLargeIcon = 0x0, ShgfiPidl = 0x8, ShgfiSysIconIndex = 0x4000;
    private const int ShilJumbo = 4, ShilExtraLarge = 2, IldTransparent = 1;
    private static readonly Guid ImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    // Only the methods up to GetIcon are declared; their order must match the COM table exactly.
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr image, IntPtr mask, ref int index);
        [PreserveSig] int ReplaceIcon(int index, IntPtr icon, ref int result);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int maskColour, ref int index);
        [PreserveSig] int Draw(IntPtr parameters);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, int flags, out IntPtr icon);
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetImageList(int imageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList? list);


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Puts the icon in the middle of a dark, cover-shaped tile so it sits like the game covers do.</summary>
    public static ImageSource ComposeTile(BitmapSource icon)
    {
        const int width = 400, height = 400, size = 300;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2A, 0x30, 0x3A), Color.FromRgb(0x12, 0x15, 0x1A), 90), null, new Rect(0, 0, width, height));
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(icon, new Rect((width - size) / 2.0, (height - size) / 2.0, size, size));
        }

        var tile = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        tile.Render(visual);
        tile.Freeze();
        return tile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width;
        public int Height;
    }

    [ComImport, Guid("bcc18b79-ba16-42cb-b4b5-bc8ba5f2e9c5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size32 size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindContext,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
