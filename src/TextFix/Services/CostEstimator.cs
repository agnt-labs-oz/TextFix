namespace TextFix.Services;

/// <summary>
/// Per-model USD cost estimation. Rates are approximate published prices and may drift over time.
/// </summary>
public static class CostEstimator
{
    private record Rate(decimal InputPerMillion, decimal OutputPerMillion);

    // Source: https://www.anthropic.com/pricing — refresh when the model list changes.
    private static readonly Dictionary<string, Rate> Rates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5-20251001"] = new(1m, 5m),
        ["claude-sonnet-4-5-20250929"] = new(3m, 15m),
        ["claude-sonnet-4-6"] = new(3m, 15m),
        ["claude-opus-4-6"] = new(15m, 75m),

        // Source: https://openai.com/api/pricing — refresh when the model list changes.
        ["gpt-4o-mini"] = new(0.15m, 0.60m),
        ["gpt-4o"] = new(2.50m, 10m),
    };

    // Mid-range fallback so unknown/future models neither under- nor over-estimate wildly.
    private static readonly Rate Fallback = new(3m, 15m);

    public static decimal Estimate(string model, int inputTokens, int outputTokens) =>
        Estimate(model, inputTokens, outputTokens, isLocal: false);

    /// <summary>
    /// Local inference is free regardless of model name — the flag wins over any
    /// rate-table match, since a local server can serve a cloud model's name.
    /// </summary>
    public static decimal Estimate(string model, int inputTokens, int outputTokens, bool isLocal)
    {
        if (isLocal) return 0m;

        var rate = Rates.GetValueOrDefault(model ?? "", Fallback);
        return inputTokens * rate.InputPerMillion / 1_000_000m
             + outputTokens * rate.OutputPerMillion / 1_000_000m;
    }
}
