using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class WifiNetshTests
{
    private const string Networks = """
        Interface name : Wi-Fi
        There are 4 networks currently visible.

        SSID 1 : HomeNet
            Network type            : Infrastructure
            Authentication          : WPA2-Personal
            BSSID 1                 : aa:bb:cc:dd:ee:01
                 Signal             : 40%
                 Radio type         : 802.11ac
            BSSID 2                 : aa:bb:cc:dd:ee:02
                 Signal             : 87%

        SSID 2 : Cafe
            Network type            : Infrastructure
            BSSID 1                 : 11:22:33:44:55:66
                 Signal             : 55%

        SSID 3 :
            Network type            : Infrastructure
            BSSID 1                 : 99:99:99:99:99:99
                 Signal             : 99%

        SSID 4 : Neighbor
            BSSID 1                 : 77:77:77:77:77:77
                 Signal             : 70%
        """;

    private const string Interfaces = """
        There is 1 interface on the system:

            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi 6 AX201 160MHz
            BSSID                  : aa:bb:cc:dd:ee:02
            SSID                   : HomeNet
            Signal                 : 87%
        """;

    private const string Profiles = """
        Profiles on interface Wi-Fi:

        Group policy profiles (read only)
        ---------------------------------
            <None>

        User profiles
        -------------
            All User Profile     : HomeNet
            All User Profile     : Neighbor
        """;

    [Fact]
    public void Networks_keep_best_signal_skip_hidden_and_sort_connected_then_saved_then_signal()
    {
        var list = WifiNetsh.ParseNetworks(Networks, "HomeNet", new[] { "HomeNet", "Neighbor" });

        Assert.Equal(new[] { "HomeNet", "Neighbor", "Cafe" }, list.Select(n => n.Ssid));
        Assert.Equal(87, list[0].Signal);
        Assert.True(list[0].Connected);
        Assert.True(list[0].Known);
        Assert.True(list[1].Known);
        Assert.False(list[1].Connected);
        Assert.False(list[2].Known);
        Assert.Equal(55, list[2].Signal);
    }

    [Fact]
    public void Connected_ssid_ignores_bssid_line_and_is_null_when_disconnected()
    {
        Assert.Equal("HomeNet", WifiNetsh.ParseConnectedSsid(Interfaces));
        Assert.Null(WifiNetsh.ParseConnectedSsid("    Name  : Wi-Fi\n    BSSID : aa:bb:cc:dd:ee:02\n"));
        Assert.Null(WifiNetsh.ParseConnectedSsid(null));
    }

    [Fact]
    public void Profiles_are_read_without_headers_or_none_entries() =>
        Assert.Equal(new[] { "HomeNet", "Neighbor" }, WifiNetsh.ParseProfiles(Profiles));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    public void Passphrase_length_rules(string? password, bool valid) =>
        Assert.Equal(valid, WifiNetsh.IsValidPassphrase(password));

    [Fact]
    public void Profile_xml_escapes_names_and_picks_security_from_the_password()
    {
        var secured = WifiNetsh.BuildProfileXml("A&B <Net>", "pass<&>word");
        Assert.Contains("<name>A&amp;B &lt;Net&gt;</name>", secured);
        Assert.Contains("WPA2PSK", secured);
        Assert.Contains("<keyMaterial>pass&lt;&amp;&gt;word</keyMaterial>", secured);
        System.Xml.Linq.XDocument.Parse(secured); // must be well-formed

        var open = WifiNetsh.BuildProfileXml("Guest", null);
        Assert.Contains("<authentication>open</authentication>", open);
        Assert.DoesNotContain("keyMaterial", open);
        System.Xml.Linq.XDocument.Parse(open);
    }
}
