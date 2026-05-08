# TextFix

A lightweight Windows desktop app that corrects and improves your text using AI. Select text in any app, press a hotkey, and TextFix replaces it with the corrected version — no copy-pasting, no browser tabs, no context switching.

![Floating overlay showing a before/after diff](docs/screenshots/overlay.png)

## How it works

1. Type or select text in any application (Teams, Outlook, Notepad, VS Code, a browser — anything)
2. Press **Ctrl+Shift+Z** (configurable)
3. A floating overlay appears showing the original vs. corrected text
4. Click **Apply** (or press Enter) to replace your text, or **Cancel** (Esc) to keep the original

TextFix uses the clipboard under the hood: it copies your selection, sends it to Claude for correction, then pastes the result back — all in about a second.

## Features

- **Six built-in correction modes** — switch instantly from the overlay or system tray:
  - *Fix errors* — spelling, grammar, and typo fixes
  - *Professional* — polished business tone
  - *Concise* — trim filler and tighten prose
  - *Friendly* — warm, conversational rewrite
  - *Expand* — add detail and description
  - *Prompt enhancer* — rewrite text as an effective AI prompt
- **Custom modes** — define your own correction profile with a custom system prompt (add / edit / delete from Settings). Use it for tone-of-voice presets, domain-specific rewrites, or anything the built-ins don't cover.
- **Emoji and emoticons preserved** — `:)`, `:-D`, `🙂`, `🙏` and friends survive correction across every built-in mode.
- **Floating overlay** — three-tab result panel (Corrected / Diff / Original) with colored inline word diff, clickable Apply/Cancel buttons, auto-apply countdown, draggable, resizable, and a collapse toggle to peek behind it.
- **Correction history** — recent corrections available from the tray menu, click to copy. Limit is configurable; one-click wipe from tray or Settings for privacy.
- **Local stats** — open *About TextFix* from the tray to see lifetime corrections, time saved, per-mode breakdown, and this month's API spend estimate. All local; nothing is sent anywhere.
- **Everything is configurable** — hotkey, model, default mode, auto-apply delay, manual-only mode (edit the AI output before applying), history retention, log verbosity, custom prompts. All stored in a single `settings.json` at `%APPDATA%/TextFix/`.
- **Auto-update** — Velopack pulls new releases in the background; install on next launch.
- **Single-file exe** — no installer dependencies, no .NET runtime required.

![Tray menu with the Mode submenu open](docs/screenshots/mode-picker.png)

## Setup

### 1. Get an Anthropic API key

Sign in at [console.anthropic.com](https://console.anthropic.com/settings/keys) and create a new key. Keep the page open — you'll paste the key into TextFix in step 3.

### 2. Install TextFix

**Easiest:** grab the latest installer from the [Releases](../../releases) page. The first install is unsigned, so SmartScreen will warn the first time — click **More info** → **Run anyway**. Future updates install silently via the in-app updater.

**Or build from source** (requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)):

```
git clone https://github.com/agnt-labs-oz/TextFix.git
cd TextFix
dotnet build
dotnet run --project src/TextFix/TextFix.csproj
```

### 3. Paste your key into Settings

On first run, the Settings window opens automatically. Paste your API key, pick a model (Claude Haiku is the default — fast and cheap), and close.

![Settings window with API key field](docs/screenshots/settings.png)

### 4. Try it

Select text in any app (Notepad is fine for a first try), press **Ctrl+Shift+Z**, and the overlay appears with the suggested correction.

To switch correction modes, right-click the tray icon and pick from the **Mode** submenu.

### Configuration

All settings are stored at `%APPDATA%/TextFix/settings.json`. Your API key is encrypted with Windows DPAPI — it never leaves your machine in plaintext.

