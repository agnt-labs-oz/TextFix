namespace TextFix.Models;

public record StatsAggregate
{
    public int LifetimeCorrections { get; init; }
    public double TimeSavedMinutes { get; init; }
    public Dictionary<string, int> PerMode { get; init; } = new();
    public decimal MonthCostUsd { get; init; }

    public string? MostUsedMode =>
        PerMode.Count == 0 ? null : PerMode.OrderByDescending(kv => kv.Value).First().Key;
}
