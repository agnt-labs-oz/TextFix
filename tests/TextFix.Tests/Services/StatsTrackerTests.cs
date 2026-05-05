using System.IO;
using TextFix.Models;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class StatsTrackerTests : IDisposable
{
    private readonly string _path;

    public StatsTrackerTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"TextFixStatsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "stats.jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RecordAsync_AppendsOneJsonLine()
    {
        var tracker = new StatsTracker(_path);
        await tracker.RecordAsync(new CorrectionResult
        {
            OriginalText = "hello wrld",
            CorrectedText = "hello world",
            ModeName = "Fix errors",
            Model = "claude-haiku-4-5-20251001",
            InputTokens = 100, OutputTokens = 50,
        });

        var lines = await File.ReadAllLinesAsync(_path);
        Assert.Single(lines);
        Assert.Contains("\"mode\":\"Fix errors\"", lines[0]);
        Assert.Contains("\"chars_in\":10", lines[0]);
        Assert.Contains("\"chars_out\":11", lines[0]);
    }

    [Fact]
    public async Task RecordAsync_SkipsErrors()
    {
        var tracker = new StatsTracker(_path);
        await tracker.RecordAsync(CorrectionResult.Error("a", "boom"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task RecordAsync_SkipsNoChanges()
    {
        var tracker = new StatsTracker(_path);
        await tracker.RecordAsync(new CorrectionResult { OriginalText = "same", CorrectedText = "same" });
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task AggregateAsync_ComputesLifetimeAndPerMode()
    {
        var tracker = new StatsTracker(_path);
        await tracker.RecordAsync(MakeResult("Fix errors", 100, 50, "claude-haiku-4-5-20251001"));
        await tracker.RecordAsync(MakeResult("Fix errors", 200, 100, "claude-haiku-4-5-20251001"));
        await tracker.RecordAsync(MakeResult("Concise", 50, 25, "claude-haiku-4-5-20251001"));

        var agg = await tracker.AggregateAsync();

        Assert.Equal(3, agg.LifetimeCorrections);
        Assert.Equal(2, agg.PerMode["Fix errors"]);
        Assert.Equal(1, agg.PerMode["Concise"]);
    }

    [Fact]
    public async Task AggregateAsync_TimeSavedUsesCharsInAnd200Cpm()
    {
        var tracker = new StatsTracker(_path);
        // 200 chars at 200 cpm = 1.0 minute
        await tracker.RecordAsync(MakeResult("Fix errors", 100, 50, "claude-haiku-4-5-20251001",
            originalLen: 200, correctedLen: 200));
        var agg = await tracker.AggregateAsync();
        Assert.Equal(1.0, agg.TimeSavedMinutes, 2);
    }

    [Fact]
    public async Task AggregateAsync_MonthCostSumsCurrentUtcMonthOnly()
    {
        // Write a line dated last month directly, plus a fresh recorded line for this month.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lastMonth = DateTime.UtcNow.AddMonths(-1).ToString("o");
        await File.AppendAllLinesAsync(_path, new[]
        {
            $"{{\"timestamp\":\"{lastMonth}\",\"mode\":\"Fix errors\",\"provider\":\"anthropic\",\"model\":\"claude-haiku-4-5-20251001\",\"chars_in\":100,\"chars_out\":100,\"tokens_in\":1000000,\"tokens_out\":0,\"cost_estimate\":1.0,\"status\":\"success\"}}",
        });

        var tracker = new StatsTracker(_path);
        await tracker.RecordAsync(MakeResult("Fix errors", 1_000_000, 0, "claude-haiku-4-5-20251001"));

        var agg = await tracker.AggregateAsync();
        Assert.Equal(1.0m, agg.MonthCostUsd); // last month excluded
    }

    [Fact]
    public async Task AggregateAsync_EmptyFile_ReturnsZeros()
    {
        var tracker = new StatsTracker(_path);
        var agg = await tracker.AggregateAsync();
        Assert.Equal(0, agg.LifetimeCorrections);
        Assert.Empty(agg.PerMode);
        Assert.Equal(0m, agg.MonthCostUsd);
        Assert.Equal(0.0, agg.TimeSavedMinutes);
    }

    private static CorrectionResult MakeResult(string mode, int inTokens, int outTokens, string model,
        int originalLen = 10, int correctedLen = 10) =>
        new()
        {
            OriginalText = new string('a', originalLen),
            CorrectedText = new string('b', correctedLen),
            ModeName = mode,
            Model = model,
            InputTokens = inTokens,
            OutputTokens = outTokens,
        };
}
