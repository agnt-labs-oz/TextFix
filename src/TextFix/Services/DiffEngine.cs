namespace TextFix.Services;

public enum DiffKind
{
    Equal,
    Removed,
    Added,
}

public record DiffSegment(DiffKind Kind, string Text);

public record DiffStats(
    int OriginalWordCount,
    int RemovedWordCount,
    int AddedWordCount,
    int CharEditDistance,
    int MaxCharLength)
{
    /// <summary>
    /// Word-level churn (Removed + Added) / Original. Range [0.0, +inf). Most "fix typos"
    /// corrections produce ratios >= 1.0 because every typo is a full token swap at word
    /// granularity ("helo" vs "hello" are different tokens). Use <see cref="CharChangeRatio"/>
    /// for "is this a correction or a rewrite" gating.
    /// </summary>
    public double ChangeRatio => OriginalWordCount == 0
        ? (AddedWordCount == 0 ? 0.0 : 1.0)
        : (double)(RemovedWordCount + AddedWordCount) / OriginalWordCount;

    /// <summary>
    /// Character-level edit distance / max(originalLen, correctedLen). Range [0.0, 1.0].
    /// ~0.0–0.2 = typo or minor correction; ~0.5+ = substantial rewrite. This is the
    /// metric to use when deciding whether the change is "small enough that a diff helps".
    /// </summary>
    public double CharChangeRatio => MaxCharLength == 0
        ? 0.0
        : (double)CharEditDistance / MaxCharLength;
}

public record DiffResult(IReadOnlyList<DiffSegment> Segments, DiffStats Stats);

public static class DiffEngine
{
    /// <summary>
    /// Word-level diff. Whitespace runs are kept as their own tokens so the rendered
    /// segments preserve original spacing/newlines exactly.
    /// </summary>
    public static DiffResult Compute(string original, string corrected)
    {
        var origTokens = Tokenize(original);
        var corrTokens = Tokenize(corrected);

        var segments = MergeSegments(LcsDiff(origTokens, corrTokens));

        int origWords = origTokens.Count(IsWord);
        int removedWords = segments
            .Where(s => s.Kind == DiffKind.Removed)
            .Sum(s => CountWords(s.Text));
        int addedWords = segments
            .Where(s => s.Kind == DiffKind.Added)
            .Sum(s => CountWords(s.Text));

        int editDist = LevenshteinDistance(original ?? "", corrected ?? "");
        int maxLen = Math.Max(original?.Length ?? 0, corrected?.Length ?? 0);

        return new DiffResult(segments,
            new DiffStats(origWords, removedWords, addedWords, editDist, maxLen));
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Two-row rolling DP — O(n) memory rather than O(n*m).
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text)) return tokens;

        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            bool ws = char.IsWhiteSpace(text[i]);
            while (i < text.Length && char.IsWhiteSpace(text[i]) == ws)
                i++;
            tokens.Add(text.Substring(start, i - start));
        }
        return tokens;
    }

    private static bool IsWord(string token) =>
        token.Length > 0 && !char.IsWhiteSpace(token[0]);

    private static int CountWords(string text) =>
        Tokenize(text).Count(IsWord);

    private static List<DiffSegment> LcsDiff(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Standard Myers/LCS dynamic-programming table.
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                if (a[i] == b[j])
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var segs = new List<DiffSegment>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                segs.Add(new DiffSegment(DiffKind.Equal, a[x]));
                x++; y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                segs.Add(new DiffSegment(DiffKind.Removed, a[x]));
                x++;
            }
            else
            {
                segs.Add(new DiffSegment(DiffKind.Added, b[y]));
                y++;
            }
        }
        while (x < n) { segs.Add(new DiffSegment(DiffKind.Removed, a[x++])); }
        while (y < m) { segs.Add(new DiffSegment(DiffKind.Added, b[y++])); }
        return segs;
    }

    private static List<DiffSegment> MergeSegments(List<DiffSegment> segs)
    {
        // Coalesce consecutive same-kind segments to reduce Run count in WPF rendering.
        var merged = new List<DiffSegment>();
        foreach (var seg in segs)
        {
            if (merged.Count > 0 && merged[^1].Kind == seg.Kind)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + seg.Text };
            else
                merged.Add(seg);
        }
        return merged;
    }
}
