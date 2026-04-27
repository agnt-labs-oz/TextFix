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

    public int OverlayAutoApplySeconds { get; set; } = 3;
    public bool ManualApplyOnly { get; set; }
    public bool StartWithWindows { get; set; }

    public string ActiveModeName { get; set; } = "Fix errors";

    public List<CorrectionMode> CustomModes { get; set; } = [];

    // Persisted overlay bounds for the result/diff view. null = unset (use defaults / position near cursor).
    public double? OverlayWidth { get; set; }
    public double? OverlayHeight { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    public CorrectionMode GetActiveMode()
    {
        return CorrectionMode.Defaults.FirstOrDefault(m => m.Name == ActiveModeName)
            ?? CustomModes.FirstOrDefault(m => m.Name == ActiveModeName)
            ?? CorrectionMode.Defaults[0];
    }

    public IReadOnlyList<CorrectionMode> AllModes()
    {
        var list = new List<CorrectionMode>(CorrectionMode.Defaults);
        list.AddRange(CustomModes);
        return list;
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
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to encrypt API key with DPAPI. Cannot store key securely.", ex);
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
