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

    [Theory]
    [InlineData("<text>hello world</text>", "hello world")]
    [InlineData("<text>hello world", "hello world")]        // truncated at the token limit
    [InlineData("hello world</text>", "hello world")]
    [InlineData("<result>hello world</result>", "hello world")] // the Anthropic prefill tag
    [InlineData("<TEXT>hello world</TEXT>", "hello world")]     // models vary the casing
    public void Strip_RemovesWrapperTags(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_KeepsWrapperTags_WhenTheUserWasCorrectingXml()
    {
        // The user selected an XML fragment that genuinely contains a <text> element.
        // Those tags are their content, and deleting them would corrupt the document —
        // the one thing this class must never do.
        const string original = "<text>teh quick brown fox</text>";
        const string raw = "<text>the quick brown fox</text>";

        Assert.Equal(raw, ResponseSanitizer.Strip(raw, original));
    }

    [Fact]
    public void Strip_LeavesInnerTagsAlone()
    {
        // Only a wrapper enclosing the whole response is scaffolding.
        const string raw = "Set the <text> element before the </text> closing tag.";

        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
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
