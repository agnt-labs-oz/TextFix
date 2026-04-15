# TextFix — Design Specification

## Overview

TextFix is a Windows desktop application that lets users correct text in any app using AI. The core workflow: select text, press a hotkey, AI corrects it, corrected text replaces the original. Inspired by Rewrite for Mac, filling a gap — no polished Windows app currently owns this space.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Name | TextFix | Clean, available, descriptive |
| AI Provider (MVP) | Claude API (Haiku) | Fast, cheap, user already in Anthropic ecosystem |
| Hotkey | User-configurable, default Ctrl+Shift+C | Memorable, configurable avoids conflicts |
| Processing feedback | Hybrid overlay (pill → diff toast) | Small footprint during wait, full preview before commit |
| Focus change handling | Paste if same window, clipboard+notify if changed | Safe — never pastes into wrong app |
| Correction modes (MVP) | Fix errors only | Core pain point, simplest to get right |
| Settings | System tray → WPF window, backed by JSON | UI for non-technical users, JSON for power users |
| Framework | WPF / .NET / C# | Rich overlay styling, modern Windows UI |
| Architecture | Single-process monolith | Simple to build, debug, deploy; extract later if needed |

## Architecture

Single WPF application running as a system tray app (no main window).

### Components

```
┌─────────────────────────────────────────────┐
│                  TextFix App                │
│                                             │
│  ┌──────────┐   ┌──────────┐   ┌────────┐  │
│  │  Hotkey   │──>│ Clipboard│──>│  AI    │  │
│  │ Listener  │   │ Manager  │   │ Client │  │
│  └──────────┘   └──────────┘   └────────┘  │
│       │              │  ▲          │        │
│       │              │  │          │        │
│       ▼              ▼  │          ▼        │
│  ┌──────────┐   ┌──────────┐   ┌────────┐  │
│  │  System   │   │ Focus    │   │Floating│  │
│  │   Tray    │   │ Tracker  │   │Overlay │  │
│  └──────────┘   └──────────┘   └────────┘  │
│       │                                     │
│       ▼                                     │
│  ┌──────────┐                               │
│  │ Settings  │                               │
│  │ Manager   │                               │
│  └──────────┘                               │
└─────────────────────────────────────────────┘
```

### Correction Flow

1. User selects text in any app, presses Ctrl+Shift+C
2. **Hotkey Listener** catches it via Win32 `RegisterHotKey`
3. **Focus Tracker** records the active window handle (`GetForegroundWindow`)
4. **Clipboard Manager** saves current clipboard, simulates Ctrl+C, reads the selected text
5. **Floating Overlay** appears near the cursor as a small pill showing a spinner
6. **AI Client** sends text to Claude Haiku with a correction system prompt
7. Overlay expands to diff toast showing before/after with Apply (Enter) / Cancel (Esc)
8. On Apply: Focus Tracker checks if original window is still active
   - **Same window**: Clipboard Manager puts corrected text on clipboard, simulates Ctrl+V, restores original clipboard
   - **Different window**: Puts corrected text on clipboard, overlay shows "Correction ready — Ctrl+V to paste"
9. If user doesn't press Enter or Esc within 3 seconds of the diff toast appearing, the correction is auto-applied (configurable delay in settings, 0 = never auto-apply)

## Component Details

### Hotkey Listener
- Win32 `RegisterHotKey` P/Invoke for system-wide hotkey
- Default: Ctrl+Shift+C, stored in settings JSON, changeable at runtime
- On hotkey press, fires an event that kicks off the correction pipeline

### Clipboard Manager
- Saves current clipboard contents before touching it
- Simulates Ctrl+C via Win32 `SendInput` to copy selected text
- ~100ms delay after Ctrl+C to let the target app populate the clipboard
- After correction: sets clipboard to corrected text, simulates Ctrl+V, restores original clipboard
- Detects "no text selected" by comparing clipboard before/after Ctrl+C

### Focus Tracker
- Records source window handle via `GetForegroundWindow` before simulating Ctrl+C
- After AI response, checks `GetForegroundWindow` again
- Match: proceed with paste-back
- Mismatch: clipboard + notify approach

### AI Client
- Anthropic C# SDK (or raw HTTP if SDK is immature)
- System prompt: "Fix all typos, spelling, and grammar errors in the following text. Return only the corrected text with no explanation. Preserve the original meaning, tone, and formatting."
- Model: claude-haiku for speed/cost
- Async on background thread, cancellable via Escape
- 10-second timeout

