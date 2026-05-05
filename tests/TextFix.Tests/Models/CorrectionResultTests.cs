using TextFix.Models;

namespace TextFix.Tests.Models;

public class CorrectionResultTests
{
    [Fact]
    public void Model_DefaultsToEmptyString()
    {
        var result = new CorrectionResult { OriginalText = "a", CorrectedText = "b" };
        Assert.Equal("", result.Model);
    }

    [Fact]
    public void Model_RoundTripsThroughInit()
    {
        var result = new CorrectionResult
        {
            OriginalText = "a",
            CorrectedText = "b",
            Model = "claude-haiku-4-5-20251001",
        };
        Assert.Equal("claude-haiku-4-5-20251001", result.Model);
    }
}
