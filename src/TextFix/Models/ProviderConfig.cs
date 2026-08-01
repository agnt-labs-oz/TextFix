using System.Text.Json.Serialization;
using TextFix.Services;

namespace TextFix.Models;

/// <summary>
/// What one provider remembers. Each provider keeps its own model and key so
/// switching away and back is lossless.
/// </summary>
public class ProviderConfig
{
    public string Id { get; set; } = "";

    /// <summary>Empty means "use the preset's base URL".</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Empty means "use the preset's default model, or the first model returned by
    /// ListModelsAsync if the preset has no default" (Ollama's case).
    /// </summary>
    public string Model { get; set; } = "";

    public string EncryptedApiKey { get; set; } = "";

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrEmpty(EncryptedApiKey);

    public string GetApiKey() => DpapiString.Unprotect(EncryptedApiKey);

    public void SetApiKey(string? plainKey) => EncryptedApiKey = DpapiString.Protect(plainKey);
}
