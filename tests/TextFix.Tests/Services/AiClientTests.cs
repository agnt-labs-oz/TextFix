using TextFix.Models;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class AiClientTests
{
    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var settings = new AppSettings { ApiKey = "" };

        var ex = Assert.Throws<InvalidOperationException>(() => new AiClient(settings));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_Succeeds_WithApiKey()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };

        var client = new AiClient(settings);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextIsEmpty()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };
        var client = new AiClient(settings);

        var result = await client.CorrectAsync("");

        Assert.True(result.IsError);
        Assert.Contains("empty", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };
        var client = new AiClient(settings);
        var longText = new string('a', 5001);

        var result = await client.CorrectAsync(longText);

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
