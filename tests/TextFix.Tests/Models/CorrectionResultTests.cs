using TextFix.Models;

namespace TextFix.Tests.Models;

public class CorrectionResultTests
{
    [Fact]
    public void HasChanges_True_WhenTextsDiffer()
    {
        var result = new CorrectionResult
        {
            OriginalText = "teh cat",
            CorrectedText = "the cat",
        };
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void HasChanges_False_WhenTextsMatch()
    {
        var result = new CorrectionResult
        {
            OriginalText = "hello",
            CorrectedText = "hello",
        };
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void IsError_False_WhenNoErrorMessage()
    {
        var result = new CorrectionResult
        {
            OriginalText = "hi",
            CorrectedText = "hi",
        };
        Assert.False(result.IsError);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ErrorFactory_SetsErrorMessage_And_HasChangesIsFalse()
    {
        var result = CorrectionResult.Error("original", "something went wrong");

        Assert.True(result.IsError);
        Assert.Equal("something went wrong", result.ErrorMessage);
        Assert.Equal("original", result.OriginalText);
        Assert.Equal("original", result.CorrectedText);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void ErrorFactory_WithEmptyOriginal()
    {
        var result = CorrectionResult.Error("", "error");

        Assert.True(result.IsError);
        Assert.False(result.HasChanges);
        Assert.Equal("", result.OriginalText);
    }
}
