using TextFix.Services;

namespace TextFix.Tests.Services;

public class CostEstimatorTests
{
    [Theory]
    // Haiku 4.5: $1/M in, $5/M out
    [InlineData("claude-haiku-4-5-20251001", 1_000_000, 1_000_000, 6.0)]
    // Sonnet 4.5: $3/M in, $15/M out
    [InlineData("claude-sonnet-4-5-20250929", 1_000_000, 1_000_000, 18.0)]
    // Sonnet 4.6: $3/M in, $15/M out
    [InlineData("claude-sonnet-4-6", 1_000_000, 1_000_000, 18.0)]
    // Opus 4.6: $15/M in, $75/M out
    [InlineData("claude-opus-4-6", 1_000_000, 1_000_000, 90.0)]
    public void Estimate_ReturnsExpectedCost(string model, int inTokens, int outTokens, double expectedUsd)
    {
        var actual = (double)CostEstimator.Estimate(model, inTokens, outTokens);
        Assert.Equal(expectedUsd, actual, 4);
    }

    [Fact]
    public void Estimate_UnknownModel_FallsBackToSonnetRate()
    {
        // Mid-range fallback so unknown models neither under- nor over-estimate wildly.
        var cost = CostEstimator.Estimate("some-future-model", 1_000_000, 1_000_000);
        Assert.Equal(18.0m, cost);
    }

    [Fact]
    public void Estimate_ZeroTokens_ReturnsZero()
    {
        Assert.Equal(0m, CostEstimator.Estimate("claude-haiku-4-5-20251001", 0, 0));
    }
}
