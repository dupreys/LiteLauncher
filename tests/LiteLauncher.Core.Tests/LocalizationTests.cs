using System.Globalization;
using System.Text.RegularExpressions;
using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class LocalizationTests
{

    public static TheoryData<string> Codes => new() { "pt-BR", "es" };

    private static IReadOnlyDictionary<string, string> Table(string code) =>
        code == "pt-BR" ? Translations.Portuguese : Translations.Spanish;

    [Fact]
    public void Both_languages_translate_the_same_texts()
    {
        Assert.Equal(Translations.Portuguese.Keys.OrderBy(k => k), Translations.Spanish.Keys.OrderBy(k => k));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Translations_keep_their_placeholders(string code)
    {
        foreach (var (english, translated) in Table(code))
        {
            var want = Regex.Matches(english, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
            var got = Regex.Matches(translated, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
            Assert.True(want.SequenceEqual(got), $"{code}: placeholders differ for \"{english}\"");
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{code}: empty translation for \"{english}\"");
        }
    }

    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("es", "es")]
    [InlineData("en", "en")]
    [InlineData("fr", "en")]
    public void Detect_maps_the_system_language(string culture, string expected) =>
        Assert.Equal(expected, Loc.Detect(new CultureInfo(culture)));

    [Fact]
    public void Next_cycles_through_every_language()
    {
        var code = "en";
        var seen = new List<string>();
        for (var i = 0; i < Loc.Languages.Length; i++) { seen.Add(code); code = Loc.Next(code); }
        Assert.Equal("en", code);
        Assert.Equal(Loc.Languages.Length, seen.Distinct().Count());
    }
}
