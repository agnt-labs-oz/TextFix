# TextFix v0.2.5 — Overlay Action Row, Inline History & Tray Left-Click

## Goal

Add quick-access controls to the overlay (redo, copy, history, close), an inline history panel, session stats, and tray left-click to open the overlay. Keep the correction flow fast — new controls only appear after a correction is applied or when opened from the tray.

## What Changes

### 1. Overlay Action Row

A compact row of buttons appears at the bottom of the overlay in two states:
- **After "Applied!"** — replaces the current plain "Press hotkey to correct more, Esc to close" text
- **Idle mode** — when opened via tray left-click (no correction in progress)

Buttons: **Redo** | **Copy** | **History** | _(spacer)_ | **Close**

Behavior:
- **Redo**: Re-fires `RetryRequested` (reuses the last captured text with the current mode)
- **Copy**: Copies `LastResult.CorrectedText` to clipboard, shows brief "Copied!" feedback
- **History**: Toggles the inline history panel visible/collapsed
- **Close**: Calls `FadeOutAndHide()` (same as Esc)

The action row does NOT appear during processing, result (apply/cancel), or error states — those keep their existing controls.

### 2. Inline History Panel

A scrollable list that appears between the info/applied content and the action row when the History button is toggled on. Shows the last 10 corrections from `CorrectionHistory.Items`.

Each item shows:
- Truncated corrected text (first ~60 chars)
- Mode name + relative timestamp ("Fix errors · 2 min ago")
- Click to copy that correction to clipboard (brief "Copied!" feedback)

Header line shows session stats: **"3 today · 12 total"**

### 3. Session Stats

Simple in-memory counters on `CorrectionHistory`:
- `TotalCount` — incremented on every successful correction (including ones evicted from the ring buffer)
- `TodayCount` — corrections since midnight (computed from timestamps)

To support timestamps, `CorrectionResult` gets a `DateTime Timestamp` property set at creation time.

### 4. Tray Left-Click

`NotifyIcon.MouseClick` handler: on left button click, opens the overlay in a new "idle" state positioned near the system tray (bottom-right of the primary screen). The overlay shows the action row with history accessible. If the overlay is already visible, left-click hides it instead (toggle).

Right-click continues to open the context menu as before.

### 5. Overlay Idle State

A new state (`_showingIdle`) for when the overlay is opened from the tray with no active correction. Shows:
- "TextFix" title with app icon/branding
- Session stats line ("3 corrections today · 12 total")
- Action row (Redo grayed out if no last result, Copy grayed out if no last result)
- History toggle

Keyboard: Esc closes. Enter does nothing.

## File Changes

| File | Change |
|------|--------|
| `Models/CorrectionResult.cs` | Add `DateTime Timestamp { get; init; }` with default `DateTime.UtcNow` |
| `Models/CorrectionHistory.cs` | Add `TotalCount` int, `TodayCount` computed property |
| `Views/OverlayWindow.xaml` | Add action row panel, history panel with ScrollViewer, idle state panel |
| `Views/OverlayWindow.xaml.cs` | Add `ShowIdle()`, history toggle, copy/redo/close handlers, idle state flag |
| `App.xaml.cs` | Add `NotifyIcon.MouseClick` handler for left-click, wire new overlay events |
| `Tests/CorrectionHistoryTests.cs` | Add tests for TotalCount, TodayCount |
| `Tests/CorrectionResultTests.cs` | Add test for Timestamp default |

## What's NOT in Scope

- Persistent history (disk storage) — v0.3
- Token/cost stats from API responses — v0.3
- Undo (revert last correction in target app) — v0.3
- Persistent stats across sessions — v0.3
