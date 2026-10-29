using LiteLauncher.Core.Apps;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class StartAppsTests
{
    [Fact]
    public void Parses_a_list_sorted_by_name()
    {
        var apps = StartApps.Parse("""[{"Name":"Zeta","AppID":"z.exe"},{"Name":"alpha","AppID":"{GUID}\\a.exe"}]""");
        Assert.Equal(new[] { "alpha", "Zeta" }, apps.Select(a => a.Name));
    }

    [Fact]
    public void A_single_app_comes_back_as_an_object()
    {
        var apps = StartApps.Parse("""{"Name":"Notepad","AppID":"Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"}""");
        Assert.Single(apps);
        Assert.Equal("Notepad", apps[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""[{"Name":"","AppID":"x"},{"Name":"y"}]""")]
    public void Bad_or_empty_input_gives_no_apps(string json) => Assert.Empty(StartApps.Parse(json));

    [Fact]
    public void The_same_id_is_listed_once()
    {
        var apps = StartApps.Parse("""[{"Name":"A","AppID":"x"},{"Name":"A2","AppID":"X"}]""");
        Assert.Single(apps);
    }

    [Fact]
    public void Filter_matches_names_ignoring_case_accents_and_punctuation()
    {
        var all = new[]
        {
            new StartApp("AMD Software: Adrenalin Edition", "a"),
            new StartApp("Configurações", "b"),
            new StartApp("Configurações do Discord", "c"),
            new StartApp("Notepad", "d"),
            new StartApp("Explorador de Arquivos", "e"),
        };
        var shown = StartApps.Filter(all, new[] { "AMD software adrenalin", "configuracoes", "Explorador de arquivos", "Nada" });
        Assert.Equal(new[] { "a", "b", "e" }, shown.Select(a => a.AppId));
    }
}
