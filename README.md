# TextFix

A lightweight Windows desktop app that corrects and improves your text using AI. Select text in any app, press a hotkey, and TextFix replaces it with the corrected version — no copy-pasting, no browser tabs, no context switching.

![TextFix overlay showing a correction](docs/screenshots/overlay.png)

## How it works

1. Type or select text in any application (Teams, Outlook, Notepad, VS Code, a browser — anything)
2. Press **Ctrl+Shift+Z** (configurable)
3. A floating overlay appears showing the original vs. corrected text
4. Click **Apply** (or press Enter) to replace your text, or **Cancel** (Esc) to keep the original

TextFix uses the clipboard under the hood: it copies your selection, sends it to Claude for correction, then pastes the result back — all in about a second.

## Features

- **Six correction modes** — switch instantly from the overlay or system tray:
  - *Fix errors* — spelling, grammar, and typo fixes
  - *Professional* — polished business tone
  - *Concise* — trim filler and tighten prose
  - *Friendly* — warm, conversational rewrite
  - *Expand* — add detail and description
  - *Prompt enhancer* — rewrite text as an effective AI prompt
- **Floating overlay** — shows a colored before/after diff with clickable Apply/Cancel buttons, auto-apply countdown, and a pin toggle for keeping it open between corrections
- **System tray app** — runs quietly in the background, accessible from the notification area
- **Correction history** — last 50 corrections in the overlay, click any entry to copy
- **Local stats** — open *About TextFix* from the tray to see lifetime corrections, time saved, per-mode breakdown, and this month's API spend estimate (all local, never sent anywhere)
- **Settings** — API key (encrypted with DPAPI), model selection, hotkey configuration, auto-apply delay
- **Single-file exe** — no installer, no dependencies, just download and run

## Setup

### 1. Get an Anthropic API key

Sign in at [console.anthropic.com](https://console.anthropic.com/settings/keys) and create a new key. Keep the page open — you'll paste the key into TextFix in step 3.

![Anthropic API keys page](docs/screenshots/api-key.png)

### 2. Install TextFix

**Easiest:** grab the latest installer from the [Releases](../../releases) page.

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

![Tray menu with mode picker expanded](docs/screenshots/mode-picker.png)

### Configuration

All settings are stored at `%APPDATA%/TextFix/settings.json`. Your API key is encrypted with Windows DPAPI — it never leaves your machine in plaintext.

| Setting | Default | Notes |
|---------|---------|-------|
| Hotkey | Ctrl+Shift+Z | Any modifier+key combo |
| Model | claude-haiku-4-5 | Haiku, Sonnet 4.5/4.6, Opus 4.6 |
| Auto-apply delay | 3 seconds | Off, 3s, 5s, 10s |
| Keep overlay open | Off | Pin overlay for multiple corrections |
| Log level | Warn | Info / Warn / Error — bump to Info to capture per-correction events |

## Suggest features / report bugs

- **Ideas** → [Discussions › Ideas](https://github.com/agnt-labs-oz/TextFix/discussions/categories/ideas)
- **Bugs** → [Issues](https://github.com/agnt-labs-oz/TextFix/issues/new/choose)
- Or click **Suggest a feature…** / **Report an issue…** in the tray menu — both pre-fill the right form.

## Support TextFix

TextFix is free and MIT-licensed. If it saves you time, you can leave a tip:

[☕ ko-fi.com/3smallwins](https://ko-fi.com/3smallwins)

## Privacy

When you trigger a correction, TextFix sends your selected text to your chosen AI provider (Anthropic by default — you provide your own API key). **Nothing is sent to the developer.** No telemetry is collected. Your API key is encrypted on disk with Windows DPAPI.

The lightweight log file at `%APPDATA%\TextFix\logs\` records app lifecycle events, errors, and per-correction *counts* — never the text you correct. Stats shown in the **About TextFix** window come from a local file at `%APPDATA%\TextFix\stats.jsonl` and never leave your machine. Delete either file at any time.

## Roadmap

### Shipped
- Hotkey-driven select-correct-replace flow with floating interactive overlay
- Six preset correction modes, switchable from overlay or tray
- Colored before/after diff with intra-line word highlights
- Correction history (last 50)
- Local usage stats panel + Ko-fi tip jar in About window
- Daily-rolling logs with retention
- "Suggest a feature" / "Report an issue" in tray menu (pre-filled GitHub forms)

### Planned
- **Custom modes** — user-defined correction profiles with custom system prompts
- **Multiple AI providers** — OpenAI, Google Gemini, local models via Ollama
- **Real-time auto-correction** — monitor typing and correct as you go
- **Start with Windows** — launch on login (setting exists, wiring TBD)
- **Translation / language-learning mode** — translate selection, explain grammar
- **Undo support** — Ctrl+Z to revert the last applied correction

## Tech stack

- **.NET 10** / C# with WPF (UI) + WinForms (system tray NotifyIcon)
- **Anthropic C# SDK** for Claude API access
- **Win32 P/Invoke** via `LibraryImport` for global hotkeys, clipboard automation, focus tracking, and `SendInput`
- **DPAPI** for API key encryption at rest
- **GitHub Actions** for automated releases — push a version tag and get a self-contained installer

## License

MIT
