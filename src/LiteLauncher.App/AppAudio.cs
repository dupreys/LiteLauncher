using System.Runtime.InteropServices;

namespace LiteLauncher.App;

/// <summary>
/// Mutes or unmutes the audio of specific programs (the per-app volume mixer of Windows) without touching the
/// master volume. Best-effort: with no audio device or a protected session it simply does nothing.
/// </summary>
internal static class AppAudio
{
    public static void SetMute(IReadOnlySet<uint> processIds, bool mute)
    {
        if (processIds.Count == 0) return;

        object? managerObject = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var hr = enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);
            Marshal.ReleaseComObject(enumerator);
            if (hr != 0 || device is null) return;

            var iid = typeof(IAudioSessionManager2).GUID;
            hr = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out managerObject);
            Marshal.ReleaseComObject(device);
            if (hr != 0 || managerObject is not IAudioSessionManager2 manager) return;

            if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions is null) return;
            try
            {
                if (sessions.GetCount(out var count) != 0) return;
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var session) != 0 || session is null) continue;
                    try
                    {
                        if (session.GetProcessId(out var pid) != 0 || !processIds.Contains(pid)) continue;
                        if (session is ISimpleAudioVolume volume)
                        {
                            var context = Guid.Empty;
                            volume.SetMute(mute, ref context);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessions);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // no audio device, or the session list changed while reading it
        }
        finally
        {
            if (managerObject is not null) Marshal.ReleaseComObject(managerObject);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr groupingId, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr groupingId, uint streamFlags, out IntPtr audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    // Method order must match the COM vtable: IAudioSessionControl first, then the IAudioSessionControl2 additions.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
