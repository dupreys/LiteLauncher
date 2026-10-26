using LiteLauncher.Core;
using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class FriendsAndGridDbTests
{
    [Fact]
    public void Only_online_friends_are_listed_with_players_in_game_first()
    {
        var friends = SteamFriends.ParseSummaries("""
            {"response":{"players":[
              {"steamid":"76561197960265729","personaname":"Zed","personastate":1},
              {"steamid":"76561197960265730","personaname":"Offline Olga","personastate":0},
              {"steamid":"76561197960265731","personaname":"Amy","personastate":1,"gameextrainfo":"Dota 2"},
              {"steamid":"76561197960265732","personaname":"Bob","personastate":3}
            ]}}
            """);

        var online = SteamFriends.SortOnline(friends);

        Assert.Equal(new[] { "Amy", "Zed", "Bob" }, online.Select(f => f.Name));
        Assert.Equal("Playing Dota 2", online[0].StatusText);
        Assert.Equal("Away", online[2].StatusText);
    }

    [Fact]
    public void Friend_ids_are_read_from_friend_list()
    {
        var ids = SteamFriends.ParseFriendIds("""{"friendslist":{"friends":[{"steamid":"76561197960265729"},{"steamid":"bad"}]}}""");
        Assert.Equal(new[] { "76561197960265729" }, ids);
    }

    [Fact]
    public void SteamGridDb_game_id_and_images_are_parsed()
    {
        Assert.Equal(1234, SteamGridDb.ParseGameId("""{"success":true,"data":{"id":1234,"name":"Dota 2"}}"""));
        Assert.Equal(55, SteamGridDb.ParseFirstSearchId("""{"success":true,"data":[{"id":55},{"id":66}]}"""));

        var images = SteamGridDb.ParseImages("""
            {"success":true,"data":[{"url":"https://cdn/x.png","thumb":"https://cdn/x_t.png","width":600,"height":900},{"thumb":"no-url"}]}
            """);
        var only = Assert.Single(images);
        Assert.Equal("https://cdn/x_t.png", only.ThumbUrl);
        Assert.Equal(900, only.Height);
    }

    [Fact]
    public void Logo_override_is_applied_only_when_file_exists()
    {
        var file = Path.GetTempFileName();
        try
        {
            var game = new Game("Steam", "570", "Dota 2");
            var result = GameOverrides.Apply(game, new Dictionary<string, GameOverride> { ["Steam:570"] = new() { LogoPath = file } });
            Assert.Equal(file, result.LogoPath);
        }
        finally { File.Delete(file); }
    }
}
