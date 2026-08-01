using System.Net;
using System.Net.Sockets;
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class OpenAiCompatibleProviderTests
{
    private const string OkBody = """
    {
      "choices": [ { "message": { "role": "assistant", "content": "The quick brown fox" } } ],
      "usage": { "prompt_tokens": 42, "completion_tokens": 7 }
    }
    """;

    private static OpenAiCompatibleProvider Make(HttpMessageHandler handler, string? presetId = null)
    {
        var preset = ProviderPresets.Get(presetId ?? ProviderPresets.OllamaId);
        return new OpenAiCompatibleProvider(preset, preset.BaseUrl, "llama3.2:3b", apiKey: "", handler);
    }

    [Fact]
    public async Task CorrectAsync_HappyPath_ReturnsCorrectedTextAndTokens()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, OkBody));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        Assert.False(result.IsError);
        Assert.Equal("The quick brown fox", result.CorrectedText);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.True(result.IsLocal);
    }

    [Fact]
    public async Task CorrectAsync_StripsChattyPreamble()
    {
        const string chatty = """
        { "choices": [ { "message": { "content": "Sure! Here's the corrected text:\nThe quick brown fox" } } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, chatty));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        Assert.Equal("The quick brown fox", result.CorrectedText);
    }

    [Fact]
    public async Task CorrectAsync_FlagsConversationalOutputWithoutErroring()
    {
        const string refusal = """
        { "choices": [ { "message": { "content": "I cannot help with that request." } } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, refusal));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        // Shown with a warning banner, not swallowed as an error.
        Assert.False(result.IsError);
        Assert.True(result.LooksConversational);
    }

    [Fact]
    public async Task CorrectAsync_UsesPresetTokenParam()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        await Make(handler, ProviderPresets.OpenAiId).CorrectAsync("hi there", "Fix errors.");

        Assert.Contains("max_completion_tokens", handler.LastRequestBody!);
        Assert.DoesNotContain("\"max_tokens\"", handler.LastRequestBody!);
    }

    [Fact]
    public async Task CorrectAsync_OmitsAuthHeader_WhenNoKey()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        await Make(handler).CorrectAsync("hi there", "Fix errors.");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task CorrectAsync_SendsBearerToken_WhenKeyPresent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        var preset = ProviderPresets.Get(ProviderPresets.OpenAiId);
        var provider = new OpenAiCompatibleProvider(preset, preset.BaseUrl, "gpt-4o-mini", "sk-test", handler);

        await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CorrectAsync_ReadsAlternateUsageFieldNames()
    {
        const string altUsage = """
        {
          "choices": [ { "message": { "content": "ok" } } ],
          "usage": { "input_tokens": 11, "output_tokens": 3 }
        }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, altUsage));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Equal(11, result.InputTokens);
        Assert.Equal(3, result.OutputTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.Forbidden, "API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited")]
    [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "unavailable")]
    public async Task CorrectAsync_MapsStatusCodesToFriendlyMessages(HttpStatusCode status, string expected)
    {
        var provider = Make(new StubHttpMessageHandler(status, "{}"));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains(expected, result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ModelNotFound_SuggestsOllamaPull()
    {
        const string body = """{ "error": { "message": "model 'llama3.2:3b' not found" } }""";
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.NotFound, body));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("ollama pull llama3.2:3b", result.ErrorMessage!);
    }

    [Fact]
    public async Task CorrectAsync_NotFoundWithoutModelHint_SuggestsCheckingBaseUrl()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.NotFound, "not found"));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Contains("/v1", result.ErrorMessage!);
    }

    [Fact]
    public async Task CorrectAsync_ConnectionRefused_NamesTheHost()
    {
        var refused = new HttpRequestException("refused", new SocketException(10061));
        var provider = Make(new StubHttpMessageHandler(refused));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        // The generic "check your connection" would be actively misleading here.
        Assert.Contains("localhost:11434", result.ErrorMessage!);
        Assert.Contains("running", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_EmptyChoices_ReturnsError()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, """{ "choices": [] }"""));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CorrectAsync_UserCancellation_ReportsCancelled()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var task = Make(handler).CorrectAsync("hi there", "Fix errors.", cts.Token);
        await cts.CancelAsync();

        var result = await task;

        Assert.True(result.IsError);
        Assert.Contains("cancelled", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_Timeout_MentionsModelLoading()
    {
        // Preset timeout of 1s against a handler that takes 5s.
        var preset = ProviderPresets.Get(ProviderPresets.OllamaId) with { TimeoutSeconds = 1 };
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody, TimeSpan.FromSeconds(5));
        var provider = new OpenAiCompatibleProvider(preset, preset.BaseUrl, "llama3.2:3b", "", handler);

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("Timed out", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListModelsAsync_ParsesDataIds()
    {
        const string body = """
        { "data": [ { "id": "llama3.2:3b" }, { "id": "qwen2.5:7b" } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        var models = await provider.ListModelsAsync();

        Assert.Equal(["llama3.2:3b", "qwen2.5:7b"], models);
    }

    [Fact]
    public async Task ListModelsAsync_Throws_OnConnectionRefused()
    {
        // Unlike CorrectAsync, this surfaces the failure so Test Connection can report it.
        var refused = new HttpRequestException("refused", new SocketException(10061));
        var provider = Make(new StubHttpMessageHandler(refused));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.ListModelsAsync());
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, OkBody));

        var result = await provider.CorrectAsync(new string('a', 5001), "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
