# Colored Inline/Line Diff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the strikethrough-original / plain-corrected tab pair with a true colored diff (inline word diff for single-line corrections, unified line diff for multi-line, no diff at all when the change ratio exceeds a configurable threshold).

**Architecture:** Hand-rolled word-level Myers/LCS diff in a new `DiffEngine` service. Result panel's `CorrectedText` becomes a `RichTextBox` whose `FlowDocument` is rendered three ways depending on (a) the change ratio vs. `AppSettings.DiffMaxChangeRatio` and (b) whether either text contains a newline. Refine/edit mode resets the document to plain text so editing is straightforward. Threshold exposed in Settings as a 5–100% slider, default 30%.

**Tech Stack:** .NET 10 WPF, WinForms (existing), no new NuGet packages, xUnit for tests.

---

## File Structure

| File | Role |
|------|------|
| `src/TextFix/Services/DiffEngine.cs` | **new** — `DiffEngine.Compute(string, string)` returns `DiffResult` (segments + stats); `DiffSegment` record, `DiffKind` enum |
| `src/TextFix/Models/AppSettings.cs` | add `DiffMaxChangeRatio` (double, 0.30 default), clamp `[0.05, 1.0]` on load |
| `src/TextFix/Views/OverlayWindow.xaml` | replace `CorrectedText` `TextBox` with a `RichTextBox`; add diff brush resources |
| `src/TextFix/Views/OverlayWindow.xaml.cs` | new `RenderDiff` method, `DiffMaxChangeRatio` property, rewritten `GetEditedText`, plain-text reset on editable mode |
| `src/TextFix/Views/SettingsWindow.xaml` | new slider + percent label below auto-apply |
| `src/TextFix/Views/SettingsWindow.xaml.cs` | bind slider value, persist as `ratio = percent / 100.0` |
| `src/TextFix/App.xaml.cs` | set `_overlay.DiffMaxChangeRatio` from settings on init and after settings save |
| `tests/TextFix.Tests/Services/DiffEngineTests.cs` | **new** |
| `tests/TextFix.Tests/Models/AppSettingsTests.cs` | extend with clamp tests |

---

## Task 1: Add `DiffMaxChangeRatio` to AppSettings

**Files:**
- Modify: `src/TextFix/Models/AppSettings.cs`
- Modify: `tests/TextFix.Tests/Models/AppSettingsTests.cs`

- [ ] **Step 1: Write failing test for default value**

Add to `tests/TextFix.Tests/Models/AppSettingsTests.cs`:

```csharp
[Fact]
public void DiffMaxChangeRatio_DefaultsTo30Percent()
{
    var settings = new AppSettings();
    Assert.Equal(0.30, settings.DiffMaxChangeRatio, 3);
}
```

