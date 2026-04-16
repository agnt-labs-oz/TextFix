using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextFix.Models;

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Encrypted API key (base64-encoded DPAPI blob). Use GetApiKey/SetApiKey for plaintext access.
    /// </summary>
    public string EncryptedApiKey { get; set; } = "";

    /// <summary>
    /// Legacy plaintext key — read during migration, never written.
    /// </summary>
    public string ApiKey { get; set; } = "";

    public string Hotkey { get; set; } = "Ctrl+Shift+Z";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";

    public string SystemPrompt { get; set; } =
        "Fix all typos, spelling, and grammar errors in the following text. Return only the corrected text with no explanation. Preserve the original meaning, tone, and formatting.";

    public int OverlayAutoApplySeconds { get; set; } = 3;
    public bool KeepOverlayOpen { get; set; }
    public bool StartWithWindows { get; set; }

    public string ActiveModeName { get; set; } = "Fix errors";

    public CorrectionMode GetActiveMode()
    {
        return CorrectionMode.Defaults.FirstOrDefault(m => m.Name == ActiveModeName)
            ?? CorrectionMode.Defaults[0];
    }

    [JsonIgnore]
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix",
            "settings.json");

    public string GetApiKey()
    {
        // Prefer encrypted key
        if (!string.IsNullOrEmpty(EncryptedApiKey))
        {
            try
            {
                var encrypted = Convert.FromBase64String(EncryptedApiKey);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        // Fall back to legacy plaintext key (migration)
        return ApiKey;
    }

    public void SetApiKey(string plainKey)
    {
        if (string.IsNullOrEmpty(plainKey))
        {
            EncryptedApiKey = "";
            ApiKey = "";
            return;
        }

        try
        {
            var plain = Encoding.UTF8.GetBytes(plainKey);
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            EncryptedApiKey = Convert.ToBase64String(encrypted);
            ApiKey = ""; // Clear legacy plaintext
        }
        catch
        {
            // Fallback: store plaintext if DPAPI fails (shouldn't happen on Windows)
            ApiKey = plainKey;
        }
    }

    public async Task SaveAsync(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public static async Task<AppSettings> LoadAsync(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Migrate plaintext key to encrypted
            if (!string.IsNullOrEmpty(settings.ApiKey) && string.IsNullOrEmpty(settings.EncryptedApiKey))
            {
                settings.SetApiKey(settings.ApiKey);
                await settings.SaveAsync(path);
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }
}
