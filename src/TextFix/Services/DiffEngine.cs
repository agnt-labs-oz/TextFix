namespace TextFix.Services;

public enum DiffKind
{
    Equal,
    Removed,
    Added,
}

public record DiffSegment(DiffKind Kind, string Text);

public record DiffStats(int OriginalWordCount, int RemovedWordCount, int AddedWordCount)
{
    /// <summary>
    /// (Removed + Added) / Original. Range [0.0, +inf): a pure-expansion correction can
    /// exceed 1.0 (e.g. 1 word becomes 4 words = 4.0). When original is empty, returns
    /// 0.0 if nothing was added (identical empties) else 1.0.
    /// </summary>
    public double ChangeRatio => OriginalWordCount == 0
        ? (AddedWordCount == 0 ? 0.0 : 1.0)
        : (double)(RemovedWordCount + AddedWordCount) / OriginalWordCount;
}

public record DiffResult(IReadOnlyList<DiffSegment> Segments, DiffStats Stats);

public static class DiffEngine
{
    public static DiffResult Compute(string original, string corrected)
    {
        // Implemented in Task 3.
        throw new NotImplementedException();
    }
}
