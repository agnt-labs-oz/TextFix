# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TextFix is a Windows desktop application that lets users quickly correct and improve typed text using AI. The core workflow: user types text in any app (Teams, editors, console, etc.), selects it, triggers TextFix via hotkey, and the app grabs the selected text, sends it to an AI service for correction, and replaces the original text with the corrected version.

### Core Use Cases
- Fix typos and misspellings from fast typing
- Correct grammar and punctuation
- Optionally expand/rewrite text in various styles

### Feature Tiers
- **MVP**: Hotkey-triggered select-correct-replace workflow
- **Future**: Real-time auto-correction as the user types (system-wide input hook, much harder — latency management, conflict avoidance with target app input handling)

### Key Technical Requirements
- System-wide global hotkey to trigger correction from any application
- Clipboard integration: copy selected text, process it, paste corrected text back
- AI API integration for text correction (e.g., Claude API, OpenAI)
- Minimal UI — speed is the priority; the app should feel invisible

## Tech Stack

- **.NET** (WPF or WinForms) targeting Windows
- **C#** as primary language
- AI provider SDK (Anthropic/OpenAI) for text processing

## Architecture

- **Hotkey listener**: Global keyboard hook (Win32 `RegisterHotKey` or low-level keyboard hook) to trigger correction from any app
- **Clipboard manager**: Simulates Ctrl+C to capture selected text, then Ctrl+V to paste corrected text back
- **AI client**: Sends text to AI API with a system prompt tuned for correction, returns cleaned text
- **Settings/config**: User-configurable hotkey, AI provider, API key, correction style preferences
- **System tray**: App runs in the system tray with minimal footprint

## Build & Run

```bash
dotnet build
dotnet run
```

## Testing

```bash
dotnet test
```
