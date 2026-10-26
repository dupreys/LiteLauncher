using LiteLauncher.Core.Steam;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_objects_and_values()
    {
        const string text = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "UserConfig"
                {
                    "language" "english"
                }
            }
            """;

        var root = VdfParser.Parse(text);
        var state = root["AppState"];

        Assert.NotNull(state);
        Assert.Equal("730", state!.GetString("appid"));
        Assert.Equal("Counter-Strike 2", state.GetString("name"));
        Assert.Equal("english", state["UserConfig"]!.GetString("language"));
    }

    [Fact]
    public void Unescapes_windows_paths()
    {
        var root = VdfParser.Parse("\"path\" \"D:\\\\SteamLibrary\"");
        Assert.Equal(@"D:\SteamLibrary", root.GetString("path"));
    }

    [Fact]
    public void Ignores_comments_and_is_case_insensitive()
    {
        var root = VdfParser.Parse("// comment\n\"Key\" \"value\"");
        Assert.Equal("value", root.GetString("key"));
    }

    [Fact]
    public void Tolerates_truncated_input()
    {
        var root = VdfParser.Parse("\"AppState\" { \"appid\" \"1\" ");
        Assert.Equal("1", root["AppState"]!.GetString("appid"));
    }
}
