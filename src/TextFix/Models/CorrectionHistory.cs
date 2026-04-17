namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private const int MaxItems = 10;

    public IReadOnlyList<CorrectionResult> Items => _items;
    public int TotalCount { get; private set; }

    public int TodayCount
    {
        get
        {
            var todayUtc = DateTime.UtcNow.Date;
            int count = 0;
            foreach (var item in _items)
            {
                if (item.Timestamp.Date == todayUtc)
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
        _items.Insert(0, result);

        if (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }
}
