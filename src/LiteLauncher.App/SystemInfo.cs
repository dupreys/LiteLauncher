using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteLauncher.App;

internal enum NetworkKind { None, Wifi, Ethernet }

internal static class SystemInfo
{
    /// <summary>
    /// The connection the PC is using. Only adapters with a default gateway count, so virtual switches
    /// (Hyper-V, VPN, VM adapters) don't make an offline PC look connected.
    /// </summary>
    public static NetworkKind GetNetwork()
    {
        try
        {
            var wifi = false;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                var isEthernet = nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx;
                var isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                if (!isEthernet && !isWifi) continue;

                var hasGateway = nic.GetIPProperties().GatewayAddresses
                    .Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                if (!hasGateway) continue;

                if (isEthernet) return NetworkKind.Ethernet;
                wifi = true;
            }
            return wifi ? NetworkKind.Wifi : NetworkKind.None;
        }
        catch (NetworkInformationException)
        {
            return NetworkKind.None;
        }
    }

    /// <summary>
    /// True when running from the MSIX package. That is how Windows starts a "gaming home app" (Xbox mode),
    /// and it cannot pass command-line arguments, so the packaged app always starts full screen.
    /// </summary>
    public static bool IsPackaged()
    {
        try
        {
            uint length = 0;
            const int appModelErrorNoPackage = 15700;
            return GetCurrentPackageFullName(ref length, null) != appModelErrorNoPackage;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // Windows older than 8
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);

    /// <summary>Puts the PC to sleep (not hibernate). Returns false when Windows refuses.</summary>
    public static bool Sleep() => SetSuspendState(false, false, false);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
                                               [MarshalAs(UnmanagedType.U1)] bool forceCritical,
                                               [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    /// <summary>Battery state, or null on a desktop PC (no battery).</summary>
    public static (int Percent, bool Charging)? GetBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;

        const byte noBattery = 128;
        const byte unknownPercent = 255;
        if ((status.BatteryFlag & noBattery) != 0 || status.BatteryLifePercent == unknownPercent) return null;

        return (Math.Min((int)status.BatteryLifePercent, 100), status.ACLineStatus == 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
