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
}
