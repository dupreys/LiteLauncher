using System.Security.Cryptography;
using System.Text;

namespace LiteLauncher.Core.Settings;

/// <summary>
/// Keeps secrets (API keys) unreadable in settings.json. On Windows they are encrypted with DPAPI for the current
/// user, so the file is useless when copied to another PC or read by another account. Elsewhere (tests, Linux) the
/// value stays as it is.
/// Stored form: "dpapi:" + Base64. A value without the prefix is an older plain-text one and is still accepted.
/// </summary>
public static class SecretProtector
{
    public const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LiteLauncher.settings.v1");

    public static bool IsProtected(string? stored) => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The text to write to the file.</summary>
    public static string? Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || IsProtected(secret)) return secret;
        if (!OperatingSystem.IsWindows()) return secret;

        try
        {
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(data);
        }
        catch (CryptographicException)
        {
            return secret; // better a readable key than a lost one
        }
    }

    /// <summary>The secret from the stored text. Null when it cannot be decrypted (another user or PC): enter it again.</summary>
    public static string? Unprotect(string? stored)
    {
        if (!IsProtected(stored)) return stored;
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var data = Convert.FromBase64String(stored![Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
