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
        var actual = (double)CostEstimator.Estimate(model, inTokens, outTokens, isLocal: false);
        Assert.Equal(expectedUsd, actual, 4);
    }

    [Fact]
    public void Estimate_UnknownModel_FallsBackToSonnetRate()
    {
        // Mid-range fallback so unknown models neither under- nor over-estimate wildly.
        var cost = CostEstimator.Estimate("some-future-model", 1_000_000, 1_000_000, isLocal: false);
        Assert.Equal(18.0m, cost);
    }

    [Fact]
    public void Estimate_ZeroTokens_ReturnsZero()
    {
        Assert.Equal(0m, CostEstimator.Estimate("claude-haiku-4-5-20251001", 0, 0, isLocal: false));
    }

    [Fact]
    public void Estimate_LocalModel_IsAlwaysFree()
    {
        // Local inference costs nothing. Without this, the mid-range fallback would
        // bill an Ollama run at Claude Sonnet rates.
        var cost = CostEstimator.Estimate("llama3.2:3b", 1_000_000, 1_000_000, isLocal: true);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_LocalFlagBeatsKnownModelName()
    {
        // A local server can serve a model whose name collides with a cloud one.
        var cost = CostEstimator.Estimate("claude-opus-4-6", 1_000_000, 1_000_000, isLocal: true);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_KnownOpenAiModel_UsesItsOwnRate()
    {
        // Must not fall back to the Sonnet mid-range rate.
        var cost = CostEstimator.Estimate("gpt-4o-mini", 1_000_000, 1_000_000, isLocal: false);
        Assert.True(cost > 0m);
        Assert.True(cost < 18.0m, "gpt-4o-mini must not be priced at the Sonnet fallback rate");
    }

    // The 3-argument overload that used to live here defaulted isLocal to false, and a
    // call site in CorrectionHistory kept using it after the flag was added — so local
    // corrections still accrued cost. The overload is gone; isLocal is now required so
    // the compiler finds every call site. Do not reintroduce a defaulting overload.
}
