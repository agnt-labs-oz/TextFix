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
            Provider = result.ProviderId,
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

    /// <summary>Marks a line that carries forward the time-saved total across wipes.</summary>
    private const string CarryoverStatus = "carryover";

    /// <summary>
    /// Wipes the lifetime record behind the About window — correction counts, per-mode
    /// breakdown, spend — while carrying the cumulative "time saved" total forward.
    /// </summary>
    /// <remarks>
    /// "Clear history" used to wipe <see cref="CorrectionHistory"/> and stop there, so
    /// this file survived — and the About window went on reporting every correction the
    /// user had just asked to erase.
    ///
    /// Time saved is deliberately NOT wiped (user decision, 2026-08-11): counts and
    /// per-mode stats describe *what was corrected* and go with the history; time saved
    /// is a single running motivational total, closer to an odometer than a log. The
    /// mechanism: the per-correction lines are replaced by ONE carryover line holding
    /// the summed input length — no modes, no models, no timestamps of individual
    /// corrections survive, so the behavioural record is still gone.
    ///
    /// Unlike <see cref="RecordAsync"/>, failure here is NOT swallowed. A stats line
    /// that fails to write is a lost data point; a wipe that silently fails leaves the
    /// user believing data is gone when it is not.
    /// </remarks>
    public Task ClearAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return;

            long totalCharsIn = 0;
            foreach (var raw in File.ReadAllLines(_path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    // A previous wipe's carryover line sums in like any other, so
                    // repeated wipes keep accumulating rather than resetting.
                    var entry = JsonSerializer.Deserialize<StatsEntry>(raw);
                    if (entry is not null) totalCharsIn += entry.CharsIn;
                }
                catch (JsonException) { /* a corrupt line carries nothing forward */ }
            }

            if (totalCharsIn <= 0)
            {
                File.Delete(_path);
                return;
            }

            var carry = new StatsEntry
            {
                Timestamp = DateTime.UtcNow,
                CharsIn = (int)Math.Min(totalCharsIn, int.MaxValue),
                Status = CarryoverStatus,
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(carry, WriteOptions) + Environment.NewLine);
        }
    });

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

            // A carryover line preserves only the time-saved total across a history
            // wipe. It is not a correction: it must not count toward lifetime, modes
            // or spend, or the wipe would appear to have failed.
            if (entry.Status == CarryoverStatus)
            {
                totalCharsIn += entry.CharsIn;
                continue;
            }

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
