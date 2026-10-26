using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class AchievementsTests
{
    [Fact]
    public void Api_json_counts_unlocked_and_total()
    {
        const string json = """
            {"playerstats":{"steamID":"1","gameName":"X","achievements":[
              {"apiname":"A","achieved":1,"unlocktime":1},
              {"apiname":"B","achieved":0,"unlocktime":0},
              {"apiname":"C","achieved":1,"unlocktime":2}],"success":true}}
            """;

        var result = Achievements.ParseApiJson(json);

        Assert.Equal(AchievementStatus.Ok, result.Status);
        Assert.Equal(2, result.Unlocked);
        Assert.Equal(3, result.Total);
        Assert.Equal(2 / 3.0, result.Fraction, 3);
    }

    [Fact]
    public void Api_json_without_achievements_means_none()
    {
        var result = Achievements.ParseApiJson("""{"playerstats":{"error":"Requested app has no stats","success":false}}""");
        Assert.Equal(AchievementStatus.None, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Api_json_garbage_is_unavailable(string? json) =>
        Assert.Equal(AchievementStatus.Unavailable, Achievements.ParseApiJson(json).Status);

    [Fact]
    public void Community_xml_counts_closed_achievements()
    {
        const string xml = """
            <playerstats><steamID64>1</steamID64><achievements>
              <achievement closed="1"><name>A</name></achievement>
              <achievement closed="0"><name>B</name></achievement>
              <achievement closed="0"><name>C</name></achievement>
              <achievement closed="1"><name>D</name></achievement>
            </achievements></playerstats>
            """;

        var result = Achievements.ParseCommunityXml(xml);

        Assert.Equal(AchievementStatus.Ok, result.Status);
        Assert.Equal(2, result.Unlocked);
        Assert.Equal(4, result.Total);
    }

    [Fact]
    public void Community_xml_error_or_html_is_unavailable()
    {
        Assert.Equal(AchievementStatus.Unavailable,
            Achievements.ParseCommunityXml("<playerstats><error><![CDATA[This profile is private.]]></error></playerstats>").Status);
        Assert.Equal(AchievementStatus.Unavailable, Achievements.ParseCommunityXml("<html><body>oops</body></html>").Status);
        Assert.Equal(AchievementStatus.Unavailable, Achievements.ParseCommunityXml("<html><br></html>").Status);
    }

    [Fact]
    public void Community_xml_without_achievements_means_none() =>
        Assert.Equal(AchievementStatus.None, Achievements.ParseCommunityXml("<playerstats><steamID64>1</steamID64></playerstats>").Status);

    [Fact]
    public void Community_xml_lists_items_unlocked_first_with_icons()
    {
        const string xml = """
            <playerstats><achievements>
              <achievement closed="0"><name>Locked one</name><description>Do it</description><iconOpen>https://x/open.jpg</iconOpen><iconClosed>https://x/closed.jpg</iconClosed></achievement>
              <achievement closed="1"><name>Old</name><description>Done</description><iconClosed>https://x/old.jpg</iconClosed><unlockTimestamp>1000</unlockTimestamp></achievement>
              <achievement closed="1"><name>New</name><iconClosed>https://x/new.jpg</iconClosed><unlockTimestamp>2000</unlockTimestamp></achievement>
            </achievements></playerstats>
            """;

        var items = Achievements.ParseCommunityXml(xml).Items;

        Assert.Equal(new[] { "New", "Old", "Locked one" }, items.Select(i => i.Name));
        Assert.Equal("https://x/open.jpg", items[2].IconUrl);   // locked: grey icon
        Assert.Equal("https://x/new.jpg", items[0].IconUrl);    // unlocked: colour icon
        Assert.Null(items[0].Description);                      // hidden/empty description stays null
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000), items[0].UnlockedAt);
        Assert.Null(items[2].UnlockedAt);
    }

    [Fact]
    public void Api_json_items_carry_names_and_unlock_times()
    {
        const string json = """
            {"playerstats":{"achievements":[
              {"apiname":"A","achieved":0,"name":"First","description":"Start","unlocktime":0},
              {"apiname":"B","achieved":1,"name":"Second","description":"Finish","unlocktime":500}],"success":true}}
            """;

        var items = Achievements.ParseApiJson(json).Items;

        Assert.Equal(new[] { "Second", "First" }, items.Select(i => i.Name));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(500), items[0].UnlockedAt);
    }

    [Fact]
    public async Task Epic_games_are_not_supported_and_make_no_request()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var epic = new Game("Epic", "abc", "Some Game");

        var result = await Achievements.FetchAsync(http, epic, "76561198000000000", "key");

        Assert.Equal(AchievementStatus.NotSupported, result.Status);
    }

    [Fact]
    public async Task Steam_without_sign_in_asks_to_sign_in()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var result = await Achievements.FetchAsync(http, new Game("Steam", "570", "Dota 2"), null, null);
        Assert.Equal(AchievementStatus.NotSignedIn, result.Status);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No network request expected.");
    }
}
