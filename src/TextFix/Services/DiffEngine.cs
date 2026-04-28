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
