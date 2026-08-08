using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextFix.Services;
using TextFix.Services.Providers;

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
    public string LogLevel { get; set; } = "Warn";

    /// <summary>
    /// Maximum number of recent corrections to keep in history.
    /// Default 10 — small for privacy and to stop the panel growing unbounded.
    /// CorrectionHistory enforces a hard ceiling (<see cref="CorrectionHistory.MaxItemsCap"/>).
    /// </summary>
    public int HistoryMaxItems { get; set; } = 10;

    public string ActiveModeName { get; set; } = "Fix errors";

    public List<CorrectionMode> CustomModes { get; set; } = [];

    /// <summary>Which provider corrections currently run against.</summary>
    public string ActiveProviderId { get; set; } = ProviderPresets.AnthropicId;

    /// <summary>Per-provider model and key. Populated lazily by GetProviderConfig.</summary>
    public List<ProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Returns the config for <paramref name="id"/>, creating and storing an empty one
    /// on first use. Always returns the same instance for the same id, so callers can
    /// mutate the result directly.
    /// </summary>
    public ProviderConfig GetProviderConfig(string id)
    {
        var existing = Providers.FirstOrDefault(
            p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var created = new ProviderConfig { Id = id };
        Providers.Add(created);
        return created;
    }

    /// <summary>
    /// The config the active provider actually uses. Resolves through
    /// <see cref="ProviderPresets.Get"/> exactly as <c>ProviderFactory.Create</c> does —
    /// a hand-edited ActiveProviderId of "groq" must land on the Anthropic config that
    /// corrections really run from, not create and persist a stray "groq" entry.
    /// </summary>
    [JsonIgnore]
    public ProviderConfig ActiveProvider =>
        GetProviderConfig(ProviderPresets.Get(ActiveProviderId).Id);

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
        var fromEncrypted = DpapiString.Unprotect(EncryptedApiKey);
        // Fall back to the legacy plaintext key during migration.
        return string.IsNullOrEmpty(fromEncrypted) ? ApiKey : fromEncrypted;
    }

    public void SetApiKey(string plainKey)
    {
        EncryptedApiKey = DpapiString.Protect(plainKey);
        ApiKey = ""; // Never re-persist the legacy plaintext field.
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

            // Settings files written by builds before HistoryMaxItems existed deserialise
            // the missing key as 0, which would clamp the in-memory history to a single
            // entry on next load. Restore the documented default for any non-positive value.
            if (settings.HistoryMaxItems < 1)
                settings.HistoryMaxItems = 10;

            // Migrate plaintext key to encrypted
            if (!string.IsNullOrEmpty(settings.ApiKey) && string.IsNullOrEmpty(settings.EncryptedApiKey))
            {
                // Zero the in-memory plaintext copy before SetApiKey re-stores the encrypted form,
                // so even an unexpected mid-migration save can't re-persist the legacy field.
                var legacy = settings.ApiKey;
                settings.ApiKey = "";
                settings.SetApiKey(legacy);
                await settings.SaveAsync(path);
            }

            // Migrate the single top-level key+model onto the anthropic provider config.
            // Guarded on "no anthropic entry" rather than "Providers is empty" so a
            // second load cannot append a duplicate.
            //
            // KNOWN LIMITATION — read this before adding a "remove provider" feature.
            // This guard cannot distinguish "never migrated" from "migrated, then the
            // user deleted the anthropic entry". Because the top-level EncryptedApiKey
            // and Model fields are kept populated for old-build compatibility, deleting
            // the anthropic provider would cause the next load to silently recreate it
            // from those fields — resurrecting a credential the user removed on purpose.
            // Unreachable today (nothing removes a provider). Whoever adds removal must
            // introduce a schema-version or migration-completed marker at that point.
            var hasAnthropic = settings.Providers.Any(
                p => string.Equals(p.Id, ProviderPresets.AnthropicId, StringComparison.OrdinalIgnoreCase));
            if (!hasAnthropic)
            {
                var anthropic = settings.GetProviderConfig(ProviderPresets.AnthropicId);
                anthropic.EncryptedApiKey = settings.EncryptedApiKey;
                anthropic.Model = settings.Model;
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
