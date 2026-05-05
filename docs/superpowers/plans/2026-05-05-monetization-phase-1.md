# Monetization Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the validation surfaces from the monetization spec — local stats panel, structured logging, in-app discovery of feedback channels and the Ko-fi tip jar, plus a polished README — without introducing telemetry, license keys, or paid features.

**Architecture:** Three new services (`CostEstimator`, `AppLog`, `StatsTracker`) live alongside the existing services. `CorrectionResult` gains a `Model` field so cost can be computed accurately per-correction. `StatsTracker` appends one JSONL line per successful correction to `%APPDATA%/TextFix/stats.jsonl`; the new `AboutWindow` aggregates that file on open to render lifetime count, time saved, per-mode breakdown, and this-month spend. `AppLog` replaces the ad-hoc `LogError`/`LogDebug` helpers in `App.xaml.cs` with a single daily-rolling logger. The tray menu gains five new items pointing at the new About window, the Ko-fi URL, the log folder, and pre-filled GitHub Issues / Discussions URLs.

**Tech Stack:** .NET 10 / C# / WPF / WinForms (existing); no new NuGet packages; xUnit for tests.

---

## File Structure

| File | Role |
|------|------|
| `src/TextFix/Services/CostEstimator.cs` | **new** — `CostEstimator.Estimate(model, inputTokens, outputTokens)` returns `decimal` cost in USD using a per-model rate table |
| `src/TextFix/Services/AppLog.cs` | **new** — daily-rolling logger at `%APPDATA%/TextFix/logs/textfix-YYYY-MM-DD.log`; `Info`/`Warn`/`Error` levels; 7-day retention on first write each day |
| `src/TextFix/Services/StatsTracker.cs` | **new** — `RecordAsync(result)` appends JSONL; `AggregateAsync()` returns a `StatsAggregate` |
| `src/TextFix/Models/StatsAggregate.cs` | **new** — `LifetimeCorrections`, `TimeSavedMinutes`, `PerMode` (`Dictionary<string,int>`), `MonthCostUsd` |
| `src/TextFix/Models/CorrectionResult.cs` | add `Model` property |
| `src/TextFix/Models/CorrectionHistory.cs` | replace hardcoded Haiku rates with `CostEstimator` lookup |
| `src/TextFix/Models/AppSettings.cs` | add `LogLevel` (default `"Warn"`) |
| `src/TextFix/Services/AiClient.cs` | set `Model` on returned `CorrectionResult` |
| `src/TextFix/Services/CorrectionService.cs` | inject `StatsTracker`, call `RecordAsync` on `CorrectionCompleted` |
| `src/TextFix/Views/AboutWindow.xaml` | **new** — dark-themed window matching `SettingsWindow.xaml`, stats panel, Ko-fi CTA |
| `src/TextFix/Views/AboutWindow.xaml.cs` | **new** — code-behind, reads `StatsAggregate`, opens Ko-fi/GitHub URLs |
| `src/TextFix/App.xaml.cs` | construct new services; add five tray menu items; wire About window; replace `LogError`/`LogDebug` call sites with `AppLog` |
| `tests/TextFix.Tests/Services/CostEstimatorTests.cs` | **new** |
| `tests/TextFix.Tests/Services/AppLogTests.cs` | **new** |
| `tests/TextFix.Tests/Services/StatsTrackerTests.cs` | **new** |
| `tests/TextFix.Tests/Models/CorrectionResultTests.cs` | **new** (covers `Model` round-trip) |
| `tests/TextFix.Tests/Models/AppSettingsTests.cs` | extend with `LogLevel` default test |
| `README.md` | full rewrite per spec — hero screenshot, walkthrough, Privacy section, Support section |
| `.github/ISSUE_TEMPLATE/bug_report.yml` | **new** — structured bug report form |
| `.github/DISCUSSION_TEMPLATE/ideas.yml` | **new** — feature suggestion form (used by the in-app "Suggest a feature" button via URL pre-fill) |

---

## Task 1: Add `Model` to `CorrectionResult`

**Files:**
- Modify: `src/TextFix/Models/CorrectionResult.cs`
- Create: `tests/TextFix.Tests/Models/CorrectionResultTests.cs`

- [ ] **Step 1: Write failing test for `Model` property**

Create `tests/TextFix.Tests/Models/CorrectionResultTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test --filter FullyQualifiedName~CorrectionResultTests
```

Expected: build error `'CorrectionResult' does not contain a definition for 'Model'`.

- [ ] **Step 3: Add `Model` property**

In `src/TextFix/Models/CorrectionResult.cs`, between the existing `ModeName` line and `InputTokens` line, add:

```csharp
    public string Model { get; init; } = "";
```

- [ ] **Step 4: Re-run test, expect PASS**

```
dotnet test --filter FullyQualifiedName~CorrectionResultTests
```

Expected: 2 passed.

- [ ] **Step 5: Run the full suite to confirm no regressions**

```
dotnet test
```

Expected: 25 passed (23 existing + 2 new).

- [ ] **Step 6: Commit**

```
git add src/TextFix/Models/CorrectionResult.cs tests/TextFix.Tests/Models/CorrectionResultTests.cs
git commit -m "feat(model): add Model field to CorrectionResult"
```

---

## Task 2: `CostEstimator` service

**Files:**
- Create: `src/TextFix/Services/CostEstimator.cs`
- Create: `tests/TextFix.Tests/Services/CostEstimatorTests.cs`

- [ ] **Step 1: Write failing tests for known-model rates**

Create `tests/TextFix.Tests/Services/CostEstimatorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test --filter FullyQualifiedName~CostEstimatorTests
```

Expected: build error `The type or namespace 'CostEstimator' could not be found`.