- [ ] **Step 2: Run test — expect compile failure (property doesn't exist)**

Run from repo root:
```
dotnet test --filter FullyQualifiedName~AppSettingsTests.DiffMaxChangeRatio_DefaultsTo30Percent
```
Expected: build error `'AppSettings' does not contain a definition for 'DiffMaxChangeRatio'`.

- [ ] **Step 3: Add the property**

In `src/TextFix/Models/AppSettings.cs`, just after the `OverlayLeft`/`OverlayTop` block (around line 42), add:

```csharp
public double DiffMaxChangeRatio { get; set; } = 0.30;
```

- [ ] **Step 4: Re-run test, expect PASS**

```
dotnet test --filter FullyQualifiedName~AppSettingsTests.DiffMaxChangeRatio_DefaultsTo30Percent
```
Expected: 1 passed.

- [ ] **Step 5: Write failing tests for the clamp**

Add to `AppSettingsTests.cs`:

```csharp
[Theory]
[InlineData(0.0, 0.05)]    // below floor → floor
[InlineData(0.04, 0.05)]
[InlineData(0.05, 0.05)]   // floor exact
[InlineData(0.30, 0.30)]   // unchanged
[InlineData(1.0, 1.0)]     // ceiling exact
[InlineData(2.5, 1.0)]     // above ceiling → ceiling
[InlineData(-0.5, 0.05)]   // negative → floor
public async Task LoadAsync_ClampsDiffMaxChangeRatio(double stored, double expected)
{
    var path = Path.GetTempFileName();
    try
    {
        var seed = new AppSettings { DiffMaxChangeRatio = stored };
        await seed.SaveAsync(path);

        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(expected, loaded.DiffMaxChangeRatio, 3);
    }
    finally
    {
        File.Delete(path);
    }
}
```

- [ ] **Step 6: Run test — expect 4 failures** (the four out-of-range cases)

```
dotnet test --filter FullyQualifiedName~AppSettingsTests.LoadAsync_ClampsDiffMaxChangeRatio
```
Expected: 3 passed (in-range), 4 failed (out-of-range stay un-clamped without code).

- [ ] **Step 7: Add the clamp in `LoadAsync`**

In `src/TextFix/Models/AppSettings.cs`, inside the `try` block of `LoadAsync`, after `var settings = JsonSerializer.Deserialize<AppSettings>(...)` and before the API key migration block, insert:

```csharp
// Clamp DiffMaxChangeRatio to a sane range (5%..100%).
settings.DiffMaxChangeRatio = Math.Clamp(settings.DiffMaxChangeRatio, 0.05, 1.0);
```

- [ ] **Step 8: Re-run test, expect all PASS**

```
dotnet test --filter FullyQualifiedName~AppSettingsTests.LoadAsync_ClampsDiffMaxChangeRatio
```
Expected: 7 passed.

- [ ] **Step 9: Run the full suite**

```
dotnet test
```
Expected: all existing tests pass, two new test methods reported.

- [ ] **Step 10: Commit**

```
git add src/TextFix/Models/AppSettings.cs tests/TextFix.Tests/Models/AppSettingsTests.cs
git commit -m "feat(settings): add DiffMaxChangeRatio with 30% default and load-time clamp"
```

---

## Task 2: Diff types

**Files:**
- Create: `src/TextFix/Services/DiffEngine.cs`
- Create: `tests/TextFix.Tests/Services/DiffEngineTests.cs`

- [ ] **Step 1: Write a smoke test that just constructs an empty result**

Create `tests/TextFix.Tests/Services/DiffEngineTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test --filter FullyQualifiedName~DiffEngineTests.DiffSegment_HoldsKindAndText
```
Expected: build error referencing `DiffSegment` / `DiffKind`.

- [ ] **Step 3: Create the types**

Create `src/TextFix/Services/DiffEngine.cs`:

```csharp
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
```

- [ ] **Step 4: Re-run test, expect PASS**

```
dotnet test --filter FullyQualifiedName~DiffEngineTests.DiffSegment_HoldsKindAndText
```
Expected: 1 passed.

- [ ] **Step 5: Commit**

```
git add src/TextFix/Services/DiffEngine.cs tests/TextFix.Tests/Services/DiffEngineTests.cs
git commit -m "feat(diff): add DiffSegment, DiffKind, DiffStats, DiffResult types"
```

---

## Task 3: Diff algorithm

**Files:**
- Modify: `src/TextFix/Services/DiffEngine.cs`
- Modify: `tests/TextFix.Tests/Services/DiffEngineTests.cs`

- [ ] **Step 1: Write failing tests covering identity, swap, addition, removal, whitespace, and ratio**

Append to `DiffEngineTests.cs`:

```csharp
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

    // Reconstructing equal+removed should give the original
    var rebuiltOriginal = string.Concat(
        result.Segments.Where(s => s.Kind != DiffKind.Added).Select(s => s.Text));
    Assert.Equal("the brown fox", rebuiltOriginal);

    // Reconstructing equal+added should give the corrected
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
```

- [ ] **Step 2: Run tests — expect 7 failures (NotImplementedException)**

```
dotnet test --filter FullyQualifiedName~DiffEngineTests
```
Expected: 1 passed (Step 1 of Task 2 still passes), 7 failed.

- [ ] **Step 3: Replace the `NotImplementedException` with the algorithm**

In `src/TextFix/Services/DiffEngine.cs`, replace the `Compute` body and add helpers. Final file:

```csharp
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

        return new DiffResult(segments, new DiffStats(origWords, removedWords, addedWords));
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
```

- [ ] **Step 4: Run tests, expect all PASS**

```
dotnet test --filter FullyQualifiedName~DiffEngineTests
```
Expected: 8 passed.

- [ ] **Step 5: Run full suite to check no regression**

```
dotnet test
```
Expected: all green, count went from 23 → 31.

- [ ] **Step 6: Commit**

```
git add src/TextFix/Services/DiffEngine.cs tests/TextFix.Tests/Services/DiffEngineTests.cs
git commit -m "feat(diff): word-level Myers diff with whitespace preservation"
```

---

## Task 4: Replace `CorrectedText` TextBox with RichTextBox (no diff yet)

This task is a behavior-preserving refactor — the result panel should still look identical at the end. It isolates the WPF control swap from the rendering changes that follow.

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml` (lines 297–318 region)
- Modify: `src/TextFix/Views/OverlayWindow.xaml.cs` (`ApplyEditableState`, `GetEditedText`, the line `CorrectedText.Text = result.CorrectedText;`)

- [ ] **Step 1: Replace the TextBox in XAML with a RichTextBox**

In `src/TextFix/Views/OverlayWindow.xaml`, replace the `<TabItem Header="Corrected">` block (currently lines 300–309) with:

```xml
<TabItem Header="Corrected" Style="{StaticResource OverlayTabItem}">
    <ScrollViewer VerticalScrollBarVisibility="Auto"
                  HorizontalScrollBarVisibility="Disabled">
        <RichTextBox x:Name="CorrectedText" Foreground="#E0E0E0"
                     Background="Transparent" BorderBrush="Transparent"
                     CaretBrush="#6C63FF" IsReadOnly="True" IsDocumentEnabled="True"
                     FontFamily="Segoe UI" FontSize="12" Padding="0"
                     VerticalScrollBarVisibility="Disabled"
                     HorizontalScrollBarVisibility="Disabled">
            <RichTextBox.Resources>
                <Style TargetType="Paragraph">
                    <Setter Property="Margin" Value="0"/>
                </Style>
            </RichTextBox.Resources>
            <FlowDocument PageWidth="2000"/>
        </RichTextBox>
    </ScrollViewer>
</TabItem>
```

> Note: `PageWidth="2000"` together with `HorizontalScrollBarVisibility="Disabled"` keeps the document narrow enough to wrap inside the overlay. The outer `ScrollViewer` does the actual scrolling.

- [ ] **Step 2: Add a small helper to set/get plain text on the RichTextBox**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, add the following private helpers near the top of the class body (just after the existing `GetEditedText` method around line 154):

```csharp
private void SetCorrectedPlainText(string text)
{
    var doc = new FlowDocument { PageWidth = 2000 };
    var para = new System.Windows.Documents.Paragraph(
        new System.Windows.Documents.Run(text ?? ""))
    { Margin = new Thickness(0) };
    doc.Blocks.Add(para);
    CorrectedText.Document = doc;
}

private string GetCorrectedDocumentText()
{
    var range = new System.Windows.Documents.TextRange(
        CorrectedText.Document.ContentStart,
        CorrectedText.Document.ContentEnd);
    // RichTextBox introduces a trailing "\r\n" — trim it so callers see plain text.
    return range.Text.TrimEnd('\r', '\n');
}
```

- [ ] **Step 3: Update `ApplyEditableState` for the new control**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, replace the existing `ApplyEditableState` method (lines 135–152) with:

```csharp
private void ApplyEditableState(bool editable)
{
    CorrectedText.IsReadOnly = !editable;
    if (editable)
    {
        CorrectedText.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x23, 0x23, 0x36));
        CorrectedText.BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x55, 0x55, 0x55));
        CorrectedText.BorderThickness = new Thickness(1);
        CorrectedText.Padding = new Thickness(6, 4, 6, 4);

        // Editing a styled diff document is confusing. Reset to plain text.
        if (_currentResult is not null)
            SetCorrectedPlainText(_currentResult.CorrectedText);
    }
    else
    {
        CorrectedText.Background = WpfMedia.Brushes.Transparent;
        CorrectedText.BorderBrush = WpfMedia.Brushes.Transparent;
        CorrectedText.BorderThickness = new Thickness(0);
        CorrectedText.Padding = new Thickness(0);
    }
}
```

- [ ] **Step 4: Update `GetEditedText`**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, replace the existing `GetEditedText` (line 154):

```csharp
public string GetEditedText() => GetCorrectedDocumentText();
```

- [ ] **Step 5: Update the place that assigned `CorrectedText.Text`**

In `ShowResult` (around line 265), replace:

```csharp
CorrectedText.Text = result.CorrectedText;
```

with:

```csharp
SetCorrectedPlainText(result.CorrectedText);
```

- [ ] **Step 6: Search for any other `CorrectedText.Text` references**

```
grep -n "CorrectedText\.Text" src/TextFix/Views/OverlayWindow.xaml.cs
```
Expected: only line 717 (`var text = CorrectedText.Text;` inside `OnActionCopy`). Replace it with:

```csharp
var text = GetCorrectedDocumentText();
```

- [ ] **Step 7: Build**

```
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```
Expected: Build succeeded. 0 Errors.

- [ ] **Step 8: Run all tests**

```
dotnet test
```
Expected: 31 passed (no UI regression at unit-test level).

- [ ] **Step 9: Manual smoke test**

```
dotnet run --project src/TextFix/TextFix.csproj
```

In any text field (e.g. Notepad), type `helo wrld`, select it, press Ctrl+Shift+Z. Verify:
- Result panel shows the corrected text in the Corrected tab (plain, like before).
- "Original" tab still strikethrough.
- Apply / Cancel still work, copy still works.
- Toggling "Manual only — let me edit the output" in Settings then triggering a correction lets you edit the corrected text.
- Pressing Apply applies the edited text.

If anything is off, debug before committing.

- [ ] **Step 10: Commit**

```
git add src/TextFix/Views/OverlayWindow.xaml src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "refactor(overlay): switch corrected pane to RichTextBox, behavior-preserving"
```

---

## Task 5: Render inline word diff (single-line, low ratio)

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Add `DiffMaxChangeRatio` property and `RenderDiff` method**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, add at the top of the class body alongside the other fields (around line 25):

```csharp
public double DiffMaxChangeRatio { get; set; } = 0.30;
```

Then add a new method (place it near `SetCorrectedPlainText`):

```csharp
private void RenderDiff(string original, string corrected)
{
    var diff = TextFix.Services.DiffEngine.Compute(original, corrected);
    bool multiline = original.Contains('\n') || corrected.Contains('\n');

    if (diff.Stats.ChangeRatio > DiffMaxChangeRatio)
    {
        // High change — diff would be noise. Just show corrected text.
        SetCorrectedPlainText(corrected);
        return;
    }

    if (multiline)
    {
        RenderUnifiedLineDiff(diff);   // implemented in Task 6
    }
    else
    {
        RenderInlineWordDiff(diff);
    }
}

