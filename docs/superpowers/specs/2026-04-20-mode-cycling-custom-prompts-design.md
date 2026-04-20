# TextFix v0.3 — Mode Cycling, Custom Prompts, Persistent History & Stats

## Goal

Let users iterate on text by re-applying different modes without re-capturing, create and save custom prompts, persist correction history across sessions, and show token/cost stats. Also update the model list to include Claude 4.7 models.

## What Changes

### 1. Mode Cycling (Re-run on Same Text)

After a correction completes, the user can switch modes and re-run without going back to the source app. Two re-run paths:

- **From original** — re-runs the originally captured text through a new mode (fresh start)
- **Refine** — re-runs the most recent corrected text through a new mode (chaining)

**Trigger points:**
- Changing the mode in the overlay's ComboBox during result view automatically re-runs from original
- Action row Redo splits into two: "Redo (original)" and "Redo (refine)"
- The inline custom prompt box (see below) runs on the original text in result view, or on the last corrected text in applied/idle view

**Implementation:**
- `CorrectionService` gets `ReapplyAsync(string text)` — same as `TriggerCorrectionAsync` but skips clipboard capture/focus tracking and uses the provided text directly
- `App.xaml.cs` fires `ReapplyAsync` with the appropriate text based on user action
- Overlay diff always shows comparison against the input text that was used

### 2. Custom Prompts — Inline + Saved

**Inline prompt box:** A `TextBox` in the overlay visible during result, applied, and idle states. Placeholder text: "Try a custom prompt...". User types an instruction and presses Enter or clicks a Go button. This runs `ReapplyAsync` using the typed text as the system prompt.

**Save flow:** After a custom prompt produces a successful result, a "Save as mode" link appears below the prompt box. Clicking it shows an inline name input. User types a name and presses Enter → the custom mode (name + prompt) is saved to `AppSettings.CustomModes` and appears in all mode selectors immediately.

**Custom modes in settings:** A new section in `SettingsWindow` below the existing Mode dropdown. Shows a scrollable list of user-created custom modes, each with Edit and Delete buttons. Edit expands to show name + prompt fields inline. The 6 built-in modes are not editable or deletable.

**Mode selectors:** Overlay ComboBox, tray context menu, and settings Mode dropdown all show built-in modes first, then a separator, then custom modes.

**Storage:** `AppSettings.CustomModes` — a `List<CorrectionMode>` serialized in `settings.json`.

`GetActiveMode()` searches `Defaults` first, then `CustomModes`.

### 3. Persistent History

Correction history survives app restarts. `CorrectionHistory` gains `SaveAsync()`/`LoadAsync()` methods that read/write a `history.json` file in the TextFix AppData folder.

**Format:** JSON array of `CorrectionResult` objects (OriginalText, CorrectedText, ModeName, Timestamp). Only the last 50 items are persisted (up from 10 in-memory cap).

**When to save:** After each successful correction is added. Debounce not needed — corrections are infrequent.

**When to load:** At app startup, before `SetupServices()`.

**Ring buffer change:** `CorrectionHistory.MaxItems` increases from 10 to 50. The overlay history panel still shows the most recent items with scroll.

**TotalCount persistence:** `TotalCount` is also saved/loaded so lifetime stats survive restarts.

### 4. Token/Cost Stats

Parse the Anthropic API response to extract token usage and display it.

**API response parsing:** `AnthropicClient.Messages.Create()` returns a `Message` object that includes `Usage.InputTokens` and `Usage.OutputTokens`. Capture these on `CorrectionResult`.

**New properties on `CorrectionResult`:**
- `int InputTokens { get; init; }` — defaults to 0
- `int OutputTokens { get; init; }` — defaults to 0

**Cost calculation:** Hardcode per-model pricing in a static lookup. Calculate cost from `(inputTokens * inputPrice + outputTokens * outputPrice)`. Show as `$0.0012` format.

**Display:**
- History panel items show token count: "Fix errors · 2 min ago · 847 tokens"
- Idle panel stats line: "3 today · 12 total · $0.05 session"
- Session cost is a running sum on `CorrectionHistory` (not persisted — resets on restart)

**Pricing table (per million tokens):**

| Model | Input | Output |
|-------|-------|--------|
| claude-haiku-4-5-20251001 | $0.80 | $4.00 |
| claude-sonnet-4-5-20250514 | $3.00 | $15.00 |
| claude-sonnet-4-6 | $3.00 | $15.00 |
| claude-sonnet-4-7 | $3.00 | $15.00 |
| claude-opus-4-6 | $15.00 | $75.00 |
| claude-opus-4-7 | $15.00 | $75.00 |

### 5. Model List Update

Add `claude-sonnet-4-7` and `claude-opus-4-7` to `SettingsWindow.KnownModels`. Default remains `claude-haiku-4-5-20251001`.

## File Changes

| File | Change |
|------|--------|
| `Models/CorrectionResult.cs` | Add `InputTokens`, `OutputTokens` properties |
| `Models/CorrectionHistory.cs` | Increase MaxItems to 50, add `SaveAsync()`/`LoadAsync()`, add `SessionCost` computed property, persist `TotalCount` |
| `Models/AppSettings.cs` | Add `CustomModes` list, update `GetActiveMode()` to search both lists |
| `Models/CorrectionMode.cs` | No changes needed — record already has Name + SystemPrompt |
| `Services/CorrectionService.cs` | Add `ReapplyAsync(string text)`, capture token usage from API response |
| `Services/AiClient.cs` | Return token counts from API response on `CorrectionResult` |
| `Views/OverlayWindow.xaml` | Add custom prompt TextBox + Go button, "Save as mode" link/name input, split Redo into "Redo (original)" / "Redo (refine)", token display in history items |
| `Views/OverlayWindow.xaml.cs` | Wire prompt box submit, save-mode flow, mode-change-triggers-rerun, new events (`ReapplyRequested`, `SaveModeRequested`), update stats display with cost |
| `Views/SettingsWindow.xaml` | Add custom modes management section (list + edit/delete) |
| `Views/SettingsWindow.xaml.cs` | Add 4.7 models, custom mode CRUD, rebuild mode list on changes |
| `App.xaml.cs` | Wire `ReapplyRequested` event, rebuild tray/overlay mode lists on custom mode save/delete, load history at startup |
| `Tests/AppSettingsTests.cs` | Tests for CustomModes persistence, GetActiveMode with customs |
| `Tests/CorrectionHistoryTests.cs` | Tests for SaveAsync/LoadAsync, TotalCount persistence, SessionCost |
| `Tests/CorrectionResultTests.cs` | Tests for InputTokens/OutputTokens defaults |

## What's NOT in Scope

- Multiple AI providers (OpenAI, etc.) — separate spec
- Start with Windows — separate feature
- Real-time auto-correction — future