- [ ] **Step 3: Implement `CostEstimator`**

Create `src/TextFix/Services/CostEstimator.cs`:

```csharp
namespace TextFix.Services;

/// <summary>
/// Per-model USD cost estimation. Rates are approximate published prices and may drift over time.
/// </summary>
public static class CostEstimator
{
    private record Rate(decimal InputPerMillion, decimal OutputPerMillion);

    private static readonly Dictionary<string, Rate> Rates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5-20251001"] = new(1m, 5m),
        ["claude-sonnet-4-5-20250929"] = new(3m, 15m),
        ["claude-sonnet-4-6"] = new(3m, 15m),
        ["claude-opus-4-6"] = new(15m, 75m),
    };

    private static readonly Rate Fallback = new(3m, 15m);

    public static decimal Estimate(string model, int inputTokens, int outputTokens)
    {
        var rate = Rates.GetValueOrDefault(model ?? "", Fallback);
        return inputTokens * rate.InputPerMillion / 1_000_000m
             + outputTokens * rate.OutputPerMillion / 1_000_000m;
    }
}
```

- [ ] **Step 4: Re-run tests, expect PASS**

```
dotnet test --filter FullyQualifiedName~CostEstimatorTests
```

Expected: 6 passed.

- [ ] **Step 5: Commit**

```
git add src/TextFix/Services/CostEstimator.cs tests/TextFix.Tests/Services/CostEstimatorTests.cs
git commit -m "feat(cost): add per-model cost estimator with Haiku/Sonnet/Opus rates"
```

---

## Task 3: Make `CorrectionHistory` use `CostEstimator`

**Why:** `CorrectionHistory` currently hardcodes Haiku rates, which under-bills Sonnet and Opus. Now that `CorrectionResult` has `Model` and `CostEstimator` exists, route through it.

**Files:**
- Modify: `src/TextFix/Models/CorrectionHistory.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`

- [ ] **Step 1: Add a failing test that distinguishes Haiku vs Opus cost**

Append to `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`:

```csharp
[Fact]
public void SessionCost_UsesPerModelRates()
{
    var history = new CorrectionHistory();
    history.Add(new CorrectionResult
    {
        OriginalText = "a", CorrectedText = "b",
        InputTokens = 1_000_000, OutputTokens = 0,
        Model = "claude-haiku-4-5-20251001",
    });
    history.Add(new CorrectionResult
    {
        OriginalText = "c", CorrectedText = "d",
        InputTokens = 1_000_000, OutputTokens = 0,
        Model = "claude-opus-4-6",
    });
    // Haiku $1 + Opus $15 = $16
    Assert.Equal(16m, history.SessionCost);
}
```

- [ ] **Step 2: Run test — expect failure**

```
dotnet test --filter FullyQualifiedName~CorrectionHistoryTests.SessionCost_UsesPerModelRates
```

Expected: assert fail (the existing implementation will return roughly $1.6 because both calls use Haiku rates).

- [ ] **Step 3: Replace hardcoded rates with `CostEstimator` call**

In `src/TextFix/Models/CorrectionHistory.cs`:

Remove these two lines:

```csharp
    // Haiku pricing: $0.80/M input, $4.00/M output
    private const decimal InputCostPerToken = 0.80m / 1_000_000m;
    private const decimal OutputCostPerToken = 4.00m / 1_000_000m;
```

In the `Add` method, replace the `SessionCost +=` line:

```csharp
        SessionCost += result.InputTokens * InputCostPerToken
                     + result.OutputTokens * OutputCostPerToken;
```

with:

```csharp
        SessionCost += TextFix.Services.CostEstimator.Estimate(
            result.Model, result.InputTokens, result.OutputTokens);
```

- [ ] **Step 4: Re-run failing test, expect PASS**

```
dotnet test --filter FullyQualifiedName~CorrectionHistoryTests.SessionCost_UsesPerModelRates
```

Expected: 1 passed.

- [ ] **Step 5: Run full suite to confirm no regressions**

```
dotnet test
```

Expected: all tests pass. Note that the existing `SessionCost_SumsTokenCosts` test only asserts `> 0`, so it still passes despite using the fallback rate (no `Model` set).

- [ ] **Step 6: Commit**

```
git add src/TextFix/Models/CorrectionHistory.cs tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
git commit -m "refactor(history): route SessionCost through CostEstimator"
```

---

## Task 4: `AppLog` service with daily rolling

**Files:**
- Create: `src/TextFix/Services/AppLog.cs`
- Create: `tests/TextFix.Tests/Services/AppLogTests.cs`

- [ ] **Step 1: Write failing tests for level filtering and file naming**

Create `tests/TextFix.Tests/Services/AppLogTests.cs`:

```csharp
using System.IO;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class AppLogTests : IDisposable
{
    private readonly string _logDir;

    public AppLogTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), $"TextFixLogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Write_CreatesFileNamedByDate()
    {
        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("hello");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Info_AtWarnLevel_DoesNotWrite()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);
        log.Info("ignored");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.Now:yyyy-MM-dd}.log");
        Assert.False(File.Exists(expected));
    }

    [Fact]
    public void Warn_AtWarnLevel_Writes()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);
        log.Warn("kept");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.Now:yyyy-MM-dd}.log");
        var contents = File.ReadAllText(expected);
        Assert.Contains("[WARN]", contents);
        Assert.Contains("kept", contents);
    }

    [Fact]
    public void Error_WritesStackTraceWhenExceptionProvided()
    {
        var log = new AppLog(_logDir, AppLog.Level.Info);
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { log.Error("context", ex); }

        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.Now:yyyy-MM-dd}.log"));
        Assert.Contains("[ERROR]", contents);
        Assert.Contains("context", contents);
        Assert.Contains("InvalidOperationException", contents);
    }

    [Fact]
    public void Cleanup_DeletesFilesOlderThan7Days()
    {
        var oldDate = DateTime.Now.AddDays(-10);
        var oldFile = Path.Combine(_logDir, $"textfix-{oldDate:yyyy-MM-dd}.log");
        File.WriteAllText(oldFile, "ancient");

        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("today");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void Cleanup_KeepsFilesWithinLast7Days()
    {
        var recentDate = DateTime.Now.AddDays(-3);
        var recentFile = Path.Combine(_logDir, $"textfix-{recentDate:yyyy-MM-dd}.log");
        File.WriteAllText(recentFile, "recent");
        File.SetLastWriteTime(recentFile, recentDate);

        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("today");

        Assert.True(File.Exists(recentFile));
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test --filter FullyQualifiedName~AppLogTests
```

