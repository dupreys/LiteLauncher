using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LiteLauncher.App;

/// <summary>
/// Shows and hides the Windows on-screen keyboard. The touch keyboard (TabTip) is tried first because it is the
/// one built into the system and works with a controller on Windows 11; the classic osk.exe is the fallback.
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
            if (File.Exists(TabTipPath))
            {
                Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
                await Task.Delay(600); // the touch keyboard needs a moment to appear

                if (generation != _generation) { Close(); return; } // closed while it was still starting
                if (IsWindowShown(TouchKeyboardClass)) return;

                // Windows 11's text input panel (the controller-friendly keyboard) is asked for directly.
                if (ToggleTextInputPanel())
                {
                    await Task.Delay(500);
                    if (generation != _generation) { Close(); return; }
                    if (IsWindowShown(TouchKeyboardClass)) return;
                }
            }

            if (generation != _generation) return;
            Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true });
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

    private static bool IsWindowShown(string windowClass)
    {
        var window = FindWindow(windowClass, null);
        return window != IntPtr.Zero && IsWindowVisible(window);
    }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr window);
    }

    private static readonly Guid TipInvocationClass = new("4ce576fa-83dc-4f88-951c-9d0782b4e376");

    private static bool ToggleTextInputPanel()
    {
        try
        {
            if (Type.GetTypeFromCLSID(TipInvocationClass) is not { } type) return false;
            var tip = (ITipInvocation)Activator.CreateInstance(type)!;
            tip.Toggle(GetDesktopWindow());
            Marshal.ReleaseComObject(tip);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
