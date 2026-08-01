using System.Security.Cryptography;
using System.Text;

namespace TextFix.Services;

/// <summary>
/// DPAPI string protection, scoped to the current Windows user. Extracted from
/// AppSettings so per-provider configs can reuse it instead of duplicating the
/// try/catch.
/// </summary>
public static class DpapiString
{
    /// <summary>Returns a base64 DPAPI blob, or "" for empty input.</summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to encrypt API key with DPAPI. Cannot store key securely.", ex);
        }
    }

    /// <summary>Returns "" when the blob is empty, corrupt, or from another user.</summary>
    public static string Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";

        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
        }
    }
}