Expected: build error `The type or namespace 'AppLog' could not be found`.

- [ ] **Step 3: Implement `AppLog`**

Create `src/TextFix/Services/AppLog.cs`:

```csharp
using System.IO;

namespace TextFix.Services;

public sealed class AppLog
{
    public enum Level { Info, Warn, Error }

    private readonly string _dir;
    private readonly Level _minLevel;
    private readonly object _gate = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public AppLog(string directory, Level minLevel)
    {
        _dir = directory;
        _minLevel = minLevel;
    }

    public string LogDirectory => _dir;

    public void Info(string message) => Write(Level.Info, message, null);
    public void Warn(string message) => Write(Level.Warn, message, null);
    public void Error(string message, Exception? ex = null) => Write(Level.Error, message, ex);

    private void Write(Level level, string message, Exception? ex)
    {
        if (level < _minLevel) return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_dir);

                var today = DateTime.Now.Date;
                if (_lastCleanupDate != today)
                {
                    CleanupOldLogs();
                    _lastCleanupDate = today;
                }

                var path = Path.Combine(_dir, $"textfix-{today:yyyy-MM-dd}.log");
                var line = $"[{DateTime.UtcNow:o}] [{level.ToString().ToUpperInvariant()}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + ex;
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(_dir, "textfix-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
            catch { /* best effort */ }
        }
    }
}
```

- [ ] **Step 4: Re-run tests, expect all PASS**

```
dotnet test --filter FullyQualifiedName~AppLogTests
```

Expected: 6 passed.

- [ ] **Step 5: Commit**

```
git add src/TextFix/Services/AppLog.cs tests/TextFix.Tests/Services/AppLogTests.cs
git commit -m "feat(log): add daily-rolling AppLog with level filtering and 7-day retention"
```

---

## Task 5: Add `LogLevel` setting

**Files:**
- Modify: `src/TextFix/Models/AppSettings.cs`
- Modify: `tests/TextFix.Tests/Models/AppSettingsTests.cs`

- [ ] **Step 1: Write failing test for default**

Append to `tests/TextFix.Tests/Models/AppSettingsTests.cs`:

```csharp
[Fact]
public void LogLevel_DefaultsToWarn()
{
    var settings = new AppSettings();
    Assert.Equal("Warn", settings.LogLevel);
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test --filter FullyQualifiedName~AppSettingsTests.LogLevel_DefaultsToWarn
```

Expected: build error `'AppSettings' does not contain a definition for 'LogLevel'`.

- [ ] **Step 3: Add the property**

In `src/TextFix/Models/AppSettings.cs`, add after the `StartWithWindows` property (around line 32):

```csharp
    public string LogLevel { get; set; } = "Warn";
```

- [ ] **Step 4: Re-run, expect PASS**

```
dotnet test --filter FullyQualifiedName~AppSettingsTests.LogLevel_DefaultsToWarn
```

Expected: 1 passed.

- [ ] **Step 5: Run full suite**

```
dotnet test
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add src/TextFix/Models/AppSettings.cs tests/TextFix.Tests/Models/AppSettingsTests.cs
git commit -m "feat(settings): add LogLevel setting (default Warn)"
```

---

## Task 6: `StatsTracker` service

**Files:**
- Create: `src/TextFix/Models/StatsAggregate.cs`
- Create: `src/TextFix/Services/StatsTracker.cs`
- Create: `tests/TextFix.Tests/Services/StatsTrackerTests.cs`

- [ ] **Step 1: Write failing tests covering record + aggregate**

Create `tests/TextFix.Tests/Services/StatsTrackerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests — expect compile failures**

```
dotnet test --filter FullyQualifiedName~StatsTrackerTests
```

Expected: build errors for `StatsTracker`, `StatsAggregate`.

- [ ] **Step 3: Create `StatsAggregate`**

Create `src/TextFix/Models/StatsAggregate.cs`:

```csharp
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
```

- [ ] **Step 4: Implement `StatsTracker`**

Create `src/TextFix/Services/StatsTracker.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextFix.Models;

namespace TextFix.Services;

public sealed class StatsTracker
{
    private readonly string _path;
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public StatsTracker(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix",
            "stats.jsonl");

