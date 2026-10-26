using System.Text;

namespace LiteLauncher.Core.Steam;

/// <summary>A node of a Valve KeyValues (VDF/ACF) document.</summary>
public sealed class VdfNode
{
    /// <summary>The string value, or null when this node is an object.</summary>
    public string? Value { get; set; }

    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.TryGetValue(key, out var node) ? node : null;

    public string? GetString(string key) => this[key]?.Value;

    public long GetLong(string key, long fallback = 0) =>
        long.TryParse(GetString(key), out var n) ? n : fallback;
}

/// <summary>
/// Minimal, forgiving parser for the text KeyValues format used by
/// libraryfolders.vdf and appmanifest_*.acf. Binary VDF is not supported.
/// </summary>
public static class VdfParser
{
    private enum Tok { Text, Open, Close, End }

    public static VdfNode Parse(string text)
    {
        var root = new VdfNode();
        var pos = 0;
        ReadObject(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ReadObject(string s, ref int pos, VdfNode node, bool topLevel)
    {
        while (true)
        {
            var key = Next(s, ref pos, out var kind);
            switch (kind)
            {
                case Tok.End:
                    return;
                case Tok.Close:
                    if (topLevel) continue; // stray brace, ignore
                    return;
                case Tok.Open:
                    continue; // stray brace, ignore
            }

            var value = Next(s, ref pos, out var valueKind);
            switch (valueKind)
            {
                case Tok.Open:
                    var child = new VdfNode();
                    ReadObject(s, ref pos, child, topLevel: false);
                    node.Children[key] = child;
                    break;
                case Tok.Text:
                    node.Children[key] = new VdfNode { Value = value };
                    break;
                case Tok.Close:
                    if (!topLevel) return;
                    break;
                default:
                    return;
            }
        }
    }

    private static string Next(string s, ref int pos, out Tok kind)
    {
        // Skip whitespace and // comments.
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) { pos++; continue; }
            if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }

        if (pos >= s.Length) { kind = Tok.End; return ""; }

        var c = s[pos];
        if (c == '{') { pos++; kind = Tok.Open; return ""; }
        if (c == '}') { pos++; kind = Tok.Close; return ""; }

        var sb = new StringBuilder();
        if (c == '"')
        {
            pos++;
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length)
                {
                    pos++;
                    sb.Append(s[pos] switch { 'n' => '\n', 't' => '\t', var other => other });
                }
                else
                {
                    sb.Append(s[pos]);
                }
                pos++;
            }
            pos++; // closing quote
        }
        else
        {
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}')
                sb.Append(s[pos++]);
        }

        kind = Tok.Text;
        return sb.ToString();
    }
}
