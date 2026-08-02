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
            CostEstimate = CostEstimator.Estimate(result.Model, result.InputTokens, result.OutputTokens, result.IsLocal),
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
            // Normalize to UTC so a Local/Unspecified timestamp from a hand-edited line doesn't
            // shift entries into the wrong month for users east of UTC near month boundaries.
            var ts = entry.Timestamp.ToUniversalTime();
            if (ts.Year == year && ts.Month == month)
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
