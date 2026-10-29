using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LiteLauncher.Core;

namespace LiteLauncher.App;

/// <summary>Force-closes a game by ending every process whose program lives in the game's install folder.</summary>
internal static class GameProcessKiller
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Returns how many processes were ended; 0 when none were found (or no safe folder is known).</summary>
    public static int Kill(Game game)
    {
        var folder = ProcessScope.Resolve(game);
        if (folder is null) return 0;

        var self = Environment.ProcessId;
        var killed = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self) continue;
                var image = ImagePath(process.Id);
                if (image is null || !ProcessScope.IsInside(folder, image)) continue;

                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Already gone, or protected (anti-cheat, another user's process): nothing more we can do.
            }
            finally
            {
                process.Dispose();
            }
        }
        return killed;
    }

    /// <summary>Ids of the running processes that belong to the game (programs inside its install folder).</summary>
    public static HashSet<uint> GamePids(Game game)
    {
        var ids = new HashSet<uint>();
        var folder = ProcessScope.Resolve(game);
        if (folder is null) return ids;

        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self) continue;
                var image = ImagePath(process.Id);
                if (image is not null && ProcessScope.IsInside(folder, image)) ids.Add((uint)process.Id);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // gone already, or not ours to look at
            }
            finally
            {
                process.Dispose();
            }
        }
        return ids;
    }

    /// <summary>Id of the process that owns the window in front right now (0 when unknown).</summary>
    public static uint ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    /// <summary>Brings the running game's window to the front. False when no window of the game was found.</summary>
    public static bool BringToFront(Game game)
    {
        var folder = ProcessScope.Resolve(game);
        if (folder is null) return false;

        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self) continue;
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;
                var image = ImagePath(process.Id);
                if (image is null || !ProcessScope.IsInside(folder, image)) continue;

                if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
                return SetForegroundWindow(window);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // closing right now, or not ours to touch
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    private static string? ImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
}
