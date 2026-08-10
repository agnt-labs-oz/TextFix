using TextFix.Models;
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ProviderFactoryTests
{
    /// <summary>
    /// Ollama's preset carries no default model — it depends entirely on what the user has
    /// pulled — so a bare config is deliberately unusable and Create() returns null for it.
    /// Tests that want a working local provider have to say which model.
    /// </summary>
    private static AppSettings OllamaSettings(string model = "llama3.2:3b")
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        settings.GetProviderConfig(ProviderPresets.OllamaId).Model = model;
        return settings;
    }

    [Fact]
    public void Create_ReturnsAnthropicProvider_ForAnthropicId()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.AnthropicId };
        settings.GetProviderConfig(ProviderPresets.AnthropicId).SetApiKey("sk-ant-test");

        Assert.IsType<AnthropicProvider>(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_ReturnsOpenAiCompatible_ForOllama()
    {
        var settings = OllamaSettings();

        Assert.IsType<OpenAiCompatibleProvider>(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_ReturnsNull_WhenRequiredKeyMissing()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OpenAiId };

        Assert.Null(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_SucceedsWithoutKey_ForLocalProvider()
    {
        var settings = OllamaSettings();

        Assert.NotNull(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_CachesInstance_WhenConfigUnchanged()
    {
        var settings = OllamaSettings();
        var factory = new ProviderFactory(settings);

        Assert.Same(factory.Create(), factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenModelChanges()
    {
        var settings = OllamaSettings();
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.GetProviderConfig(ProviderPresets.OllamaId).Model = "qwen2.5:7b"; // was llama3.2:3b

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenActiveProviderChanges()
    {
        var settings = OllamaSettings();
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.ActiveProviderId = ProviderPresets.CustomId;
        var custom = settings.GetProviderConfig(ProviderPresets.CustomId);
        custom.BaseUrl = "http://localhost:1234/v1";
        custom.Model = "local-model";

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenApiKeyRotatedToSameLengthValue()
    {
        // Regression: a length-only cache key would return the cached provider still
        // holding the old credential. API keys have fixed-length formats, so a rotation
        // of identical length is the normal case, not an edge case.
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.AnthropicId };
        var config = settings.GetProviderConfig(ProviderPresets.AnthropicId);
        config.SetApiKey("sk-ant-aaaaaaaaaaaaaaaa");
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        config.SetApiKey("sk-ant-bbbbbbbbbbbbbbbb"); // same length, different value

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_ReturnsNewInstance_AfterInvalidate_EvenWhenConfigUnchanged()
    {
        var settings = OllamaSettings();
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        factory.Invalidate();

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_ReturnsNull_WhenOpenAiCompatibleBaseUrlIsEmpty()
    {
        // Custom ships with no preset base URL. Reachable from the tray and overlay
        // switchers, which have no validation in front of them — unlike Settings.
        // Without this guard the request died inside SendAsync as an unhelpful
        // "An unexpected error occurred."
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.CustomId };
        settings.GetProviderConfig(ProviderPresets.CustomId).Model = "local-model";

        Assert.Null(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_ReturnsNull_WhenModelIsEmptyAndPresetHasNoDefault()
    {
        // Ollama with nothing pulled and nothing chosen would have sent "model": "".
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };

        Assert.Null(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_UsesPresetDefaults_WhenConfigIsBlank()
    {
        // The two guards above must not fire for a preset that supplies its own defaults —
        // OpenAI has both a base URL and a default model, so a key is all it needs.
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OpenAiId };
        settings.GetProviderConfig(ProviderPresets.OpenAiId).SetApiKey("sk-test");

        Assert.IsType<OpenAiCompatibleProvider>(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_UnknownProviderId_FallsBackToAnthropic()
    {
        // Note the key goes on the *anthropic* config, not "bogus": Get("bogus")
        // resolves to the Anthropic preset, and the factory looks the config up by
        // preset.Id. A fallback to a keyless provider would correctly return null.
        var settings = new AppSettings { ActiveProviderId = "bogus" };
        settings.GetProviderConfig(ProviderPresets.AnthropicId).SetApiKey("sk-ant-test");

        Assert.IsType<AnthropicProvider>(new ProviderFactory(settings).Create());
    }
}
