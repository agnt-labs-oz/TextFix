using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ApiErrorBodyTests
{
    [Fact]
    public void ExtractMessage_ReadsNestedShape_UsedByOpenAiAndAnthropic()
    {
        const string body = """
        { "type": "error", "error": { "type": "invalid_request_error", "message": "Your credit balance is too low to access the Anthropic API." } }
        """;

        Assert.Equal(
            "Your credit balance is too low to access the Anthropic API.",
            ApiErrorBody.ExtractMessage(body));
    }

    [Fact]
    public void ExtractMessage_ReadsFlatShape_UsedByOllamaAndLlamaCpp()
    {
        const string body = """{ "error": "model 'llama3.2:3b' not found, try pulling it first" }""";

        Assert.Equal("model 'llama3.2:3b' not found, try pulling it first", ApiErrorBody.ExtractMessage(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]                             // no error member
    [InlineData("""{ "error": null }""")]          // present but null
    [InlineData("""{ "error": { "code": 42 } }""")] // object without a message
    [InlineData("""{ "error": { "message": "" } }""")] // message present but blank
    [InlineData("[1,2,3]")]                        // valid JSON, wrong root kind
    [InlineData("<html><body>502 Bad Gateway</body></html>")] // proxy error page
    [InlineData("not json at all")]
    public void ExtractMessage_ReturnsNull_WhenThereIsNothingQuotable(string body)
    {
        // Null means "the caller keeps its own wording" — an HTML page must never be
        // pasted into the overlay as if it were an explanation.
        Assert.Null(ApiErrorBody.ExtractMessage(body));
    }

    [Fact]
    public void ExtractMessage_TruncatesRunawayMessages()
    {
        var body = $$"""{ "error": { "message": "{{new string('x', 5000)}}" } }""";

        var result = ApiErrorBody.ExtractMessage(body)!;

        Assert.Equal(ApiErrorBody.MaxLength + 1, result.Length); // + the ellipsis
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Truncate_LeavesShortTextExactlyAsIs()
    {
        Assert.Equal("short", ApiErrorBody.Truncate("short"));
    }
}
