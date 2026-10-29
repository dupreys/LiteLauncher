using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LiteLauncher.App;

/// <summary>
/// Shows and hides the Windows text input panel (touch keyboard, TabTip), which works with a controller on Windows 11.
/// Neither takes keyboard focus away from the launcher's text box, so typed characters land where expected.
/// </summary>
internal static class VirtualKeyboard
{
    private const string TouchKeyboardClass = "IPTip_Main_Window";
    private const string ClassicKeyboardClass = "OSKMainClass";
    private const uint WmSysCommand = 0x0112;
    private static readonly IntPtr ScClose = new(0xF060);

    private static int _generation; // lets a late "show" notice that a "hide" happened in between
    private static bool _requested;

    /// <summary>True between a Show and the next Hide requested by the launcher.</summary>
    public static bool IsVisible => _requested;

    private static string TabTipPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");

    public static async Task ShowAsync()
    {
        if (_requested) return;
        _requested = true;
        var generation = ++_generation;

        try
        {
            // Only the Windows text input panel (touch keyboard) is used. The classic on-screen keyboard (osk.exe) is
            // never started: the panel's window is not always recognised, and a fallback would open both keyboards.
            if (File.Exists(TabTipPath))
            {
                Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
                await Task.Delay(300);
                if (generation != _generation) Close(); // closed while it was still starting
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // No keyboard available on this PC: the physical keyboard still works.
        }
    }

    public static void Hide()
    {
        _generation++;
        _requested = false;
        Close();
    }

    private static void Close()
    {
        PostClose(TouchKeyboardClass);
        PostClose(ClassicKeyboardClass);
    }

    private static void PostClose(string windowClass)
    {
        var window = FindWindow(windowClass, null);
        if (window != IntPtr.Zero) PostMessage(window, WmSysCommand, ScClose, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
