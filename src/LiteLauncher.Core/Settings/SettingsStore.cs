using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiteLauncher.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>JSON names of the settings that hold secrets (stored encrypted, see <see cref="SecretProtector"/>).</summary>
    private static readonly string[] SecretFields = { nameof(AppSettings.SteamWebApiKey), nameof(AppSettings.SteamGridDbApiKey) };

    public SettingsStore(string? filePath = null) => FilePath = filePath ?? DefaultFilePath;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLauncher");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "settings.json");

    public string FilePath { get; }

    /// <summary>Loads settings; a missing or corrupt file yields defaults instead of an error.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();

            var node = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
            if (node is null) return new AppSettings();

            var legacy = false;
            foreach (var field in SecretFields)
            {
                if (node[field] is not JsonValue value || !value.TryGetValue<string>(out var stored)) continue;
                legacy |= !string.IsNullOrEmpty(stored) && !SecretProtector.IsProtected(stored);
                node[field] = SecretProtector.Unprotect(stored);
            }

            var settings = node.Deserialize<AppSettings>() ?? new AppSettings();
            settings.Normalize();

            // Keys saved by an older version were plain text: encrypt them right away.
            if (legacy && OperatingSystem.IsWindows())
            {
                try { Save(settings); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes atomically (temp file + move) so a crash can't leave a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = FilePath + ".tmp";
        var node = JsonSerializer.SerializeToNode(settings, Options) as JsonObject ?? new JsonObject();
        foreach (var field in SecretFields)
        {
            if (node[field] is JsonValue value && value.TryGetValue<string>(out var secret))
                node[field] = SecretProtector.Protect(secret);
        }

        File.WriteAllText(temp, node.ToJsonString(Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
