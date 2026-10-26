using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteLauncher.Core;

/// <summary>A Wi-Fi network seen by the PC's adapter.</summary>
public sealed record WifiNetwork(string Ssid, int? Signal, bool Known, bool Connected);

/// <summary>
/// Reads and builds the text that Windows' <c>netsh wlan</c> uses. Only the parts that stay the same in every
/// Windows language are relied on (the "SSID" keyword, the "%" of the signal, the "name : value" layout).
/// </summary>
public static class WifiNetsh
{
    private static readonly Regex SsidBlock = new(@"^\s*SSID\s+\d+\s*:\s*(.*?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CurrentSsid = new(@"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Percent = new(@":\s*(\d{1,3})\s*%\s*$", RegexOptions.Compiled);
    private static readonly Regex ProfileLine = new(@"^\s+[^:]+:\s*(.+?)\s*$", RegexOptions.Compiled);

    /// <summary>Parses <c>netsh wlan show networks mode=bssid</c>. Hidden networks (no name) are skipped.</summary>
    public static IReadOnlyList<WifiNetwork> ParseNetworks(string? output, string? connectedSsid, IEnumerable<string> knownProfiles)
    {
        var known = new HashSet<string>(knownProfiles, StringComparer.OrdinalIgnoreCase);
        var best = new Dictionary<string, int?>(StringComparer.Ordinal);
        string? current = null;

        foreach (var line in (output ?? "").Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (SsidBlock.Match(text) is { Success: true } block)
            {
                current = block.Groups[1].Value;
                if (current.Length == 0) current = null;
                else best.TryAdd(current, null);
                continue;
            }

            if (current is not null && Percent.Match(text) is { Success: true } signal &&
                int.TryParse(signal.Groups[1].Value, out var value))
            {
                var old = best[current];
                if (old is null || value > old) best[current] = Math.Min(value, 100);
            }
        }

        return best
            .Select(p => new WifiNetwork(p.Key, p.Value, known.Contains(p.Key),
                connectedSsid is not null && string.Equals(p.Key, connectedSsid, StringComparison.Ordinal)))
            .OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.Known)
            .ThenByDescending(n => n.Signal ?? 0)
            .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The network you are connected to, from <c>netsh wlan show interfaces</c> (null when disconnected).</summary>
    public static string? ParseConnectedSsid(string? output)
    {
        foreach (var line in (output ?? "").Split('\n'))
        {
            if (CurrentSsid.Match(line.TrimEnd('\r')) is { Success: true } m) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>The saved Wi-Fi profiles, from <c>netsh wlan show profiles</c>.</summary>
    public static IReadOnlyList<string> ParseProfiles(string? output)
    {
        var names = new List<string>();
        foreach (var line in (output ?? "").Split('\n'))
        {
            if (ProfileLine.Match(line.TrimEnd('\r')) is not { Success: true } m) continue;
            var value = m.Groups[1].Value;
            if (value.StartsWith('<')) continue; // "<None>"
            names.Add(value);
        }
        return names;
    }

    /// <summary>Wi-Fi passphrases are 8 to 63 characters; an empty one means an open network.</summary>
    public static bool IsValidPassphrase(string? password) =>
        string.IsNullOrEmpty(password) || (password.Length is >= 8 and <= 63);

    /// <summary>
    /// A profile for <c>netsh wlan add profile</c>: WPA2-Personal (AES) when there is a password, otherwise an open network.
    /// WPA3-only, WEP and enterprise networks need to be set up in Windows' own settings.
    /// </summary>
    public static string BuildProfileXml(string ssid, string? password)
    {
        var name = SecurityElement.Escape(ssid);
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\"?>\n");
        xml.Append("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
        xml.Append($"<name>{name}</name><SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>");
        xml.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>");

        if (string.IsNullOrEmpty(password))
        {
            xml.Append("<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>");
        }
        else
        {
            xml.Append("<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>");
            xml.Append($"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>");
        }

        xml.Append("</security></MSM></WLANProfile>");
        return xml.ToString();
    }
}