private static readonly WpfMedia.Brush EqualBrush =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0));
private static readonly WpfMedia.Brush RemovedBrush =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xF8, 0x71, 0x71));
private static readonly WpfMedia.Brush AddedBrush =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
private static readonly WpfMedia.Brush RemovedBg =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x1F, 0xF8, 0x71, 0x71));
private static readonly WpfMedia.Brush AddedBg =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x1F, 0x4A, 0xDE, 0x80));

private void RenderInlineWordDiff(TextFix.Services.DiffResult diff)
{
    var doc = new FlowDocument { PageWidth = 2000 };
    var para = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };

    foreach (var seg in diff.Segments)
    {
        var run = new System.Windows.Documents.Run(seg.Text);
        switch (seg.Kind)
        {
            case TextFix.Services.DiffKind.Equal:
                run.Foreground = EqualBrush;
                break;
            case TextFix.Services.DiffKind.Removed:
                run.Foreground = RemovedBrush;
                run.Background = RemovedBg;
                run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                break;
            case TextFix.Services.DiffKind.Added:
                run.Foreground = AddedBrush;
                run.Background = AddedBg;
                break;
        }
        para.Inlines.Add(run);
    }
    doc.Blocks.Add(para);
    CorrectedText.Document = doc;
}

private void RenderUnifiedLineDiff(TextFix.Services.DiffResult diff)
{
    // Implemented in Task 6 — for now fall back to plain corrected text.
    var corrected = string.Concat(
        diff.Segments
            .Where(s => s.Kind != TextFix.Services.DiffKind.Removed)
            .Select(s => s.Text));
    SetCorrectedPlainText(corrected);
}
```

- [ ] **Step 2: Wire `RenderDiff` into `ShowResult`**

In `ShowResult`, replace the line you just added in Task 5 (around line 265):

```csharp
SetCorrectedPlainText(result.CorrectedText);
```

with:

```csharp
RenderDiff(result.OriginalText, result.CorrectedText);
```

- [ ] **Step 3: Build**

```
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```
Expected: 0 errors.

- [ ] **Step 4: Run all unit tests**

```
dotnet test
```
Expected: 31 passed.

- [ ] **Step 5: Manual smoke test (single-line low-ratio path)**

```
dotnet run --project src/TextFix/TextFix.csproj
```

In Notepad type `the brown fox jumped ovr the lazy dog`, select, Ctrl+Shift+Z. In the Corrected tab, verify:
- "brown" or whatever model swaps appears in red strikethrough.
- The replacement word appears in green.
- Surrounding words appear in normal off-white.
- Apply still inserts the correct corrected text into Notepad.
- Toggle "Manual only" in settings, repeat — verify editing mode shows plain text (no colored runs) so you can type freely.

- [ ] **Step 6: Commit**

```
git add src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "feat(overlay): inline word diff with red/green highlighting"
```

---

## Task 6: Render unified line diff (multi-line, low ratio)

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Replace the placeholder `RenderUnifiedLineDiff` with the real implementation**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, replace the placeholder body of `RenderUnifiedLineDiff` (added in Task 5) with:

```csharp
private void RenderUnifiedLineDiff(TextFix.Services.DiffResult diff)
{
    // Step 1: rebuild original and corrected line lists from segments
    // so we can do a *line-level* pass on top of the word-level diff.
    var origText = string.Concat(
        diff.Segments
            .Where(s => s.Kind != TextFix.Services.DiffKind.Added)
            .Select(s => s.Text));
    var corrText = string.Concat(
        diff.Segments
            .Where(s => s.Kind != TextFix.Services.DiffKind.Removed)
            .Select(s => s.Text));

    var origLines = origText.Split('\n');
    var corrLines = corrText.Split('\n');

    // Step 2: line-level LCS to classify each line as Equal/Removed/Added.
    int n = origLines.Length, m = corrLines.Length;
    var dp = new int[n + 1, m + 1];
    for (int i = n - 1; i >= 0; i--)
    {
        for (int j = m - 1; j >= 0; j--)
        {
            if (origLines[i] == corrLines[j])
                dp[i, j] = dp[i + 1, j + 1] + 1;
            else
                dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
        }
    }

    var lineOps = new List<(TextFix.Services.DiffKind kind, string text)>();
    int x = 0, y = 0;
    while (x < n && y < m)
    {
        if (origLines[x] == corrLines[y])
        {
            lineOps.Add((TextFix.Services.DiffKind.Equal, origLines[x]));
            x++; y++;
        }
        else if (dp[x + 1, y] >= dp[x, y + 1])
        {
            lineOps.Add((TextFix.Services.DiffKind.Removed, origLines[x++]));
        }
        else
        {
            lineOps.Add((TextFix.Services.DiffKind.Added, corrLines[y++]));
        }
    }
    while (x < n) lineOps.Add((TextFix.Services.DiffKind.Removed, origLines[x++]));
    while (y < m) lineOps.Add((TextFix.Services.DiffKind.Added, corrLines[y++]));

    // Step 3: build paragraphs. For paired removed-then-added lines,
    // run an inner word diff to highlight just the changed words.
    var doc = new FlowDocument { PageWidth = 2000 };

    for (int k = 0; k < lineOps.Count; k++)
    {
        var op = lineOps[k];

        if (op.kind == TextFix.Services.DiffKind.Equal)
        {
            doc.Blocks.Add(BuildLineParagraph(
                gutter: "  ",
                inlines: new[] { MakeRun(op.text, EqualBrush, null, false) },
                lineBg: null));
        }
        else if (op.kind == TextFix.Services.DiffKind.Removed
                 && k + 1 < lineOps.Count
                 && lineOps[k + 1].kind == TextFix.Services.DiffKind.Added)
        {
            // Paired modify: inner word diff between the two lines.
            var inner = TextFix.Services.DiffEngine.Compute(op.text, lineOps[k + 1].text);

            var removedInlines = new List<System.Windows.Documents.Inline>();
            var addedInlines = new List<System.Windows.Documents.Inline>();
            foreach (var seg in inner.Segments)
            {
                if (seg.Kind == TextFix.Services.DiffKind.Equal)
                {
                    removedInlines.Add(MakeRun(seg.Text, RemovedBrush, null, true));
                    addedInlines.Add(MakeRun(seg.Text, AddedBrush, null, false));
                }
                else if (seg.Kind == TextFix.Services.DiffKind.Removed)
                {
                    removedInlines.Add(MakeRun(seg.Text, RemovedBrush, RemovedHighlightBg, true));
                }
                else // Added
                {
                    addedInlines.Add(MakeRun(seg.Text, AddedBrush, AddedHighlightBg, false));
                }
            }

            doc.Blocks.Add(BuildLineParagraph("- ", removedInlines, RemovedBg));
            doc.Blocks.Add(BuildLineParagraph("+ ", addedInlines, AddedBg));
            k++; // we consumed the next op as the pair
        }
        else if (op.kind == TextFix.Services.DiffKind.Removed)
        {
            doc.Blocks.Add(BuildLineParagraph(
                gutter: "- ",
                inlines: new[] { MakeRun(op.text, RemovedBrush, null, true) },
                lineBg: RemovedBg));
        }
        else // Added (no preceding Removed)
        {
            doc.Blocks.Add(BuildLineParagraph(
                gutter: "+ ",
                inlines: new[] { MakeRun(op.text, AddedBrush, null, false) },
                lineBg: AddedBg));
        }
    }

    CorrectedText.Document = doc;
}