    public Task RecordAsync(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
            return Task.CompletedTask;

        var entry = new StatsEntry
        {
            Timestamp = result.Timestamp,
            Mode = result.ModeName,
            Provider = "anthropic",
            Model = result.Model,
            CharsIn = result.OriginalText.Length,
            CharsOut = result.CorrectedText.Length,
            TokensIn = result.InputTokens,
            TokensOut = result.OutputTokens,
            CostEstimate = CostEstimator.Estimate(result.Model, result.InputTokens, result.OutputTokens),
            Status = "success",
        };

        var line = JsonSerializer.Serialize(entry, WriteOptions);

        return Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (dir is not null) Directory.CreateDirectory(dir);
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch { /* stats are best-effort */ }
        });
    }

    public async Task<StatsAggregate> AggregateAsync()
    {
        if (!File.Exists(_path))
            return new StatsAggregate();

        var perMode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int lifetime = 0;
        long totalCharsIn = 0;
        decimal monthCost = 0m;

        var (year, month) = (DateTime.UtcNow.Year, DateTime.UtcNow.Month);

        foreach (var raw in await File.ReadAllLinesAsync(_path))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            StatsEntry? entry;
            try { entry = JsonSerializer.Deserialize<StatsEntry>(raw); }
            catch { continue; }
            if (entry is null) continue;

            lifetime++;
            totalCharsIn += entry.CharsIn;
            if (!string.IsNullOrEmpty(entry.Mode))
            {
                perMode.TryGetValue(entry.Mode, out var n);
                perMode[entry.Mode] = n + 1;
            }
            if (entry.Timestamp.Year == year && entry.Timestamp.Month == month)
                monthCost += entry.CostEstimate;
        }

        // 200 chars-per-minute is a typical typing speed; close enough for "time saved".
        var timeSaved = totalCharsIn / 200.0;
        return new StatsAggregate
        {
            LifetimeCorrections = lifetime,
            TimeSavedMinutes = timeSaved,
            PerMode = perMode,
            MonthCostUsd = monthCost,
        };
    }

    private sealed class StatsEntry
    {
        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
        [JsonPropertyName("mode")] public string Mode { get; set; } = "";
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("chars_in")] public int CharsIn { get; set; }
        [JsonPropertyName("chars_out")] public int CharsOut { get; set; }
        [JsonPropertyName("tokens_in")] public int TokensIn { get; set; }
        [JsonPropertyName("tokens_out")] public int TokensOut { get; set; }
        [JsonPropertyName("cost_estimate")] public decimal CostEstimate { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
    }
}
```

- [ ] **Step 5: Re-run tests, expect PASS**

```
dotnet test --filter FullyQualifiedName~StatsTrackerTests
```

Expected: 7 passed.

- [ ] **Step 6: Run full suite**

```
dotnet test
```

Expected: all pass.

- [ ] **Step 7: Commit**

```
git add src/TextFix/Models/StatsAggregate.cs src/TextFix/Services/StatsTracker.cs tests/TextFix.Tests/Services/StatsTrackerTests.cs
git commit -m "feat(stats): add StatsTracker with JSONL append and AggregateAsync"
```

---

## Task 7: Set `Model` on `CorrectionResult` in `AiClient`

**Files:**
- Modify: `src/TextFix/Services/AiClient.cs`

- [ ] **Step 1: Update the success-path `CorrectionResult` construction**

In `src/TextFix/Services/AiClient.cs`, find the success-path `return new CorrectionResult { ... }` block (around line 71-77) and add `Model = _settings.Model,`:

```csharp
            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected,
                Model = _settings.Model,
                InputTokens = (int)(message.Usage?.InputTokens ?? 0),
                OutputTokens = (int)(message.Usage?.OutputTokens ?? 0),
            };
```

- [ ] **Step 2: Build to confirm no compile errors**

```
dotnet build
```

Expected: build succeeds.

- [ ] **Step 3: Run full suite**

```
dotnet test
```

Expected: all pass.

- [ ] **Step 4: Commit**

```
git add src/TextFix/Services/AiClient.cs
git commit -m "feat(ai): stamp Model on CorrectionResult so cost estimates are accurate"
```

---

## Task 8: Wire `StatsTracker` into `CorrectionService`

**Why:** Every successful correction should be appended to `stats.jsonl` for the About window to aggregate later.

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Construct `StatsTracker` in `SetupServicesAsync`**

In `src/TextFix/App.xaml.cs`, add a field near the other services:

```csharp
    private StatsTracker? _statsTracker;
```

In `SetupServicesAsync`, after `var history = await CorrectionHistory.LoadAsync();` and before constructing `_correctionService`, add:

```csharp
        _statsTracker = new StatsTracker(StatsTracker.DefaultPath);
```

- [ ] **Step 2: Hook `RecordAsync` into the existing `CorrectionCompleted` handler**

In `SetupServicesAsync`, the existing handler is:

```csharp
        _correctionService.CorrectionCompleted += result =>
            Dispatcher.Invoke(async () =>
            {
                var autoApply = _settings.ManualApplyOnly ? 0 : _settings.OverlayAutoApplySeconds;
                _overlay?.ShowResult(result, autoApply, _settings.ManualApplyOnly);
                RefreshHistoryMenu();
                await _correctionService.History.SaveAsync();
            });
```

Add the `RecordAsync` call right after `_correctionService.History.SaveAsync()`:

```csharp
        _correctionService.CorrectionCompleted += result =>
            Dispatcher.Invoke(async () =>
            {
                var autoApply = _settings.ManualApplyOnly ? 0 : _settings.OverlayAutoApplySeconds;
                _overlay?.ShowResult(result, autoApply, _settings.ManualApplyOnly);
                RefreshHistoryMenu();
                await _correctionService.History.SaveAsync();
                if (_statsTracker is not null)
                    await _statsTracker.RecordAsync(result);
            });
