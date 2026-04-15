// src/TextFix/Models/CorrectionHistory.cs
namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private const int MaxItems = 10;

    public IReadOnlyList<CorrectionResult> Items => _items;

    public void Add(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
            return;

        _items.Insert(0, result);

        if (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }
}
