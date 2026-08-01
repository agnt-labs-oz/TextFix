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
        // Apostrophes are ubiquitous in English, so a single-quote unwrap can never be
        // told apart from a contraction. We do not attempt it.
        var raw = "'I can't believe it worked'";
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
}
