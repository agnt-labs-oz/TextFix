using System.IO;
using System.Text.Json;

namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private int _maxItems;

    // Hard ceiling — the settings UI clamps to this so a user can't accidentally
    // ask us to keep an unbounded number of entries on disk.
    public const int MaxItemsCap = 100;

    public CorrectionHistory(int maxItems = 50)
    {
        _maxItems = ClampMax(maxItems);
    }

    private static int ClampMax(int requested) => Math.Clamp(requested, 1, MaxItemsCap);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public IReadOnlyList<CorrectionResult> Items => _items;
    public int TotalCount { get; set; }
    public decimal SessionCost { get; private set; }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix",
            "history.json");

    public int TodayCount
    {
        get
        {
            // Timestamps are stored in UTC; "today" means the user's local today,
            // so convert before comparing — otherwise the count rolls over at UTC
            // midnight, not local midnight (off by up to a day in Australia).
            var todayLocal = DateTime.Now.Date;
            int count = 0;
            foreach (var item in _items)
            {
                if (item.Timestamp.ToLocalTime().Date == todayLocal)
                    count++;
            }
            return count;
        }
    }

    public void Add(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
            return;

        TotalCount++;
        SessionCost += TextFix.Services.CostEstimator.Estimate(
            result.Model, result.InputTokens, result.OutputTokens);

        _items.Insert(0, result);

        TrimToLimit();
    }

    public void SetMaxItems(int maxItems)
    {
        _maxItems = ClampMax(maxItems);
        TrimToLimit();
    }

    /// <summary>
    /// Wipes all history entries plus the lifetime counter and the rolling session cost.
    /// Caller is expected to follow up with <see cref="SaveAsync"/> so the on-disk copy
    /// matches — wiping in memory only would resurrect old entries on next launch.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        TotalCount = 0;
        SessionCost = 0m;
    }

    private void TrimToLimit()
    {
        while (_items.Count > _maxItems)
            _items.RemoveAt(_items.Count - 1);
    }

    public async Task SaveAsync(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var data = new HistoryData { TotalCount = TotalCount, Items = [.. _items] };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions);
    }

    public static async Task<CorrectionHistory> LoadAsync(string? path = null, int maxItems = 50)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new CorrectionHistory(maxItems);

        try
        {
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<HistoryData>(stream, JsonOptions);
            if (data is null)
                return new CorrectionHistory(maxItems);

            var history = new CorrectionHistory(maxItems);
            history.TotalCount = data.TotalCount;
            foreach (var item in data.Items)
                history._items.Add(item);
            history.TrimToLimit();
            return history;
        }
        catch
        {
            return new CorrectionHistory(maxItems);
        }
    }

    private class HistoryData
    {
        public int TotalCount { get; set; }
        public List<CorrectionResult> Items { get; set; } = [];
    }
}
