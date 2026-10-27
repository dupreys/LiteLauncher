using System.Globalization;

namespace LiteLauncher.Core;

/// <summary>
/// Interface language. English texts are the keys: <see cref="T"/> returns the translation for the current language,
/// or the English text itself when there is none, so a missing translation never breaks the screen.
/// </summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"),
        ("pt-BR", "Português (Brasil)"),
        ("es", "Español"),
    };

    private static IReadOnlyDictionary<string, string>? _table;

    public static string Language { get; private set; } = "en";

    public static event Action? Changed;

    public static string NameOf(string code) =>
        Languages.FirstOrDefault(l => l.Code == code).Name ?? Languages[0].Name;

    /// <summary>Picks a supported language for the system culture (Portuguese and Spanish variants map to ours).</summary>
    public static string Detect(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "pt" => "pt-BR",
        "es" => "es",
        _ => "en",
    };

    /// <summary>Switches language; unknown codes fall back to English.</summary>
    public static void Set(string? code)
    {
        var chosen = Languages.Any(l => l.Code == code) ? code! : "en";
        var changed = chosen != Language;
        Language = chosen;
        _table = chosen switch
        {
            "pt-BR" => Translations.Portuguese,
            "es" => Translations.Spanish,
            _ => null,
        };
        if (changed) Changed?.Invoke();
    }

    public static string T(string english) =>
        _table is not null && _table.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>Translated text with {0}, {1}… filled in.</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>The next language in the list (used by the Settings button).</summary>
    public static string Next(string current)
    {
        var index = Array.FindIndex(Languages, l => l.Code == current);
        return Languages[(index + 1) % Languages.Length].Code;
    }
}
