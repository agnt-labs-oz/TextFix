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

        // Key length rather than the key itself, to keep secrets out of the cache key.
        var key = string.Join('|', preset.Id, config.BaseUrl, config.Model, apiKey.Length);
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
