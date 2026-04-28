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
}