### Floating Overlay
- WPF window with `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`
- Positioned near cursor via `GetCursorPos`, correct monitor via screen bounds check
- **Processing state**: Small dark pill with spinner and "Correcting..."
- **Result state**: Expands to diff toast showing original (strikethrough) and corrected text, with Apply/Cancel
- **Error state**: Shows error message in pill format
- **Focus lost state**: Shows "Correction ready — Ctrl+V to paste"
- Auto-applies after 3 seconds if no interaction (configurable in settings)
- Dark theme, rounded corners, drop shadow, Segoe UI font

### Settings Manager
- JSON file at `%APPDATA%/TextFix/settings.json`
- Fields: `apiKey`, `hotkey`, `model`, `systemPrompt`, `overlayAutoApplySeconds`, `startWithWindows`
- WPF settings window accessible from system tray right-click menu
- Loaded on startup, saved on change
- Invalid/missing file: fall back to defaults, recreate

### System Tray
- NotifyIcon with right-click context menu
- Menu items: Settings, Correction History (future), About, Exit
- Tooltip shows current hotkey and status
- Left-click: no action (or toggle enable/disable)

## Error Handling & Edge Cases

### Hotkey Listener
- Registration fails (combo in use): Tray notification with link to settings to change hotkey
- Saved hotkey fails on startup: Try default, if that fails too, start without hotkey and prompt user
- Process crash: Windows cleans up `RegisterHotKey` registrations automatically

### Clipboard Manager
- No text selected (clipboard unchanged after Ctrl+C): Abort, show "No text selected"
- Clipboard locked by another app: Retry 3x with 50ms delay, then abort with message
- Text too long (>5000 chars): Abort with "Text too long — select a shorter passage"
- Clipboard contains non-text data: Check `Clipboard.ContainsText()`, abort if false
- Original clipboard restore fails: Log, don't crash

### Focus Tracker
- Source window closes during processing: `IsWindow(hwnd)` returns false, use clipboard+notify
- Source window minimized: Treat as focus lost
- Elevated app (admin) and TextFix is not: `SendInput` blocked by UIPI, show "Try running TextFix as administrator"
- Multi-monitor: Use `GetCursorPos` for overlay placement on correct monitor

### AI Client
- No API key configured: Show "Set up your API key in settings" on first hotkey press
- Network timeout (>10s): Show "Correction timed out"
- API errors: Show category — "API key invalid", "Rate limited", "Service unavailable"
- Empty or identical response: Show "No corrections needed", don't paste
- Malformed response (includes explanations): Show in overlay for user review instead of auto-pasting

### Settings Manager
- Corrupted JSON: Fall back to defaults, overwrite bad file, notify
- Missing file (first run): Create with defaults, prompt for API key
- Invalid field values from manual edit: Validate on load, substitute defaults for invalid fields

### General
- Multiple instances: Named mutex enforces single instance, second launch focuses existing
- Unhandled exceptions: Global handler logs to `%APPDATA%/TextFix/error.log`, tray notification, keep running

## Roadmap

### MVP (v0.1) — Core correction loop
- System tray app with hotkey registration
- Clipboard capture/restore pipeline
- Claude Haiku API integration (fix errors only)
- Hybrid floating overlay (pill → diff toast)
- Focus tracking with clipboard fallback
- Settings window (API key, hotkey config)
- JSON-backed settings at `%APPDATA%/TextFix/`
- Single instance enforcement
- Error handling for all edge cases above

### v0.2 — Preset correction modes
- "Fix errors", "Make professional", "Make concise", "Make friendly"
- Mode selector in system tray right-click menu
- Modifier key to cycle modes (e.g., Ctrl+Shift+Alt+C = professional)
- Custom user-defined prompts in settings

### v0.3 — Quality of life
- Correction history (last N corrections with undo)
- Start with Windows toggle
- Auto-update check
- Per-app hotkey overrides (e.g., different copy/paste keys for terminal apps)
- Dark/light theme for overlay and settings

### v0.4 — Multiple AI providers
- OpenAI, local models (Ollama) as alternatives
- Provider selection in settings
- Model selection per provider

### v1.0 — Real-time auto-correct (stretch)
- Low-level keyboard hook monitoring all input
- Word/sentence boundary detection
- Background AI correction with debouncing
- Inline suggestion overlay
- Per-app enable/disable
