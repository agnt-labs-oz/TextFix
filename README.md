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
- **Everything is configurable** — hotkey, model, default mode, auto-apply delay, manual-only mode (edit the AI output before applying), history retention, custom prompts. All stored in a single `settings.json` at `%APPDATA%/TextFix/`.
- **Auto-update** — Velopack pulls new releases in the background; install on next launch.
- **Single-file exe** — no .NET runtime required for users.

![Tray menu with the Mode submenu open](docs/screenshots/mode-picker.png)

## Getting started

### Download

Grab the latest release from the [Releases](../../releases) page. The first install is unsigned, so SmartScreen will warn the first time — click **More info** → **Run anyway**. Future updates install silently via the in-app updater.

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
5. Close Settings and start correcting text with Ctrl+Shift+Z

### Configuration

![Settings dialog](docs/screenshots/settings.png)

All settings are stored at `%APPDATA%/TextFix/settings.json`. Your API key is encrypted with Windows DPAPI — it never leaves your machine in plaintext.

| Setting | Default | Notes |
|---------|---------|-------|
| Hotkey | Ctrl+Shift+Z | Any modifier+key combo |
| Model | claude-haiku-4-5 | Haiku, Sonnet 4.5/4.6, Opus 4.6/4.7 |
| Default mode | Fix errors | Or any custom mode you've added |
| Auto-apply delay | 3 seconds | 0 = instant, or any non-negative integer |
| Manual only | Off | Disable auto-apply and edit the output before applying |
| Recent corrections | 10 | History size, 1–100. Wipe any time from Tray → Clear history… |
| Custom modes | — | Add / edit / delete from Settings |

### Privacy

- **API key**: encrypted at rest with Windows DPAPI (per-user, decrypted only by your Windows account).
- **Recent corrections**: the last N corrections (default 10, configurable) are stored as **plaintext JSON** at `%APPDATA%/TextFix/history.json` so you can copy a previous correction from the tray. This file is readable by any process running as your user. If you'd rather not keep them, lower the limit to 1 in Settings or use **Tray → Clear history…** to wipe in-memory + on-disk history immediately. The lifetime counter and session cost are reset by the same action.
- **What's sent to Anthropic**: only the text you select when you press the hotkey. Nothing else is uploaded; no analytics or telemetry.

## Roadmap

### Shipped

**v0.7** — UI polish, privacy, HiDPI
- Configurable history limit + one-click privacy wipe
- Emoji and emoticon preservation in built-in modes
- Overlay clamps to the current monitor on HiDPI/scaled displays
- Collapse / expand toggle in the action row
- Privacy documentation in README and Settings dialog
- Error log redaction (no more `Exception.ToString()`)

**v0.6** — Three-tab result panel with colored inline word diff (red strikethrough for removals, green for additions). Word-level Myers/LCS over whitespace-preserving tokens.

**v0.5 / v0.4** — Velopack auto-update from GitHub Releases. Push a tag, get a single-file installer.

**v0.3** — Custom user-defined correction modes (CRUD in Settings). Inline refine. Editable output via "Manual only" mode. Scrollable Settings dialog.

**v0.2** — Preset correction modes, interactive overlay with clickable buttons, correction history, single-instance enforcement, dark-themed Settings, auto-apply countdown, pin toggle.

### Planned

- **Multiple AI providers** — OpenAI, Google Gemini, local models via Ollama
- **Real-time auto-correction** — monitor typing and correct as you go
- **Start with Windows** — launch on login (setting exists, wiring TBD)
- **Selectable text in the Original tab** — currently a TextBlock, can't be selected
- **Undo** — Ctrl+Z to revert the last applied correction
- **About dialog** — session stats and support links (in progress on a parallel branch)

## Tech stack

- **.NET 10** / C# with WPF (UI) + WinForms (system tray NotifyIcon)
- **Anthropic C# SDK** for Claude API access
- **Win32 P/Invoke** via `LibraryImport` for global hotkeys, clipboard automation, focus tracking, and `SendInput`
- **DPAPI** for API key encryption at rest
- **Velopack** for unsigned single-file installer + auto-update
- **GitHub Actions** for automated releases — push a version tag and get an installer

## License

MIT
