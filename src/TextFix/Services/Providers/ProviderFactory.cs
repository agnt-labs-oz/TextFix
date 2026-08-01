using System.Security.Cryptography;
using System.Text;
using TextFix.Models;

namespace TextFix.Services.Providers;

/// <summary>
/// Builds the active provider from settings, caching the instance until the
/// configuration that produced it changes.
/// </summary>
public class ProviderFactory(AppSettings settings)
{
    private IAiProvider? _cached;
    private string? _cacheKey;

    /// <summary>
    /// Returns null when the provider needs a key and none is configured — the caller
    /// treats that the same way it treats a missing Anthropic key today.
    /// </summary>
    public IAiProvider? Create()
    {
        var preset = ProviderPresets.Get(settings.ActiveProviderId);
        var config = settings.GetProviderConfig(preset.Id);
        var apiKey = config.GetApiKey();

        if (preset.Key == KeyRequirement.Required && string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Hash rather than store. Two things matter here:
        //  - The key's VALUE must participate, not just its length: API keys have
        //    fixed-length formats, so a rotated key is almost always the same length,
        //    and a length-only cache key would keep serving a provider holding the
        //    revoked credential.
        //  - The digest, not the key, is what persists — a cache key outlives the
        //    call and is visible in debugger views, so the raw secret must not sit in it.
        // Hashing a NUL-separated tuple also removes any delimiter-collision risk from
        // user-entered BaseUrl/Model values on the Custom preset.
        var separator = (char)0;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{preset.Id}{separator}{config.BaseUrl}{separator}{config.Model}{separator}{apiKey}")));
        if (_cached is not null && _cacheKey == key) return _cached;

        _cached = preset.IsOpenAiCompatible
            ? new OpenAiCompatibleProvider(preset, config.BaseUrl, config.Model, apiKey)
            : new AnthropicProvider(apiKey, config.Model, preset.TimeoutSeconds);
        _cacheKey = key;
        return _cached;
    }

    /// <summary>Forces a rebuild on the next Create call.</summary>
    public void Invalidate() => _cacheKey = null;
}
