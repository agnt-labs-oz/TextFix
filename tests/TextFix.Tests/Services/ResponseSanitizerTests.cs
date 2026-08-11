using TextFix.Services;

namespace TextFix.Tests.Services;

public class ResponseSanitizerTests
{
    [Theory]
    [InlineData("The quick brown fox", "The quick brown fox")]
    [InlineData("Sure! Here's the corrected text:\nThe quick brown fox", "The quick brown fox")]
    [InlineData("Here is the corrected version:\n\nThe quick brown fox", "The quick brown fox")]
    [InlineData("Corrected text:\nThe quick brown fox", "The quick brown fox")]
    public void Strip_RemovesLeadIn(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Theory]
    [InlineData("```\nThe quick brown fox\n```", "The quick brown fox")]
    [InlineData("```text\nThe quick brown fox\n```", "The quick brown fox")]
    public void Strip_RemovesCodeFences(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Theory]
    [InlineData("\"The quick brown fox\"", "The quick brown fox")]
    [InlineData("“The quick brown fox”", "The quick brown fox")]
    public void Strip_RemovesWrappingQuotes(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesInternalQuotes_WhenTextIsNotWrapped()
    {
        var raw = "He said \"hello\" to her";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_LeavesQuotesAlone_WhenWrappedTextContainsInternalQuotes()
    {
        // Starts AND ends with a quote, so it reaches the nested-quote guard. Ambiguous
        // between "wrapped text containing a quotation" and two adjacent quoted phrases,
        // so the conservative answer is to change nothing. This test exists to reach the
        // guard — without a wrapping quote character the guard is never evaluated at all.
        var raw = "\"He said \"hello\" to her\"";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_DoesNotUnwrapSingleQuotes()
    {
        // No internal apostrophe, so the nested-quote guard cannot be what saves this:
        // it passes only if the single-quote pair is genuinely absent from `pairs`.
        var raw = "'hello'";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesMultilineBody()
    {
        var raw = "Sure, here you go:\nLine one\nLine two";
        Assert.Equal("Line one\nLine two", ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesLabelLinesThatAreNotChatter()
    {
        var raw = "Result:\nfile1.txt\nfile2.txt";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_HandlesEmptyAndWhitespace()
    {
        Assert.Equal("", ResponseSanitizer.Strip(""));
        Assert.Equal("", ResponseSanitizer.Strip("   \n  "));
    }

    [Theory]
    [InlineData("I'm unable to help with that")]
    [InlineData("I cannot process this request")]
    [InlineData("Sorry, the input is unclear")]
    [InlineData("Unfortunately this text cannot be corrected")]
    public void LooksConversational_DetectsRefusals(string text)
    {
        Assert.True(ResponseSanitizer.LooksConversational(text));
    }

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    [InlineData("Please review the attached document.")]
    [InlineData("")]
    public void LooksConversational_AllowsNormalText(string text)
    {
        Assert.False(ResponseSanitizer.LooksConversational(text));
    }

    [Fact]
    public void Strip_RemovesTheTextWrapperTheModelCopiedFromThePrompt()
    {
        // Verbatim from the first real Ollama correction: llama3.2:3b handed back the
        // <text> delimiters that PromptTemplates.UserMessage puts around the input.
        const string raw = "<text>\nThe quick brown fox jumps over the lazy dog\n</text>";

        Assert.Equal(
            "The quick brown fox jumps over the lazy dog",
            ResponseSanitizer.Strip(raw, "teh quick brown fox jumpd over teh lazy dog"));
    }

    [Fact]
    public void Strip_RemovesTheClosingTag_WhenTheModelTrailsSomethingAfterIt()
    {
        // Verbatim from a live run. An earlier fix only unwrapped a tag pair enclosing
        // the whole response, and this slipped through: the model put an emoji after the
        // closing tag, so the string no longer ended with it.
        const string raw = "The quick brown fox jumps over the lazy dog\n</text> 🙂";

        Assert.Equal(
            "The quick brown fox jumps over the lazy dog\n 🙂",
            ResponseSanitizer.Strip(raw, "teh quick brown fox jumpd over teh lazy dog"));
    }

    [Theory]
    [InlineData("<text>hello world</text>", "hello world")]
    [InlineData("<text>hello world", "hello world")]        // truncated at the token limit
    [InlineData("hello world</text>", "hello world")]
    [InlineData("<result>hello world</result>", "hello world")] // the Anthropic prefill tag
    [InlineData("<TEXT>hello world</TEXT>", "hello world")]     // models vary the casing
    [InlineData("<text>hello</text> world", "hello world")]     // tag mid-response
    public void Strip_RemovesWrapperTags(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Theory]
    // The user selected content that genuinely uses the tag — an XML fragment, or prose
    // about XML. Those tags are theirs, and deleting them would corrupt the document.
    [InlineData("<text>teh quick brown fox</text>", "<text>the quick brown fox</text>")]
    [InlineData("Set the <text> elemnt before </text>.", "Set the <text> element before </text>.")]
    public void Strip_KeepsWrapperTags_WhenTheUserWasCorrectingXml(string original, string raw)
    {
        Assert.Equal(raw, ResponseSanitizer.Strip(raw, original));
    }

    [Fact]
    public void Strip_RemovesWrapperInsideCodeFences()
    {
        // A model can do both at once; fences are stripped first, so the tag beneath
        // must still be caught.
        const string raw = "```\n<text>\nhello world\n</text>\n```";

        Assert.Equal("hello world", ResponseSanitizer.Strip(raw));
    }
}
