// tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
using System.IO;
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
    public void Add_CapsAt50()
    {
        var history = new CorrectionHistory();
        for (int i = 0; i < 55; i++)
        {
            history.Add(new CorrectionResult
            {
                OriginalText = $"orig{i}",
                CorrectedText = $"fixed{i}",
            });
        }

        Assert.Equal(50, history.Items.Count);
        Assert.Equal("fixed54", history.Items[0].CorrectedText);
        Assert.Equal("fixed5", history.Items[49].CorrectedText);
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
        for (int i = 0; i < 55; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(50, history.Items.Count);
        Assert.Equal(55, history.TotalCount);
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

    [Fact]
    public void SessionCost_SumsTokenCosts()
    {
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult
        {
            OriginalText = "a",
            CorrectedText = "b",
            InputTokens = 1000,
            OutputTokens = 500,
            ModeName = "Fix errors",
        });
        Assert.True(history.SessionCost > 0);
    }

    [Fact]
    public void MaxItems_Is50()
    {
        var history = new CorrectionHistory();
        for (int i = 0; i < 60; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(50, history.Items.Count);
        Assert.Equal(60, history.TotalCount);
    }

    [Fact]
    public void Constructor_RespectsMaxItems()
    {
        var history = new CorrectionHistory(maxItems: 5);
        for (int i = 0; i < 10; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(5, history.Items.Count);
        Assert.Equal(10, history.TotalCount);
        Assert.Equal("b9", history.Items[0].CorrectedText);
        Assert.Equal("b5", history.Items[4].CorrectedText);
    }

    [Fact]
    public void SetMaxItems_TrimsExisting()
    {
        var history = new CorrectionHistory(maxItems: 20);
        for (int i = 0; i < 20; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        history.SetMaxItems(5);

        Assert.Equal(5, history.Items.Count);
        Assert.Equal("b19", history.Items[0].CorrectedText);
        Assert.Equal("b15", history.Items[4].CorrectedText);
        // Lifetime counter is unaffected by trim — only the rolling window changed.
        Assert.Equal(20, history.TotalCount);
    }

    [Fact]
    public void Constructor_ClampsToMaxItemsCap()
    {
        var history = new CorrectionHistory(maxItems: 9999);
        for (int i = 0; i < CorrectionHistory.MaxItemsCap + 10; i++)
            history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

        Assert.Equal(CorrectionHistory.MaxItemsCap, history.Items.Count);
    }

    [Fact]
    public void Clear_EmptiesItemsAndResetsCounts()
    {
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult
        {
            OriginalText = "a",
            CorrectedText = "b",
            InputTokens = 100,
            OutputTokens = 50,
        });
        Assert.NotEmpty(history.Items);
        Assert.True(history.SessionCost > 0);

        history.Clear();

        Assert.Empty(history.Items);
        Assert.Equal(0, history.TotalCount);
        Assert.Equal(0m, history.SessionCost);
    }

    [Fact]
    public async Task LoadAsync_AppliesMaxItemsToOverflowingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"TextFixHistTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "history.json");
            // Write a file that has more entries than the new limit.
            var seeded = new CorrectionHistory(maxItems: 50);
            for (int i = 0; i < 30; i++)
                seeded.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });
            await seeded.SaveAsync(path);

            var loaded = await CorrectionHistory.LoadAsync(path, maxItems: 10);

            Assert.Equal(10, loaded.Items.Count);
            Assert.Equal("b29", loaded.Items[0].CorrectedText);
            // TotalCount preserved as-is — it's the lifetime counter, not the in-memory window.
            Assert.Equal(30, loaded.TotalCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsItems()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"TextFixHistTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "history.json");
            var history = new CorrectionHistory();
            history.Add(new CorrectionResult
            {
                OriginalText = "a",
                CorrectedText = "b",
                ModeName = "Fix errors",
                InputTokens = 100,
                OutputTokens = 50,
            });
            history.Add(new CorrectionResult
            {
                OriginalText = "c",
                CorrectedText = "d",
                ModeName = "Professional",
            });

            await history.SaveAsync(path);
            var loaded = await CorrectionHistory.LoadAsync(path);

            Assert.Equal(2, loaded.Items.Count);
            Assert.Equal(2, loaded.TotalCount);
            Assert.Equal("d", loaded.Items[0].CorrectedText);
            Assert.Equal("b", loaded.Items[1].CorrectedText);
            Assert.Equal("Professional", loaded.Items[0].ModeName);
            Assert.Equal(100, loaded.Items[1].InputTokens);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        var loaded = await CorrectionHistory.LoadAsync(path);

        Assert.Empty(loaded.Items);
        Assert.Equal(0, loaded.TotalCount);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenFileIsCorrupted()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"TextFixHistTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "bad.json");
            await File.WriteAllTextAsync(path, "not valid json {{{");
            var loaded = await CorrectionHistory.LoadAsync(path);

            Assert.Empty(loaded.Items);
            Assert.Equal(0, loaded.TotalCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
