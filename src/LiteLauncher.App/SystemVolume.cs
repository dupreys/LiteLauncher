using System.Runtime.InteropServices;

namespace LiteLauncher.App;

/// <summary>
/// Master volume and mute of the default Windows playback device (Core Audio).
/// Every call is best-effort: with no audio device the sliders are simply disabled.
/// </summary>
internal static class SystemVolume
{
    public static bool TryGet(out float level, out bool muted)
    {
        level = 0;
        muted = false;

        var volume = Open();
        if (volume is null) return false;

        try
        {
            volume.GetMasterVolumeLevelScalar(out level);
            volume.GetMute(out muted);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    public static void TrySetLevel(float level)
    {
        var volume = Open();
        if (volume is null) return;

        try
        {
            var context = Guid.Empty;
            volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context);
        }
        catch (COMException) { }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    public static void TrySetMute(bool mute)
    {
        var volume = Open();
        if (volume is null) return;

        try
        {
            var context = Guid.Empty;
            volume.SetMute(mute, ref context);
        }
        catch (COMException) { }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    private static IAudioEndpointVolume? Open()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var hr = enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);
            Marshal.ReleaseComObject(enumerator);
            if (hr != 0 || device is null) return null; // no playback device

            var iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var volume);
            Marshal.ReleaseComObject(device);

            return hr == 0 ? volume : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IAudioEndpointVolume endpointVolume);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    // Method order must match the COM vtable exactly; only the entries up to GetMute are declared.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
