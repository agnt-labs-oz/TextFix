using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ProviderPresetsTests
{
    [Fact]
    public void All_ContainsFourProviders()
    {
        Assert.Equal(4, ProviderPresets.All.Count);
    }

    [Fact]
    public void All_IdsAreUnique()
    {
        var ids = ProviderPresets.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void All_HaveDisplayNames()
    {
        Assert.All(ProviderPresets.All, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
    }

    [Fact]
    public void Get_ReturnsMatchingPreset()
    {
        Assert.Equal(ProviderPresets.OllamaId, ProviderPresets.Get("ollama").Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-provider")]
    public void Get_UnknownId_FallsBackToAnthropic(string? id)
    {
        // A hand-edited settings file or a downgrade must not crash the app.
        Assert.Equal(ProviderPresets.AnthropicId, ProviderPresets.Get(id).Id);
    }

    [Fact]
    public void Anthropic_RequiresKeyAndIsNotOpenAiCompatible()
    {
        var p = ProviderPresets.Get(ProviderPresets.AnthropicId);
        Assert.Equal(KeyRequirement.Required, p.Key);
        Assert.False(p.IsOpenAiCompatible);
    }

    [Fact]
    public void Ollama_NeedsNoKeyAndHasLongTimeout()
    {
        var p = ProviderPresets.Get(ProviderPresets.OllamaId);
        Assert.Equal(KeyRequirement.None, p.Key);
        Assert.True(p.TimeoutSeconds >= 120, "cold model load can take 10-20s before first token");
        Assert.Equal("http://localhost:11434/v1", p.BaseUrl);
    }

    [Fact]
    public void OpenAi_UsesMaxCompletionTokens()
    {
        // Verified against OpenAI's API reference 2026-08-01: max_tokens is deprecated
        // and is rejected outright by o-series models.
        Assert.Equal("max_completion_tokens", ProviderPresets.Get(ProviderPresets.OpenAiId).TokenParam);
    }

    [Fact]
    public void LocalPresets_UseMaxTokens()
    {
        // Ollama and llama.cpp only understand max_tokens.
        Assert.Equal("max_tokens", ProviderPresets.Get(ProviderPresets.OllamaId).TokenParam);
        Assert.Equal("max_tokens", ProviderPresets.Get(ProviderPresets.CustomId).TokenParam);
    }

    [Fact]
    public void OpenAiCompatiblePresets_HaveTokenParam()
    {
        Assert.All(
            ProviderPresets.All.Where(p => p.IsOpenAiCompatible),
            p => Assert.False(string.IsNullOrWhiteSpace(p.TokenParam)));
    }

    // Pins every field of every preset. The named tests above document *why* the
    // load-bearing values are what they are; this one catches silent drift in the
    // ones no named test covers — dropping the "/v1" suffix from OpenAI's base URL
    // would break every OpenAI call while leaving the rest of the suite green.
    // Adding a provider means adding a row here too, which is the intended coupling.
    [Theory]
    [InlineData("anthropic", "Anthropic", "", KeyRequirement.Required,
        "claude-haiku-4-5-20251001", 10, "", false)]
    [InlineData("ollama", "Ollama (local)", "http://localhost:11434/v1", KeyRequirement.None,
        "", 120, "max_tokens", true)]
    [InlineData("openai", "OpenAI", "https://api.openai.com/v1", KeyRequirement.Required,
        "gpt-4o-mini", 30, "max_completion_tokens", true)]
    [InlineData("custom", "Custom (OpenAI-compatible)", "", KeyRequirement.Optional,
        "", 120, "max_tokens", true)]
    public void Preset_HasExactSpecifiedValues(
        string id, string displayName, string baseUrl, KeyRequirement key,
        string defaultModel, int timeoutSeconds, string tokenParam, bool isOpenAiCompatible)
    {
        var p = ProviderPresets.Get(id);

        Assert.Equal(id, p.Id);
        Assert.Equal(displayName, p.DisplayName);
        Assert.Equal(baseUrl, p.BaseUrl);
        Assert.Equal(key, p.Key);
        Assert.Equal(defaultModel, p.DefaultModel);
        Assert.Equal(timeoutSeconds, p.TimeoutSeconds);
        Assert.Equal(tokenParam, p.TokenParam);
        Assert.Equal(isOpenAiCompatible, p.IsOpenAiCompatible);
    }
}
