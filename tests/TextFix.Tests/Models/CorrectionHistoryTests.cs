// tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
using TextFix.Models;

namespace TextFix.Tests.Models;

public class CorrectionHistoryTests
{
    [Fact]
    public void Add_StoresResult()
    {
        var history = new CorrectionHistory();
        var result = new CorrectionResult
        {
            OriginalText = "hello wrold",
            CorrectedText = "hello world",
        };

        history.Add(result);

        Assert.Single(history.Items);
        Assert.Equal("hello world", history.Items[0].CorrectedText);
    }

    [Fact]
    public void Add_NewestFirst()
    {
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult { OriginalText = "a", CorrectedText = "A" });
        history.Add(new CorrectionResult { OriginalText = "b", CorrectedText = "B" });

        Assert.Equal("B", history.Items[0].CorrectedText);
        Assert.Equal("A", history.Items[1].CorrectedText);
    }

    [Fact]
    public void Add_CapsAt10()
    {
        var history = new CorrectionHistory();
        for (int i = 0; i < 15; i++)
        {
            history.Add(new CorrectionResult
            {
                OriginalText = $"orig{i}",
                CorrectedText = $"fixed{i}",
            });
        }

        Assert.Equal(10, history.Items.Count);
        Assert.Equal("fixed14", history.Items[0].CorrectedText);
        Assert.Equal("fixed5", history.Items[9].CorrectedText);
    }

    [Fact]
    public void Add_SkipsErrors()
    {
        var history = new CorrectionHistory();
        history.Add(CorrectionResult.Error("text", "Something broke"));

        Assert.Empty(history.Items);
    }

    [Fact]
    public void Add_SkipsNoChanges()
    {
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult
        {
            OriginalText = "already correct",
            CorrectedText = "already correct",
        });

        Assert.Empty(history.Items);
    }

    [Fact]
    public void TotalCount_IncrementsOnAdd()
    {
        var history = new CorrectionHistory();
        for (int i = 0; i < 3; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(3, history.TotalCount);
    }

    [Fact]
    public void TotalCount_CountsEvictedItems()
    {
        var history = new CorrectionHistory();
        for (int i = 0; i < 15; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(10, history.Items.Count);
        Assert.Equal(15, history.TotalCount);
    }

    [Fact]
    public void TotalCount_SkipsErrorsAndNoChanges()
    {
        var history = new CorrectionHistory();
        history.Add(CorrectionResult.Error("x", "err"));
        history.Add(new CorrectionResult { OriginalText = "same", CorrectedText = "same" });
        history.Add(new CorrectionResult { OriginalText = "a", CorrectedText = "b" });

        Assert.Equal(1, history.TotalCount);
    }

    [Fact]
    public void TodayCount_CountsOnlyTodaysCorrections()
    {
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult { OriginalText = "a", CorrectedText = "b" });
        history.Add(new CorrectionResult
        {
            OriginalText = "c",
            CorrectedText = "d",
            Timestamp = DateTime.UtcNow.AddDays(-1),
        });

        Assert.Equal(1, history.TodayCount);
        Assert.Equal(2, history.TotalCount);
    }
}