| Setting | Default | Notes |
|---------|---------|-------|
| Hotkey | Ctrl+Shift+Z | Any modifier+key combo |
| Model | claude-haiku-4-5 | Haiku, Sonnet 4.5/4.6, Opus 4.6/4.7 |
| Default mode | Fix errors | Or any custom mode you've added |
| Auto-apply delay | 3 seconds | 0 = instant, or any non-negative integer |
| Manual only | Off | Disable auto-apply and edit the output before applying |
| Recent corrections | 10 | History size, 1–100. Wipe any time from Tray → Clear history… |
| Log level | Warn | Info / Warn / Error — bump to Info to capture per-correction events |
| Custom modes | — | Add / edit / delete from Settings |

### Privacy

- **API key**: encrypted at rest with Windows DPAPI (per-user, decrypted only by your Windows account).
- **Recent corrections**: the last N corrections (default 10, configurable) are stored as **plaintext JSON** at `%APPDATA%/TextFix/history.json` so you can copy a previous correction from the tray. This file is readable by any process running as your user. Wipe any time via **Tray → Clear history…** or the **Clear history now** button in Settings.
- **Logs**: a daily-rolling log at `%APPDATA%/TextFix/logs/` records lifecycle events, errors, and per-correction *counts* — never the text you correct. Retained 7 days, then deleted automatically.
- **Local stats**: aggregates shown in the *About TextFix* window come from `%APPDATA%/TextFix/stats.jsonl`. Append-only, local, never uploaded. Delete the file any time.
- **What's sent to Anthropic**: only the text you select when you press the hotkey. Nothing else is uploaded; no telemetry, no developer-side analytics.

## Suggest features / report bugs

- **Ideas** → [Discussions › Ideas](https://github.com/agnt-labs-oz/TextFix/discussions/categories/ideas)
- **Bugs** → [Issues](https://github.com/agnt-labs-oz/TextFix/issues/new/choose)
- Or click **Suggest a feature…** / **Report an issue…** in the tray menu — both pre-fill the right form.

## Support TextFix

TextFix is free and MIT-licensed. If it saves you time, you can leave a tip:

[☕ ko-fi.com/3smallwins](https://ko-fi.com/3smallwins)

## Roadmap

### Shipped

**v0.8** — About dialog with local stats panel + Ko-fi support link, "Suggest a feature…" / "Report an issue…" / "Open log folder" / "About TextFix…" tray entries wired to GitHub Discussions/Issues, daily-rolling AppLog with 7-day retention, per-model cost estimator (Haiku/Sonnet/Opus), StatsTracker with JSONL aggregates.

**v0.7** — UI polish, privacy, HiDPI: configurable history limit + one-click privacy wipe, emoji and emoticon preservation in built-in modes, overlay clamps to the current monitor on HiDPI/scaled displays, collapse / expand toggle in the action row, privacy documentation, error log redaction.

**v0.6** — Three-tab result panel with colored inline word diff (red strikethrough for removals, green for additions). Word-level Myers/LCS over whitespace-preserving tokens.

**v0.5 / v0.4** — Velopack auto-update from GitHub Releases. Push a tag, get a single-file installer.

**v0.3** — Custom user-defined correction modes (CRUD in Settings). Inline refine. Editable output via "Manual only" mode. Scrollable Settings dialog.

**v0.2** — Preset correction modes, interactive overlay with clickable buttons, correction history, single-instance enforcement, dark-themed Settings, auto-apply countdown, pin toggle.

### Planned

- **Multiple AI providers** — OpenAI, Google Gemini, local models via Ollama
- **Real-time auto-correction** — monitor typing and correct as you go
- **Start with Windows** — launch on login (setting exists, wiring TBD)
- **Translation / language-learning mode** — translate selection, explain grammar
- **Selectable text in the Original tab** — currently a TextBlock, can't be selected
- **Undo** — Ctrl+Z to revert the last applied correction

## Tech stack

- **.NET 10** / C# with WPF (UI) + WinForms (system tray NotifyIcon)
- **Anthropic C# SDK** for Claude API access
- **Win32 P/Invoke** via `LibraryImport` for global hotkeys, clipboard automation, focus tracking, and `SendInput`
- **DPAPI** for API key encryption at rest
- **Velopack** for unsigned single-file installer + auto-update
- **GitHub Actions** for automated releases — push a version tag and get a self-contained installer

## License

MIT
