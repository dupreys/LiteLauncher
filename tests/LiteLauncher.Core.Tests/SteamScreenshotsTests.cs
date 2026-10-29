using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class SteamScreenshotsTests
{
    private const string Json = """
    {"440":{"success":true,"data":{"screenshots":[
      {"id":0,"path_thumbnail":"https://cdn.akamai.steamstatic.com/steam/apps/440/ss_a.600x338.jpg?t=1","path_full":"https://cdn.akamai.steamstatic.com/steam/apps/440/ss_a.1920x1080.jpg?t=1"},
      {"id":1,"path_thumbnail":"https://evil.example/t.jpg","path_full":"https://evil.example/f.jpg"},
      {"id":2,"path_thumbnail":"http://cdn.akamai.steamstatic.com/t.jpg","path_full":"http://cdn.akamai.steamstatic.com/f.jpg"}
    ]}}}
    """;

    [Fact]
    public void Reads_only_https_images_from_steam_cdns()
    {
        var shots = SteamScreenshots.Parse(Json, "440");
        var shot = Assert.Single(shots);
        Assert.Contains("1920x1080", shot.Url);
        Assert.Contains("600x338", shot.ThumbUrl);
        Assert.Equal(1920, shot.Width);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"440":{"success":false}}""")]
    public void Unreadable_answers_give_an_empty_list(string? json) => Assert.Empty(SteamScreenshots.Parse(json, "440"));
}