private static readonly WpfMedia.Brush GutterBrush =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x88, 0x88, 0x88));
private static readonly WpfMedia.Brush RemovedHighlightBg =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x3F, 0xF8, 0x71, 0x71));
private static readonly WpfMedia.Brush AddedHighlightBg =
    new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x3F, 0x4A, 0xDE, 0x80));

private static System.Windows.Documents.Run MakeRun(
    string text,
    WpfMedia.Brush fg,
    WpfMedia.Brush? bg,
    bool strikethrough)
{
    var run = new System.Windows.Documents.Run(text) { Foreground = fg };
    if (bg is not null) run.Background = bg;
    if (strikethrough) run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
    return run;
}

private static System.Windows.Documents.Paragraph BuildLineParagraph(
    string gutter,
    IEnumerable<System.Windows.Documents.Inline> inlines,
    WpfMedia.Brush? lineBg)
{
    var para = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };
    if (lineBg is not null) para.Background = lineBg;
    para.Inlines.Add(new System.Windows.Documents.Run(gutter)
    {
        Foreground = GutterBrush,
        FontFamily = new WpfMedia.FontFamily("Consolas"),
    });
    foreach (var inline in inlines)
        para.Inlines.Add(inline);
    return para;
}
```

- [ ] **Step 2: Build**

```
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```
Expected: 0 errors.

- [ ] **Step 3: Run unit tests**

```
dotnet test
```
Expected: 31 passed.

- [ ] **Step 4: Manual smoke test (multi-line)**

Run the app. In Notepad type a 3-line block such as:

```
First line has a tpyo
Second line is fine
Third line also has eror in it
```

Select all three lines, Ctrl+Shift+Z. Verify in the Corrected tab:
- Equal lines (e.g. line 2) appear normal with no `+`/`-` prefix and no background.
- Modified lines appear as a `-` red line followed by a `+` green line; *within* those lines, only the actual changed word is strongly highlighted.
- Apply replaces all three lines with the corrected text (sanity check).

- [ ] **Step 5: Commit**

```
git add src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "feat(overlay): unified line diff with intra-line word highlights for multi-line"
```

---

## Task 7: Settings UI — diff threshold slider

**Files:**
- Modify: `src/TextFix/Views/SettingsWindow.xaml`
- Modify: `src/TextFix/Views/SettingsWindow.xaml.cs`

- [ ] **Step 1: Add the slider XAML below the auto-apply field**

In `src/TextFix/Views/SettingsWindow.xaml`, after the closing `</StackPanel>` of the auto-apply field (currently around line 189) and before the `Manual only` checkbox StackPanel, insert:

```xml
<StackPanel>
    <Label Content="Diff threshold (% of words changed; above this, just show the corrected text)"/>
    <Grid Margin="0,4,0,12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <Slider x:Name="DiffThresholdSlider" Grid.Column="0"
                Minimum="5" Maximum="100" TickFrequency="5"
                IsSnapToTickEnabled="True"
                ValueChanged="OnDiffThresholdChanged"
                VerticalAlignment="Center"/>
        <TextBlock x:Name="DiffThresholdLabel" Grid.Column="1"
                   Width="40" TextAlignment="Right" VerticalAlignment="Center"
                   Foreground="#E0E0E0" FontSize="12" Margin="8,0,0,0"
                   Text="30%"/>
    </Grid>
