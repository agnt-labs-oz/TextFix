using TextFix.Models;
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ProviderFactoryTests
{
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
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };

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
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };

        Assert.NotNull(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_CachesInstance_WhenConfigUnchanged()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);

        Assert.Same(factory.Create(), factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenModelChanges()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.GetProviderConfig(ProviderPresets.OllamaId).Model = "qwen2.5:7b";

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenActiveProviderChanges()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.ActiveProviderId = ProviderPresets.CustomId;
        settings.GetProviderConfig(ProviderPresets.CustomId).BaseUrl = "http://localhost:1234/v1";

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