```

- [ ] **Step 3: Build**

```
dotnet build
```

Expected: succeeds.

- [ ] **Step 4: Manual smoke test**

```
taskkill /IM TextFix.exe /F 2>$null; dotnet run --project src/TextFix/TextFix.csproj
```

Trigger a correction (Ctrl+Shift+Z on selected text). Open `%APPDATA%\TextFix\stats.jsonl` in a text editor — expect one JSONL line with `"mode"`, `"chars_in"`, `"chars_out"`, `"cost_estimate"`.

- [ ] **Step 5: Commit**

```
git add src/TextFix/App.xaml.cs
git commit -m "feat(stats): record successful corrections to stats.jsonl"
```

---

## Task 9: Replace `LogError`/`LogDebug` with `AppLog`

**Why:** The two ad-hoc logging methods (`error.log`, `debug.log`) get superseded by the new daily-rolling `AppLog`. One source of truth, one folder, level-controlled, retention-bounded.

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Add `AppLog` field and construct in `OnStartup`**

In `src/TextFix/App.xaml.cs`, add a field near the other services:

```csharp
    private static AppLog? _log;
```

(Field is `static` so the existing static `LogError`/`LogDebug` call sites still work without threading it through every method. We will remove the static helpers in step 4 but want a bridge during the transition.)

In `OnStartup`, after `_settings = await AppSettings.LoadAsync();`, construct the log:

```csharp
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix", "logs");
        var level = Enum.TryParse<AppLog.Level>(_settings.LogLevel, ignoreCase: true, out var lvl)
            ? lvl : AppLog.Level.Warn;
        _log = new AppLog(logDir, level);
        _log.Info($"TextFix starting (version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version})");
```

- [ ] **Step 2: Replace the bodies of `LogError` and `LogDebug`**

Replace the existing static helpers at the bottom of `App.xaml.cs` (around lines 575-602) with:

```csharp
    private static void LogError(Exception ex) => _log?.Error("Unhandled", ex);

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogDebug(string message) => _log?.Info(message);
```

Note: `LogDebug` continues to be `[Conditional("DEBUG")]` so debug logging only fires in debug builds — but when it does fire, it now goes through `AppLog` at `Info` level (which won't be written under the default `Warn` log level — fine, debug logging in release builds was never wanted anyway).

- [ ] **Step 3: Log app exit**

In `OnExit`, before the existing disposes, add:

```csharp
        _log?.Info("TextFix exiting");
```

- [ ] **Step 4: Build and verify no warnings**

```
dotnet build
```

Expected: build succeeds with no new warnings. The old `error.log` / `debug.log` files remain on disk from previous runs but are no longer written; document this in the commit message.

- [ ] **Step 5: Manual smoke test**

```
taskkill /IM TextFix.exe /F 2>$null; dotnet run --project src/TextFix/TextFix.csproj
```

Confirm `%APPDATA%\TextFix\logs\textfix-YYYY-MM-DD.log` is created and contains `[INFO]` start line. Trigger a correction; quit; check the file again — should also have an exit line.

- [ ] **Step 6: Commit**

```
git add src/TextFix/App.xaml.cs
git commit -m "refactor(log): route LogError/LogDebug through AppLog (daily rolling, 7-day retention)"
```

---

## Task 10: `AboutWindow` — UI shell

**Why:** Build the window first with mocked stats data so the visual layout is settled before wiring real data.

**Files:**
- Create: `src/TextFix/Views/AboutWindow.xaml`
- Create: `src/TextFix/Views/AboutWindow.xaml.cs`

- [ ] **Step 1: Look at SettingsWindow for the dark theme pattern**

Read `src/TextFix/Views/SettingsWindow.xaml` to copy the same `Background`, `Foreground`, font, and button styling. Match it exactly so the About window doesn't look out of place.

- [ ] **Step 2: Create `AboutWindow.xaml`**

Create `src/TextFix/Views/AboutWindow.xaml`:

```xml
<Window x:Class="TextFix.Views.AboutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="About TextFix"
        Width="420" Height="520"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        Background="#1E1E1E"
        Foreground="#E0E0E0"
        FontFamily="Segoe UI" FontSize="13">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" HorizontalAlignment="Center">
            <TextBlock Text="TextFix" FontSize="24" FontWeight="SemiBold" HorizontalAlignment="Center"/>
            <TextBlock x:Name="VersionText" FontSize="12" Foreground="#888" HorizontalAlignment="Center" Margin="0,4,0,0"/>
            <TextBlock Text="Quick AI text correction for Windows." FontSize="12" Foreground="#888" HorizontalAlignment="Center" Margin="0,8,0,0"/>
        </StackPanel>

        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,16,0,16">
            <TextBlock Margin="0,0,16,0">
                <Hyperlink x:Name="GitHubLink" Foreground="#5AB0FF" Click="GitHubLink_Click">GitHub</Hyperlink>
            </TextBlock>
            <TextBlock>
                <Hyperlink x:Name="LicenseLink" Foreground="#5AB0FF" Click="LicenseLink_Click">MIT License</Hyperlink>
            </TextBlock>
        </StackPanel>

        <Border Grid.Row="2" Background="#262626" CornerRadius="6" Padding="16">
            <StackPanel x:Name="StatsPanel">
                <TextBlock Text="Your stats" FontWeight="SemiBold" Margin="0,0,0,8"/>
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="Lifetime corrections" Foreground="#BBB"/>
                    <TextBlock x:Name="LifetimeText" Grid.Column="1" Text="–"/>
                </Grid>
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="Time saved (estimated)" Foreground="#BBB"/>
                    <TextBlock x:Name="TimeSavedText" Grid.Column="1" Text="–"/>
                </Grid>
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="Most-used mode" Foreground="#BBB"/>
                    <TextBlock x:Name="MostUsedModeText" Grid.Column="1" Text="–"/>
                </Grid>
                <Grid Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="This month spend (estimated)" Foreground="#BBB"/>
                    <TextBlock x:Name="MonthCostText" Grid.Column="1" Text="–"/>
                </Grid>
                <TextBlock Text="By mode" FontWeight="SemiBold" Margin="0,8,0,4"/>
                <ItemsControl x:Name="PerModeList">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2,0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding Key}" Foreground="#BBB"/>
                                <TextBlock Grid.Column="1" Text="{Binding Value}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>

        <Button Grid.Row="3" x:Name="KofiButton"
                Content="☕  Tip on Ko-fi"
                Click="KofiButton_Click"
                Background="#FF5E5B" Foreground="White"
                FontWeight="SemiBold" FontSize="14"
                Padding="0,10,0,10" BorderThickness="0"
                Margin="0,16,0,0" Cursor="Hand"/>
    </Grid>
