using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LiteLauncher.Core.Apps;

/// <summary>An app from the Windows Start menu (the same list Windows shows in "All apps").</summary>
public sealed record StartApp(string Name, string AppId);

/// <summary>Reads the apps that appear in the Start menu and opens them. Uses only what Windows itself offers.</summary>
public static class StartApps
{
    public const string SourceName = "Apps";

    /// <summary>Turns the JSON written by <c>Get-StartApps | ConvertTo-Json</c> into a sorted list (one item comes back as a bare object).</summary>
    public static IReadOnlyList<StartApp> Parse(string json)
    {
        var apps = new Dictionary<string, StartApp>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<StartApp>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };

            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var name = item.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var id = item.TryGetProperty("AppID", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                apps[id.Trim()] = new StartApp(name.Trim(), id.Trim());
            }
        }
        catch (JsonException)
        {
            return Array.Empty<StartApp>();
        }

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Lower case, no accents, punctuation turned into single spaces: "AMD Software: Adrenalin" -> "amd software adrenalin".</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Keeps the apps whose name starts with one of the wanted names, in the order of the wanted list.</summary>
    public static IReadOnlyList<StartApp> Filter(IEnumerable<StartApp> apps, IEnumerable<string> wanted)
    {
        var list = apps.Select(a => (App: a, Key: Normalize(a.Name))).ToList();
        var result = new List<StartApp>();
        foreach (var want in wanted.Select(Normalize).Where(w => w.Length > 0))
        {
            var found = list.FirstOrDefault(x => (x.Key == want || x.Key.StartsWith(want + " ", StringComparison.Ordinal)) && !result.Contains(x.App)).App;
            if (found is not null) result.Add(found);
        }
        return result;
    }

    /// <summary>Asks Windows for the Start menu apps (takes a second or two: run it off the UI thread).</summary>
    public static async Task<IReadOnlyList<StartApp>> ReadAsync(CancellationToken cancel = default)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress");

        try
        {
            using var process = Process.Start(info);
            if (process is null) return Array.Empty<StartApp>();
            var output = await process.StandardOutput.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            return Parse(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return Array.Empty<StartApp>();
        }
    }

    /// <summary>Opens an app the way the Start menu does.</summary>
    public static void Open(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("No app id.", nameof(appId));
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        info.ArgumentList.Add(@"shell:AppsFolder\" + appId);
        Process.Start(info)?.Dispose();
    }
}
