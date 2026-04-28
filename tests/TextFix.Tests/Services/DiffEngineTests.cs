using TextFix.Services;

namespace TextFix.Tests.Services;

public class DiffEngineTests
{
    [Fact]
    public void DiffSegment_HoldsKindAndText()
    {
        var seg = new DiffSegment(DiffKind.Equal, "hello");
        Assert.Equal(DiffKind.Equal, seg.Kind);
        Assert.Equal("hello", seg.Text);
    }

    [Fact]
    public void Compute_EmptyInputs_ReturnsNoSegments()
    {
        var result = DiffEngine.Compute("", "");
        Assert.Empty(result.Segments);
        Assert.Equal(0, result.Stats.OriginalWordCount);
        Assert.Equal(0.0, result.Stats.ChangeRatio);
    }

    [Fact]
    public void Compute_IdenticalStrings_AllEqual()
    {
        var result = DiffEngine.Compute("the quick brown fox", "the quick brown fox");
        Assert.All(result.Segments, s => Assert.Equal(DiffKind.Equal, s.Kind));
        Assert.Equal("the quick brown fox", string.Concat(result.Segments.Select(s => s.Text)));
        Assert.Equal(0, result.Stats.RemovedWordCount);
        Assert.Equal(0, result.Stats.AddedWordCount);
        Assert.Equal(0.0, result.Stats.ChangeRatio);
    }

    [Fact]
    public void Compute_SingleWordSwap_ProducesRemovedAndAdded()
    {
        var result = DiffEngine.Compute("the brown fox", "the swift fox");

        Assert.Contains(result.Segments, s => s.Kind == DiffKind.Removed && s.Text == "brown");
        Assert.Contains(result.Segments, s => s.Kind == DiffKind.Added && s.Text == "swift");
        Assert.Equal(1, result.Stats.RemovedWordCount);
        Assert.Equal(1, result.Stats.AddedWordCount);
        Assert.Equal(3, result.Stats.OriginalWordCount);

        var rebuiltOriginal = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
        Assert.Equal("the brown fox", rebuiltOriginal);

        var rebuiltCorrected = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Removed).Select(s => s.Text));
        Assert.Equal("the swift fox", rebuiltCorrected);
    }

    [Fact]
    public void Compute_PureAddition()
    {
        var result = DiffEngine.Compute("hello", "hello world");

        Assert.Equal(1, result.Stats.OriginalWordCount);
        Assert.Equal(0, result.Stats.RemovedWordCount);
        Assert.Equal(1, result.Stats.AddedWordCount);

        var rebuiltCorrected = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Removed).Select(s => s.Text));
        Assert.Equal("hello world", rebuiltCorrected);
    }

    [Fact]
    public void Compute_PureRemoval()
    {
        var result = DiffEngine.Compute("hello world", "hello");

        Assert.Equal(2, result.Stats.OriginalWordCount);
        Assert.Equal(1, result.Stats.RemovedWordCount);
        Assert.Equal(0, result.Stats.AddedWordCount);

        var rebuiltOriginal = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
        Assert.Equal("hello world", rebuiltOriginal);
    }

    [Fact]
    public void Compute_PreservesNewlinesAndSpacing()
    {
        var original  = "line one\nline two";
        var corrected = "line one\nline 2";

        var result = DiffEngine.Compute(original, corrected);

        var rebuiltOriginal = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
        var rebuiltCorrected = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Removed).Select(s => s.Text));

        Assert.Equal(original, rebuiltOriginal);
        Assert.Equal(corrected, rebuiltCorrected);
    }

    [Fact]
    public void Compute_ChangeRatio_CountsRemovedPlusAddedOverOriginal()
    {
        // 4 original words, 1 removed + 1 added → ratio = 2/4 = 0.5
        var result = DiffEngine.Compute("a b c d", "a x c d");
        Assert.Equal(4, result.Stats.OriginalWordCount);
        Assert.Equal(1, result.Stats.RemovedWordCount);
        Assert.Equal(1, result.Stats.AddedWordCount);
        Assert.Equal(0.5, result.Stats.ChangeRatio, 3);
    }

    [Fact]
    public void Compute_WhitespaceOnlyChange_RatioIsZero()
    {
        // Whitespace tokens are not "words", so even though one whitespace run
        // differs, the word counts are unchanged and ratio stays at 0.
        var result = DiffEngine.Compute("hello world", "hello  world");
        Assert.Equal(0, result.Stats.RemovedWordCount);
        Assert.Equal(0, result.Stats.AddedWordCount);
        Assert.Equal(0.0, result.Stats.ChangeRatio);

        var rebuiltOriginal = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
        var rebuiltCorrected = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Removed).Select(s => s.Text));
        Assert.Equal("hello world", rebuiltOriginal);
        Assert.Equal("hello  world", rebuiltCorrected);
    }

    [Fact]
    public void Compute_SpaceToTab_ReconstructsBoth()
    {
        var result = DiffEngine.Compute("a b", "a\tb");

        var rebuiltOrig = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
        var rebuiltCorr = string.Concat(
            result.Segments.Where(s => s.Kind != DiffKind.Removed).Select(s => s.Text));
        Assert.Equal("a b", rebuiltOrig);
        Assert.Equal("a\tb", rebuiltCorr);
    }

    [Fact]
    public void Compute_CharChangeRatio_TypoFix_IsLow()
    {
        // "helo" → "hello": 1 char insert, max len = 5, ratio = 0.2.
        var result = DiffEngine.Compute("helo", "hello");
        Assert.InRange(result.Stats.CharChangeRatio, 0.15, 0.25);
    }

    [Fact]
    public void Compute_CharChangeRatio_MultiTypoSentence_StillLow()
    {
        // Realistic typo-fix case that the word-level ratio over-counts at 2.0.
        // Char edit distance: 'l' insert, 'l' insert, 'p' insert → 3.
        // Max len = 26. Ratio ~ 0.115.
        var result = DiffEngine.Compute("helo wrld this is a tpyo", "hello world this is a typo");
        Assert.InRange(result.Stats.CharChangeRatio, 0.05, 0.20);
    }

    [Fact]
    public void Compute_CharChangeRatio_CompleteRewrite_IsHigh()
    {
        // "make this concise" → "be brief" — almost no shared structure.
        var result = DiffEngine.Compute("make this concise", "be brief");
        Assert.True(result.Stats.CharChangeRatio > 0.5,
            $"Expected high ratio for rewrite, got {result.Stats.CharChangeRatio:F2}");
    }

    [Fact]
    public void Compute_CharChangeRatio_Identical_IsZero()
    {
        var result = DiffEngine.Compute("hello world", "hello world");
        Assert.Equal(0.0, result.Stats.CharChangeRatio);
    }

    [Fact]
    public void Compute_CharChangeRatio_EmptyInputs_IsZero()
    {
        var result = DiffEngine.Compute("", "");
        Assert.Equal(0.0, result.Stats.CharChangeRatio);
    }

    [Fact]
    public void Compute_CharChangeRatio_FromEmpty_IsOne()
    {
        var result = DiffEngine.Compute("", "hello");
        Assert.Equal(1.0, result.Stats.CharChangeRatio);
    }
}