</Window>
```

- [ ] **Step 3: Create `AboutWindow.xaml.cs` with placeholder data**

Create `src/TextFix/Views/AboutWindow.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace TextFix.Views;

public partial class AboutWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string LicenseUrl = "https://github.com/agnt-labs-oz/TextFix/blob/master/LICENSE";

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? "?"}";
    }

    private void KofiButton_Click(object sender, RoutedEventArgs e) => OpenUrl(KofiUrl);
    private void GitHubLink_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    private void LicenseLink_Click(object sender, RoutedEventArgs e) => OpenUrl(LicenseUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
```

- [ ] **Step 4: Build to confirm XAML compiles**

```
dotnet build
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```
git add src/TextFix/Views/AboutWindow.xaml src/TextFix/Views/AboutWindow.xaml.cs
git commit -m "feat(about): add AboutWindow shell with version, links, Ko-fi CTA"
```

---

## Task 11: Wire `StatsAggregate` into `AboutWindow`

**Files:**
- Modify: `src/TextFix/Views/AboutWindow.xaml.cs`

- [ ] **Step 1: Add `LoadStats` method that reads from `StatsTracker`**

In `src/TextFix/Views/AboutWindow.xaml.cs`, add a new field, constructor parameter, and async load method:

```csharp
using TextFix.Models;
using TextFix.Services;

// ...

public partial class AboutWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string LicenseUrl = "https://github.com/agnt-labs-oz/TextFix/blob/master/LICENSE";

    private readonly StatsTracker _statsTracker;

    public AboutWindow(StatsTracker statsTracker)
    {
        _statsTracker = statsTracker;
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? "?"}";
        Loaded += async (_, _) => await LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        StatsAggregate agg;
        try { agg = await _statsTracker.AggregateAsync(); }
        catch { agg = new StatsAggregate(); }

        LifetimeText.Text = agg.LifetimeCorrections.ToString("N0");
        TimeSavedText.Text = FormatMinutes(agg.TimeSavedMinutes);
        MostUsedModeText.Text = agg.MostUsedMode is null
            ? "–"
            : $"{agg.MostUsedMode} ({Pct(agg)}%)";
        MonthCostText.Text = $"${agg.MonthCostUsd:0.00}";
        PerModeList.ItemsSource = agg.PerMode
            .OrderByDescending(kv => kv.Value)
            .ToList();
    }

    private static int Pct(StatsAggregate agg)
    {
        if (agg.LifetimeCorrections == 0 || agg.MostUsedMode is null) return 0;
        return (int)Math.Round(100.0 * agg.PerMode[agg.MostUsedMode] / agg.LifetimeCorrections);
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1.0) return "< 1m";
        var ts = TimeSpan.FromMinutes(minutes);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }

    // existing handlers unchanged
    // ...
}
```

- [ ] **Step 2: Build**

```
dotnet build
```

Expected: succeeds.

- [ ] **Step 3: Commit**

```
git add src/TextFix/Views/AboutWindow.xaml.cs
git commit -m "feat(about): populate stats panel from StatsTracker.AggregateAsync"
```

---

## Task 12: Add new tray menu items

**Why:** Five new items: Suggest a feature, Report an issue, Open log folder, About TextFix, Support TextFix. Plus a separator before About and another after Support.

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Add URL constants near the top of the class**

In `src/TextFix/App.xaml.cs`, just inside the `App` class (after the `[STAThread] Main` block is fine), add:

```csharp
    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubRepoUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string GitHubNewIssueUrl =
        "https://github.com/agnt-labs-oz/TextFix/issues/new?template=bug_report.yml";
    private const string GitHubNewIdeaUrl =
        "https://github.com/agnt-labs-oz/TextFix/discussions/new?category=ideas";
```

- [ ] **Step 2: Extend the tray menu in `SetupTrayIcon`**

In `src/TextFix/App.xaml.cs`, find the existing block (around lines 184-187) that adds Settings, Check for updates, separator, Exit:

```csharp
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("Check for updates…", null, OnCheckForUpdatesClicked);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
```

Replace with:

```csharp
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("Check for updates…", null, OnCheckForUpdatesClicked);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Suggest a feature…", null, (_, _) => OpenUrl(GitHubNewIdeaUrl));
        _trayIcon.ContextMenuStrip.Items.Add("Report an issue…", null, (_, _) => OpenUrl(GitHubNewIssueUrl));
        _trayIcon.ContextMenuStrip.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("About TextFix…", null, (_, _) => OpenAbout());
        _trayIcon.ContextMenuStrip.Items.Add("Support TextFix ☕", null, (_, _) => OpenUrl(KofiUrl));
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
```

- [ ] **Step 3: Add `OpenUrl`, `OpenLogFolder`, `OpenAbout` helpers**

In `src/TextFix/App.xaml.cs`, near the bottom of the class (before the `LogError`/`LogDebug` helpers), add:

```csharp
    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log?.Warn($"OpenUrl failed: {ex.Message}"); }
    }

    private void OpenLogFolder()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TextFix", "logs");
            Directory.CreateDirectory(logDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", logDir) { UseShellExecute = true });
        }
        catch (Exception ex) { _log?.Warn($"OpenLogFolder failed: {ex.Message}"); }
    }

    private void OpenAbout()
    {
        if (_statsTracker is null) return;
        var window = new Views.AboutWindow(_statsTracker);
        window.ShowDialog();
    }
```

- [ ] **Step 4: Build**

```
dotnet build
```

Expected: succeeds.

- [ ] **Step 5: Manual smoke test**

```
taskkill /IM TextFix.exe /F 2>$null; dotnet run --project src/TextFix/TextFix.csproj
```

Right-click the tray icon. Confirm the menu shows the new items in this order:

```
Mode ►
History ►
Copy Last Correction
Settings
Check for updates…
─────────────
Suggest a feature…
Report an issue…
Open log folder
─────────────
About TextFix…
Support TextFix ☕
─────────────
Exit
```

Click each:
- Suggest a feature → opens GitHub Discussions in browser at the new-idea form
- Report an issue → opens GitHub Issues new-bug form
- Open log folder → opens Explorer at the logs folder
- About TextFix → opens the About window with stats panel
- Support TextFix → opens Ko-fi page

- [ ] **Step 6: Commit**

```
git add src/TextFix/App.xaml.cs
git commit -m "feat(tray): add Suggest/Report/Logs/About/Support menu items"
```

---

## Task 13: GitHub Issue and Discussion templates

**Why:** The "Report an issue" tray item points at `?template=bug_report.yml` and "Suggest a feature" points at the Discussions Ideas category. Templates make those forms structured.

**Files:**
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `.github/DISCUSSION_TEMPLATE/ideas.yml`

- [ ] **Step 1: Create the bug report template**

Create `.github/ISSUE_TEMPLATE/bug_report.yml`:

```yaml
name: Bug report
description: Something isn't working in TextFix
labels: ["bug"]
body:
  - type: input
    id: version
    attributes:
      label: TextFix version
      description: From "About TextFix…" in the tray menu, e.g. v0.6.0
    validations:
      required: true
  - type: dropdown
    id: provider
    attributes:
      label: AI provider
      options:
        - Anthropic (Claude)
        - Other
    validations:
      required: true
  - type: input
    id: model
    attributes:
      label: Model
      placeholder: e.g. claude-haiku-4-5
  - type: dropdown
    id: mode
    attributes:
      label: Correction mode in use
      options:
        - Fix errors
        - Professional
        - Concise
        - Friendly
        - Expand
        - Prompt enhancer
        - Custom
        - Don't know / multiple
    validations:
      required: true
  - type: textarea
    id: what-happened
    attributes:
      label: What happened?
      description: What did you do, what did you expect, what did you see?
      placeholder: "I selected text and pressed Ctrl+Shift+Z. I expected ... Instead, ..."
    validations:
      required: true
  - type: textarea
    id: log-excerpt
    attributes:
      label: Relevant log excerpt
      description: From the "Open log folder" tray item — copy any [WARN] or [ERROR] lines from around the time of the bug.
      render: text
```

- [ ] **Step 2: Create the discussion template for ideas**

Create `.github/DISCUSSION_TEMPLATE/ideas.yml`:

```yaml
title: "[Idea] "
labels: []
body:
  - type: textarea
    id: idea
    attributes:
      label: Your idea
      description: What feature or change would you like to see in TextFix?
    validations:
      required: true
  - type: textarea
    id: usecase
    attributes:
      label: Why does it matter?
      description: When would you use it? What problem does it solve for you?
    validations:
      required: true
  - type: dropdown
    id: provider
    attributes:
      label: Does this involve a specific AI provider?
      options:
        - Anthropic (Claude) — current
        - OpenAI
        - Google Gemini
        - Local model (Ollama, llama.cpp, etc.)
        - Provider-agnostic / N/A
    validations:
      required: false
```

- [ ] **Step 3: Commit**

```
git add .github/ISSUE_TEMPLATE/bug_report.yml .github/DISCUSSION_TEMPLATE/ideas.yml
git commit -m "feat(github): add structured bug-report issue and ideas discussion templates"
```

---

## Task 14: Enable Discussions on the GitHub repo (one-time, manual)

**Files:** none — this is a manual step performed in the GitHub UI by the repo admin.

- [ ] **Step 1: Enable Discussions**

Open `https://github.com/agnt-labs-oz/TextFix/settings` → scroll to **Features** → tick **Discussions** → save.

- [ ] **Step 2: Create the four categories**

Open `https://github.com/agnt-labs-oz/TextFix/discussions/categories` and create (or rename existing) so the categories are exactly:

| Category | Format | Description |
|---|---|---|
| Ideas | Open-ended discussion | Suggest a feature or improvement |
| Bugs | Q&A | Report something that isn't working — for code-tracked bugs use Issues instead |
| Q&A | Q&A | Ask anything about using or configuring TextFix |
| Show & Tell | Open-ended discussion | Share how you use TextFix or what you've made with it |

- [ ] **Step 3: Verify Ideas template appears**

Visit `https://github.com/agnt-labs-oz/TextFix/discussions/new?category=ideas` and confirm the structured form from `ideas.yml` renders. (If GitHub hasn't picked up the template yet, give it a minute or two.)

---

## Task 15: README rewrite

**Why:** The README is the front door for new users and the place where Phase 1's "validate" intent lives or dies. Hero screenshot, walkthrough with screenshots, Privacy section, Support section.

**Files:**
- Modify: `README.md`
- Create: `docs/screenshots/overlay.png`
- Create: `docs/screenshots/settings.png`
- Create: `docs/screenshots/api-key.png`
- Create: `docs/screenshots/mode-picker.png`

- [ ] **Step 1: Capture screenshots from a running build**

Build and run TextFix (`dotnet run --project src/TextFix/TextFix.csproj`). Capture and save (PNG, ≤ ~600 KB each) the following:

- `docs/screenshots/overlay.png` — overlay open with a real before/after correction (use Notepad, type a sentence with a typo, select, hit Ctrl+Shift+Z, screenshot the overlay)
- `docs/screenshots/settings.png` — the Settings window with API key field visible (paste any throwaway value)
- `docs/screenshots/api-key.png` — the Anthropic console "API keys" page (with key values masked)
- `docs/screenshots/mode-picker.png` — the tray menu's Mode submenu expanded

- [ ] **Step 2: Replace `README.md` with the new content**

Rewrite `README.md` so the structure is:

```markdown
# TextFix

A lightweight Windows desktop app that corrects and improves your text using AI...

![TextFix overlay showing a correction](docs/screenshots/overlay.png)

## How it works

(unchanged from current README — keep the four-step list)

## Features

(unchanged)

## Setup

### 1. Get an Anthropic API key

Sign in at [console.anthropic.com](https://console.anthropic.com/settings/keys) and create a new key.

![Anthropic API keys page](docs/screenshots/api-key.png)

### 2. Install TextFix

(merge the existing "Download" / "build from source" sections here)

### 3. Paste your key into Settings

On first run, the Settings window opens automatically. Paste your key and pick a model.

![Settings window](docs/screenshots/settings.png)

### 4. Try it

Select text in any app and press **Ctrl+Shift+Z**. The overlay shows the suggested correction.

(Keep the existing Configuration table.)

## Suggest features / report bugs

- **Ideas** → [Discussions › Ideas](https://github.com/agnt-labs-oz/TextFix/discussions/categories/ideas)
- **Bugs** → [Issues](https://github.com/agnt-labs-oz/TextFix/issues/new/choose)
- Or click **Suggest a feature…** / **Report an issue…** in the tray menu — both pre-fill the right form.

![Tray menu mode picker](docs/screenshots/mode-picker.png)

## Support TextFix

TextFix is free and MIT-licensed. If it saves you time, you can leave a tip:

[☕ ko-fi.com/3smallwins](https://ko-fi.com/3smallwins)

## Privacy

When you trigger a correction, TextFix sends your selected text to your chosen AI provider (Anthropic by default — you provide your own API key). **Nothing is sent to the developer**, no telemetry is collected, and your API key is encrypted on disk with Windows DPAPI.

The lightweight log file at `%APPDATA%\TextFix\logs\` records counts and errors only — never the text you correct. Stats shown in the About window come from a local file at `%APPDATA%\TextFix\stats.jsonl` and never leave your machine.

## Roadmap

(Keep the existing Shipped / Planned structure; move "Usage stats" from Planned to Shipped now.)

## Tech stack

(unchanged)

## License

MIT
```

(The above is the structural intent. When implementing, write the actual full prose for each section in your own voice — don't paste literal section headers without the supporting paragraphs.)

- [ ] **Step 3: Verify markdown renders**

Use a markdown previewer (VS Code's built-in) to check that all images load and links resolve. Cross-check the Roadmap "Shipped" list now includes:

- Usage stats panel in About window
- Daily-rolling logging
- In-tray Suggest / Report / Support / About menu items

- [ ] **Step 4: Commit**

```
git add README.md docs/screenshots/
git commit -m "docs(readme): walkthrough with screenshots, Privacy + Support sections"
```

---

## Verification Pass

- [ ] **Step 1: Run the full test suite**

```
dotnet test
```

Expected: all tests pass. The new tests added by this plan: `CorrectionResultTests` (2), `CostEstimatorTests` (6), `AppLogTests` (6), `StatsTrackerTests` (7), `AppSettingsTests.LogLevel_DefaultsToWarn` (1), `CorrectionHistoryTests.SessionCost_UsesPerModelRates` (1) = **23 new tests** on top of the existing 23 → 46 total.

- [ ] **Step 2: End-to-end smoke**

```
taskkill /IM TextFix.exe /F 2>$null; dotnet run --project src/TextFix/TextFix.csproj
```

Walk through:
1. Trigger 3-5 corrections in different modes (Fix errors, Professional, Concise).
2. Open About TextFix from the tray. Confirm Lifetime ≥ 3, Per-mode breakdown shows the three modes, Time Saved shows non-zero, This Month spend > $0.00.
3. Click "☕ Tip on Ko-fi" — opens browser to https://ko-fi.com/3smallwins.
4. Tray → "Suggest a feature…" — opens Discussions ideas form (after Task 14 completes; before that, opens Discussions list).
5. Tray → "Report an issue…" — opens Issues bug-report form (after Task 13 deploys, this opens the structured form).
6. Tray → "Open log folder" — opens Explorer with `textfix-YYYY-MM-DD.log` listed.
7. Open the log file — confirm `[INFO] TextFix starting` and a per-correction `[INFO]` line aren't present at default `Warn` level (they shouldn't be — only `[WARN]` and `[ERROR]` should write under defaults). Edit `%APPDATA%\TextFix\settings.json` to set `"LogLevel": "Info"`, restart, repeat — confirm now `[INFO]` lines appear.

- [ ] **Step 3: Confirm the obsolete `error.log` and `debug.log` aren't being written**

After running through Step 2 above, check `%APPDATA%\TextFix\` — neither `error.log` nor `debug.log` should have been touched (mtime older than the smoke test). The new daily-rolling log under `logs\` is the sole sink.

(Existing leftover `error.log`/`debug.log` files from past runs can stay — don't delete them as part of the plan; users can clean them up themselves if they care.)

- [ ] **Step 4: Tag the release**

```
git tag v0.7.0
git push origin master
git push origin v0.7.0
```

The existing GitHub Actions workflow will publish `TextFix-v0.7.0-win-x64.zip` to the Releases page.
