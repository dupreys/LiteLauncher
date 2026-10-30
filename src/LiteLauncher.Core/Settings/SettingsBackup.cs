using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiteLauncher.Core.Settings;

/// <summary>
/// Export and import of the preferences as one JSON file. API keys are never written to a backup (they stay on the PC
/// they were typed on), and importing never touches the keys already stored here.
/// </summary>
public static class SettingsBackup
{
    public const string Format = "LiteLauncher-settings";
    public const int Version = 1;
    public const int MaxBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly string[] Excluded =
    {
        nameof(AppSettings.SteamWebApiKey), nameof(AppSettings.SteamGridDbApiKey),
        nameof(AppSettings.LastUpdateCheck),
        nameof(AppSettings.LastSeenVersion),
    };

    public static string Export(AppSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, Options) as JsonObject ?? new JsonObject();
        foreach (var name in Excluded) node.Remove(name);
        var envelope = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["exportedAt"] = DateTimeOffset.Now.ToString("O"),
            ["settings"] = node,
        };
        return envelope.ToJsonString(Options);
    }

    /// <summary>Reads a backup. Null when the text is not a LiteLauncher backup (or is too big or damaged).</summary>
    public static AppSettings? Import(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;
            if (root["format"]?.GetValue<string>() != Format) return null;
            if (root["version"] is not JsonValue v || !v.TryGetValue<int>(out var version) || version is < 1 or > Version) return null;
            if (root["settings"] is not JsonObject settingsNode) return null;

            foreach (var name in Excluded) settingsNode.Remove(name); // even a hand-edited file cannot bring keys in
            var imported = settingsNode.Deserialize<AppSettings>();
            imported?.Normalize();
            return imported;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Copies every preference from <paramref name="source"/> into <paramref name="target"/>, except the excluded ones.</summary>
    public static void ApplyTo(AppSettings target, AppSettings source)
    {
        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || Excluded.Contains(property.Name)) continue;
            property.SetValue(target, property.GetValue(source));
        }
        target.Normalize();
    }
}
