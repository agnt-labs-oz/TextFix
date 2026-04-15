# TextFix

A lightweight Windows desktop app that corrects and improves your text using AI. Select text in any app, press a hotkey, and TextFix replaces it with the corrected version — no copy-pasting, no browser tabs, no context switching.

## How it works

1. Type or select text in any application (Teams, Outlook, Notepad, VS Code, a browser — anything)
2. Press **Ctrl+Shift+C** (configurable)
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
- **Floating overlay** — shows a before/after diff with clickable Apply/Cancel buttons, auto-apply countdown, and a pin toggle for keeping it open between corrections
- **System tray app** — runs quietly in the background, accessible from the notification area
- **Correction history** — last 10 corrections available from the tray menu, click to copy
- **Settings** — API key (encrypted with DPAPI), model selection, hotkey configuration, auto-apply delay
- **Single-file exe** — no installer, no dependencies, just download and run

## Getting started

### Download

Grab the latest release from the [Releases](../../releases) page — it's a single `TextFix.exe` file.

### Or build from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```
git clone https://github.com/agnt-labs-oz/TextFix.git
cd TextFix
dotnet build
dotnet run --project src/TextFix/TextFix.csproj
```

### Setup

1. Run TextFix — it will appear in your system tray
2. On first launch, the Settings window opens automatically
3. Enter your [Anthropic API key](https://console.anthropic.com/settings/keys)
4. Choose a model (Claude Haiku is the default — fast and cheap)
5. Close Settings and start correcting text with Ctrl+Shift+C

### Configuration

All settings are stored at `%APPDATA%/TextFix/settings.json`. Your API key is encrypted with Windows DPAPI — it never leaves your machine in plaintext.

| Setting | Default | Notes |
|---------|---------|-------|
| Hotkey | Ctrl+Shift+C | Any modifier+key combo |
| Model | claude-haiku-4-5 | Haiku, Sonnet 4.5/4.6, Opus 4.6 |
| Auto-apply delay | 3 seconds | Off, 3s, 5s, 10s |
| Keep overlay open | Off | Pin overlay for multiple corrections |

## Roadmap

### Shipped (v0.2)
- Preset correction modes with tray and overlay switching
- Interactive overlay with clickable buttons and fade animations
- Correction history
- App icon and single-instance enforcement
- Dark-themed settings with API key show/copy
- Auto-apply countdown with configurable delay
- Pin toggle for keeping overlay open between corrections

### Planned
- **Custom modes** — user-defined correction profiles with custom system prompts
- **Multiple AI providers** — OpenAI, Google Gemini, local models via Ollama
- **Real-time auto-correction** — monitor typing and correct as you go
- **Start with Windows** — launch on login (setting exists, wiring TBD)
- **Text selection in overlay** — copy portions of corrected text
- **Undo support** — Ctrl+Z to revert the last applied correction
- **Usage stats** — track corrections per day, tokens used, cost estimate

## Tech stack

- **.NET 10** / C# with WPF (UI) + WinForms (system tray NotifyIcon)
- **Anthropic C# SDK** for Claude API access
- **Win32 P/Invoke** via `LibraryImport` for global hotkeys, clipboard automation, focus tracking, and `SendInput`
- **DPAPI** for API key encryption at rest
- **GitHub Actions** for automated releases — push a version tag and get a self-contained single-file exe

## License

MIT
