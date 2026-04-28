# TextFix — Colored Inline/Line Diff

## Goal

Replace the current "Corrected | Original (full strikethrough)" tab pair with a true visual diff so users can see *what* changed at a glance instead of eyeball-comparing two full versions. Diff style adapts to content shape (single-line vs multi-line), and is suppressed entirely when the change ratio is too high (e.g. a "rewrite concisely" prompt where almost everything differs and a diff would just be noise).

## Background

Today the result panel shows:

- **Corrected** tab — an editable `TextBox` with the new text.
- **Original** tab — a `TextBlock` with the entire original wrapped in `TextDecorations="Strikethrough"`.

For long inputs, the Original tab is unreadable, and there's no highlighting of the actual edits — the user has to flip tabs and compare line-for-line.

## Decision Logic

Computed once in `OverlayWindow.ShowResult` per result:

```text
ratio    = (removed_words + added_words) / max(total_orig_words, 1)
multiline = original.Contains('\n') || corrected.Contains('\n')

if ratio > settings.DiffMaxChangeRatio:
    render plain corrected text (no diff markup)
elif multiline:
    render unified line diff with intra-line word highlights
else:
    render inline word diff
```

The Original tab remains as-is regardless, providing a fallback view of the raw input.

## Diff Algorithm

New file: `src/TextFix/Services/DiffEngine.cs`. Hand-rolled word-level Myers/LCS diff (~80 lines), no NuGet dependency.

- Tokenization: split on whitespace boundaries, but keep the whitespace runs as their own tokens so the rendered output preserves the original spacing/newlines.
- Output: `List<DiffSegment>` where `DiffSegment` is `(DiffKind kind, string text)` and `DiffKind ∈ { Equal, Removed, Added }`.
- Helper: `DiffStats(originalWordCount, removedWordCount, addedWordCount)` returned alongside, so the caller can compute the change ratio without a second pass.

## Rendering

Replace `CorrectedText` (currently a `TextBox`) with a `RichTextBox` bound to a `FlowDocument`. The `RichTextBox` is read-only in result view and editable in refine mode (same toggle path as today via `ApplyEditableState`).

### A — Inline word diff (single-line, low ratio)

One `Paragraph`, a sequence of `Run`s:

| Kind    | Foreground | Background           | Decoration       |
|---------|------------|----------------------|------------------|
| Equal   | `#E0E0E0`  | transparent          | none             |
| Removed | `#F87171`  | `#1FF87171` (12% α)  | strikethrough    |
| Added   | `#4ADE80`  | `#1F4ADE80` (12% α)  | none             |

### B — Unified line diff (multi-line, low ratio)

The diff is grouped into logical lines. Each line becomes its own `Paragraph`:

- **Removed line:** `-` gutter character, foreground `#F87171`, line background `#1FF87171`.
- **Added line:** `+` gutter character, foreground `#4ADE80`, line background `#1F4ADE80`.
- **Equal line:** no gutter, normal foreground.
- **Within a changed line:** when a removed line is immediately followed by an added line (the typical "this line was modified" shape), run the word-level diff *between that pair* and render the differing words with a stronger tint (`#3FF87171` / `#3F4ADE80`) over the line's base background. Unpaired removed-only or added-only lines stay flat (no inner highlight needed).
- **Logical line** = a line as separated by `\n` in the original or corrected text. The diff engine emits segments at word granularity; the renderer groups them into lines for the unified view.

### C — High-ratio path (no diff)

Plain `Run`s in normal foreground, identical to current behavior. The Original tab still works for users who want to see the input.

## Editing in Refine Mode

When `ApplyEditableState(true)` runs, the `FlowDocument` is reset to a single `Paragraph` containing the corrected text as plain `Run`s. Editing styled diff segments inline would be confusing and would mangle the markup. On exit/cancel of refine, the diff re-renders from `_currentResult`.

`GetEditedText()` reads back the `FlowDocument` text via a `TextRange` over the document range — replaces the current `CorrectedText.Text` access.

## Settings

New property in `Models/AppSettings.cs`:

```csharp
public double DiffMaxChangeRatio { get; set; } = 0.30;
```

Clamped to `[0.05, 1.0]` on load (in the same place existing properties are validated). A value of `1.0` effectively means "always show a diff."

`Views/SettingsWindow.xaml` gets a new control in the General section, below the auto-apply field:

```xml
<Slider Minimum="5" Maximum="100" TickFrequency="5"
        IsSnapToTickEnabled="True"
        Value="{Binding DiffThresholdPercent}"/>
<TextBlock Text="{Binding DiffThresholdPercent, StringFormat={}{0}%}"/>
```

Label: **"Diff threshold"**. Help text: *"Above this change ratio, just show the corrected text instead of a diff."*

The slider works in integer percent (5–100, snap 5); the underlying setting is the 0.05–1.00 double. Conversion happens in the SettingsWindow code-behind on save.

## Tests

New file: `tests/TextFix.Tests/Services/DiffEngineTests.cs`.

Cases:

1. Empty original + empty corrected → empty segment list, ratio 0.
2. Identical strings → all `Equal` segments, ratio 0.
3. Single word swap (`"the brown fox" → "the swift fox"`) → `Equal "the "`, `Removed "brown"`, `Added "swift"`, `Equal " fox"`, with 1 removed + 1 added words.
4. Pure addition (`"hello" → "hello world"`) → `Equal "hello"`, `Added " world"`, ratio = 1/1 = 1.0.
5. Pure removal — symmetric to #4.
6. Whitespace preservation — newlines and tab runs round-trip through the segment list.
7. Ratio math — `(removed+added)/orig` matches what `DiffStats` reports for a constructed case.

Total test count: 23 → ~30.

## Files Touched

| File                                         | Change |
|----------------------------------------------|--------|
| `src/TextFix/Services/DiffEngine.cs`         | new — algorithm + types |
| `src/TextFix/Views/OverlayWindow.xaml`       | replace `CorrectedText` TextBox with RichTextBox |
| `src/TextFix/Views/OverlayWindow.xaml.cs`    | `RenderDiff` method, threshold plumbing, `GetEditedText` rewrite |
| `src/TextFix/Views/SettingsWindow.xaml`      | new slider + label |
| `src/TextFix/Views/SettingsWindow.xaml.cs`   | bind/save percent ↔ ratio |
| `src/TextFix/Models/AppSettings.cs`          | new `DiffMaxChangeRatio` property + clamp |
| `tests/TextFix.Tests/Services/DiffEngineTests.cs` | new |

## Out of Scope

- Character-level (sub-word) diff — stays at word granularity for now.
- Side-by-side two-pane diff layout — single-pane only.
- Per-mode threshold overrides — one global threshold setting.
- Configurable diff colors — fixed palette matching the existing overlay theme.
