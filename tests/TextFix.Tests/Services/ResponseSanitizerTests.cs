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
    public void Strip_PreservesInternalQuotes()
    {
        // Only balanced *wrapping* quotes go. A quoted phrase inside the text stays.
        var raw = "He said \"hello\" to her";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesMultilineBody()
    {
        var raw = "Sure, here you go:\nLine one\nLine two";
        Assert.Equal("Line one\nLine two", ResponseSanitizer.Strip(raw));
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
