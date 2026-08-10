using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class AnthropicProviderTests
{
    private static AnthropicProvider Make(string key = "sk-ant-test-key") =>
        new(key, "claude-haiku-4-5-20251001", timeoutSeconds: 10);

    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Make(""));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_Succeeds_WithApiKey()
    {
        Assert.NotNull(Make());
    }

    [Fact]
    public void ProviderId_IsAnthropic()
    {
        Assert.Equal(ProviderPresets.AnthropicId, Make().ProviderId);
    }

    [Fact]
    public void IsLocal_IsFalse()
    {
        Assert.False(Make().IsLocal);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextIsEmpty()
    {
        var result = await Make().CorrectAsync("", "Fix grammar.");

        Assert.True(result.IsError);
        Assert.Contains("empty", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var result = await Make().CorrectAsync(new string('a', 5001), "Fix grammar.");

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsKnownModelsWithoutNetwork()
    {
        var models = await Make().ListModelsAsync();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.StartsWith("claude-", m));
    }
}
