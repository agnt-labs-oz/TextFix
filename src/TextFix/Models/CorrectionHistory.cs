using System.IO;
using System.Text.Json;

namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private const int MaxItems = 50;

    // Haiku pricing: $0.80/M input, $4.00/M output
    private const decimal InputCostPerToken = 0.80m / 1_000_000m;
    private const decimal OutputCostPerToken = 4.00m / 1_000_000m;

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
        SessionCost += result.InputTokens * InputCostPerToken
                     + result.OutputTokens * OutputCostPerToken;

        _items.Insert(0, result);

        if (_items.Count > MaxItems)
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

    public static async Task<CorrectionHistory> LoadAsync(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new CorrectionHistory();

        try
        {
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<HistoryData>(stream, JsonOptions);
            if (data is null)
                return new CorrectionHistory();

            var history = new CorrectionHistory();
            history.TotalCount = data.TotalCount;
            foreach (var item in data.Items)
                history._items.Add(item);
            return history;
        }
        catch
        {
            return new CorrectionHistory();
        }
    }

    private class HistoryData
    {
        public int TotalCount { get; set; }
        public List<CorrectionResult> Items { get; set; } = [];
    }
}
