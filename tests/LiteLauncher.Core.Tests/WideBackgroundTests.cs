using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class WideBackgroundTests
{
    [Fact]
    public void SteamGridDb_backgrounds_prefer_the_least_wide_picture()
    {
        var picked = SteamGridDbArt.Pick(new[]
        {
            new ArtImage("https://cdn2.steamgriddb.com/a.png", "", 1920, 620),
            new ArtImage("https://cdn2.steamgriddb.com/b.png", "", 1600, 650),
            new ArtImage("https://cdn2.steamgriddb.com/c.png", "", 3840, 1240),
        }, StoreArtKind.Backdrop);
        Assert.EndsWith("b.png", picked!.Url);
    }
}

public class SteamStoreSearchTests
{
    [Fact]
    public void Only_an_exact_name_match_gives_an_app_id()
    {
        var json = """{"total":3,"items":[{"type":"app","name":"Hades II","id":1145350},{"type":"dlc","name":"Hades","id":5},{"type":"app","name":"Hades","id":1145360}]}""";
        Assert.Equal("1145360", LiteLauncher.Core.Steam.SteamStoreSearch.ParseExactMatch(json, "HADES"));
        Assert.Null(LiteLauncher.Core.Steam.SteamStoreSearch.ParseExactMatch(json, "Hades III"));
        Assert.Null(LiteLauncher.Core.Steam.SteamStoreSearch.ParseExactMatch("not json", "Hades"));
    }
}

public class WallhavenTests
{
    private const string Detail = """
    {"data":{"id":"abc123","path":"https://w.wallhaven.cc/full/ab/wallhaven-abc123.jpg","file_size":3000000,
      "tags":[{"name":"Hades (game)"},{"name":"video games"},{"name":"artwork"}]}}
    """;

    [Fact]
    public void Search_url_asks_for_safe_16x9_wallpapers_by_name()
    {
        var url = Wallhaven.BuildSearchUrl("Hades: Remastered");
        Assert.Contains("q=hades%20remastered", url);
        Assert.Contains("purity=100", url);
        Assert.Contains("1920x1080", url);
    }

    [Fact]
    public void First_three_ids_are_taken_and_bad_input_gives_none()
    {
        var json = """{"data":[{"id":"a1"},{"id":"b2"},{"id":"c3"},{"id":"d4"}]}""";
        Assert.Equal(new[] { "a1", "b2", "c3" }, Wallhaven.ParseIds(json));
        Assert.Empty(Wallhaven.ParseIds("not json"));
        Assert.Empty(Wallhaven.ParseIds("{}"));
    }

    [Fact]
    public void A_wallpaper_counts_only_when_tags_name_the_game_and_say_video_game()
    {
        Assert.Equal("https://w.wallhaven.cc/full/ab/wallhaven-abc123.jpg", Wallhaven.ParseMatchingPath(Detail, "Hades"));
        Assert.Null(Wallhaven.ParseMatchingPath(Detail, "Hollow Knight"));
        var god = """{"data":{"path":"https://w.wallhaven.cc/full/x.jpg","tags":[{"name":"hades"},{"name":"mythology"}]}}""";
        Assert.Null(Wallhaven.ParseMatchingPath(god, "Hades"));
    }

    [Fact]
    public void Foreign_hosts_and_huge_files_are_refused()
    {
        var evil = """{"data":{"path":"https://evil.example/x.jpg","tags":[{"name":"hades"},{"name":"video games"}]}}""";
        Assert.Null(Wallhaven.ParseMatchingPath(evil, "Hades"));
        var big = """{"data":{"path":"https://w.wallhaven.cc/full/x.jpg","file_size":99000000,"tags":[{"name":"hades"},{"name":"video games"}]}}""";
        Assert.Null(Wallhaven.ParseMatchingPath(big, "Hades"));
        Assert.False(Wallhaven.IsAllowedUrl("https://w.wallhaven.cc.evil.example/x.jpg"));
    }
}

public class WallhavenPickerTests
{
    [Fact]
    public void Search_results_become_pickable_images_and_foreign_or_huge_ones_are_skipped()
    {
        var json = """
        {"data":[
          {"id":"a1","path":"https://w.wallhaven.cc/full/a1/wallhaven-a1.jpg","resolution":"1920x1080","file_size":2000000,"thumbs":{"large":"https://th.wallhaven.cc/lg/a1/a1.jpg"}},
          {"id":"b2","path":"https://evil.example/b2.jpg","resolution":"1920x1080"},
          {"id":"c3","path":"https://w.wallhaven.cc/full/c3/wallhaven-c3.jpg","resolution":"3840x2160","file_size":99000000},
          {"id":"d4","path":"https://w.wallhaven.cc/full/d4/wallhaven-d4.jpg","resolution":"2560x1440","thumbs":{"large":"https://evil.example/t.jpg"}}
        ]}
        """;
        var images = Wallhaven.ParseImages(json);
        Assert.Equal(2, images.Count);
        Assert.Equal("https://th.wallhaven.cc/lg/a1/a1.jpg", images[0].ThumbUrl);
        Assert.Equal((1920, 1080), (images[0].Width, images[0].Height));
        Assert.Equal(images[1].Url, images[1].ThumbUrl); // untrusted thumbnail host: falls back to the picture itself
        Assert.Empty(Wallhaven.ParseImages("not json"));
    }
}
