using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiteLauncher.App;

/// <summary>Keeps the Steam and Epic client windows out of the way while a game is starting.</summary>
internal static class StoreWindows
{
    private static readonly string[] ClientProcesses = { "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper" };

    private const int SwShowMinNoActive = 7;
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

    /// <summary>Minimizes (without taking focus) every visible top-level window of the Steam/Epic clients; returns how many.</summary>
    public static int MinimizeClients()
    {
        var pids = new HashSet<uint>();
        foreach (var name in ClientProcesses)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                pids.Add((uint)process.Id);
                process.Dispose();
            }
        }
        if (pids.Count == 0) return 0;

        var count = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(window) && !IsIconic(window)
                && GetWindow(window, GwOwner) == IntPtr.Zero && GetWindowTextLength(window) > 0)
            {
                ShowWindow(window, SwShowMinNoActive);
                count++;
            }
            return true;
        }, IntPtr.Zero);
        return count;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
