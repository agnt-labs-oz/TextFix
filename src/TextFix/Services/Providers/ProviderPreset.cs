namespace TextFix.Services.Providers;

public enum KeyRequirement
{
    /// <summary>Local providers. No Authorization header is sent at all.</summary>
    None,
    /// <summary>Custom endpoints — some need a key, some do not.</summary>
    Optional,
    Required,
}

/// <summary>
/// Everything that varies between providers. Adding a provider should be a new row
/// here and nothing else.
/// </summary>
/// <param name="BaseUrl">Empty for Anthropic, which goes through its SDK.</param>
/// <param name="TokenParam">
/// The JSON field naming the output-token cap. OpenAI deprecated <c>max_tokens</c> in
/// favour of <c>max_completion_tokens</c> and rejects the old name on o-series models;
/// Ollama and llama.cpp understand only <c>max_tokens</c>. Empty for Anthropic.
/// </param>
public record ProviderPreset(
    string Id,
    string DisplayName,
    string BaseUrl,
    KeyRequirement Key,
    string DefaultModel,
    int TimeoutSeconds,
    string TokenParam,
    bool IsOpenAiCompatible);