</StackPanel>
```

- [ ] **Step 2: Wire the load + change handler in code-behind**

In `src/TextFix/Views/SettingsWindow.xaml.cs`, find the section in the constructor / loading code where `AutoApplyBox.Text = ...` is set (around line 46) and add immediately after it:

```csharp
DiffThresholdSlider.Value = Math.Round(settings.DiffMaxChangeRatio * 100.0);
DiffThresholdLabel.Text = $"{(int)DiffThresholdSlider.Value}%";
```

Then add a new event handler method anywhere in the class (e.g., before `OnSave`):

```csharp
private void OnDiffThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (DiffThresholdLabel is null) return;
    DiffThresholdLabel.Text = $"{(int)e.NewValue}%";
}
```

- [ ] **Step 3: Persist the value in `OnSave`**

In `OnSave`, after the `_settings.OverlayAutoApplySeconds = ...` line (around line 254), insert:

```csharp
_settings.DiffMaxChangeRatio = Math.Clamp(DiffThresholdSlider.Value / 100.0, 0.05, 1.0);
```

- [ ] **Step 4: Build**

```
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```
Expected: 0 errors.

- [ ] **Step 5: Manual UI smoke test**

Run the app. Open Settings. Verify:
- Slider appears, default value 30, label reads "30%".
- Dragging the slider snaps in 5% increments and the label updates live.
- Click Save, re-open Settings — the value persists.
- Inspect `%APPDATA%/TextFix/settings.json` and confirm a `"DiffMaxChangeRatio": 0.30` (or whatever you set) entry exists.

- [ ] **Step 6: Commit**

```
git add src/TextFix/Views/SettingsWindow.xaml src/TextFix/Views/SettingsWindow.xaml.cs
git commit -m "feat(settings): diff threshold slider (5-100%, snaps in 5% increments)"
```

---

## Task 8: Wire threshold into the live overlay

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Find where the overlay is constructed and where settings are applied**

```
grep -n "_overlay\s*=\|_settings = \|SettingsWindow" src/TextFix/App.xaml.cs
```

Note the constructor site (where `_overlay = new OverlayWindow();` runs) and the place where settings are reloaded after the Settings dialog closes (`SettingsChanged` flag check or settings reassignment).

- [ ] **Step 2: Set `DiffMaxChangeRatio` after construction**

In `src/TextFix/App.xaml.cs`, immediately after `_overlay = new OverlayWindow();`, add:

```csharp
_overlay.DiffMaxChangeRatio = _settings.DiffMaxChangeRatio;
```

- [ ] **Step 3: Refresh the threshold after the user saves settings**

In the same file, find the post-Settings-save block (search for where `_overlay.RefreshModes(...)` is called, since that's the analogous existing wiring). Immediately before or after that call, add:

```csharp
_overlay.DiffMaxChangeRatio = _settings.DiffMaxChangeRatio;
```

- [ ] **Step 4: Build**

```
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```
Expected: 0 errors.

- [ ] **Step 5: End-to-end manual test**

Run the app. In Notepad type:

```
make this concise this is a wordy paragraph that needs to be much shorter overall
```

Select, switch overlay mode to "Concise" (or similar), Ctrl+Shift+Z. With the default 30% threshold and a high-change rewrite, the Corrected tab should show **plain** text (no diff markup) — the rewrite is too different to diff usefully.

Now open Settings, slide the threshold up to 100%, Save. Trigger another correction on a similarly-rewritten sentence. The Corrected tab should now show a colored diff (because every change is below the 100% bar).

Slide threshold down to 5%, Save. A simple typo correction (e.g. `helo wrld` → `hello world`, ratio = 2/2 = 100%) should now show plain corrected text instead of a diff.

- [ ] **Step 6: Commit**

```
git add src/TextFix/App.xaml.cs
git commit -m "feat(app): plumb DiffMaxChangeRatio from settings into overlay"
```

---

## Task 9: Final verification

- [ ] **Step 1: Full build + test**

```
taskkill /IM TextFix.exe /F 2>/dev/null
dotnet build
dotnet test
```
Expected: 0 errors, 31 tests passing.

- [ ] **Step 2: Regression sweep — verify previously-working flows**

Run `dotnet run --project src/TextFix/TextFix.csproj`. Exercise:
- Trigger a correction with a typo → inline diff visible.
- Trigger a correction with multi-line input → unified line diff visible.
- Trigger a correction with a "rewrite" prompt that changes most words → plain corrected text, no diff.
- Toggle "Manual only" → editable text area is plain (no colored markup).
- Click Apply → corrected text inserted into source app.
- Click Cancel → original text restored / nothing inserted.
- Click Copy → corrected text on clipboard, paste it elsewhere to verify.
- Open History → click an entry → diff renders for that historical correction.
- Reopen Settings → slider value persisted across app restart.

- [ ] **Step 3: Stop and report**

If any of the above fails, debug before declaring complete. Otherwise, the feature is ready for release-tag (out of scope for this plan).
