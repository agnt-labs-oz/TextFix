# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TextFix is a Windows desktop application that lets users quickly correct and improve typed text using AI. The core workflow: user types text in any app (Teams, editors, console, etc.), selects it, triggers TextFix via hotkey (default Ctrl+Shift+C), and the app grabs the selected text, sends it to Claude for correction, and replaces the original text with the corrected version.

### Feature Tiers
- **MVP (current)**: Hotkey-triggered select-correct-replace workflow with floating overlay
- **Future**: Preset correction modes, multiple AI providers, real-time auto-correction

## Tech Stack

- **.NET 10** with WPF + WinForms (for NotifyIcon), targeting `net10.0-windows`
- **C#** with `AllowUnsafeBlocks` (required for LibraryImport source-generated P/Invoke)
- **Anthropic C# SDK** (`Anthropic` NuGet v12.x) — uses `ContentBlock` union type with `TryPickText()`, not `OfType<TextBlock>()`
- Win32 P/Invoke via `LibraryImport` (not `DllImport`)

## Architecture

```
App.xaml.cs (shell: tray icon, hotkey wiring, service lifecycle)
├── Services/HotkeyListener.cs    — Win32 RegisterHotKey, parses "Ctrl+Shift+C" format
├── Services/CorrectionService.cs — Pipeline orchestrator (capture → AI → paste)
│   ├── Services/ClipboardManager.cs — SendInput Ctrl+C/V, clipboard save/restore
│   ├── Services/FocusTracker.cs     — GetForegroundWindow, IsWindow, IsIconic
│   └── Services/AiClient.cs        — AnthropicClient wrapper, error handling
├── Views/OverlayWindow.xaml       — Floating overlay (processing → diff → error states)
├── Views/SettingsWindow.xaml      — API key (PasswordBox), hotkey, model config
├── Models/AppSettings.cs          — JSON persistence, DPAPI-encrypted API key
├── Models/CorrectionResult.cs     — Result record with Error() factory
└── Interop/NativeMethods.cs       — All Win32 declarations
```

### Key Design Decisions
- **INPUTUNION must have `Size = 32`** in StructLayout — matches MOUSEINPUT on x64. Without this, SendInput returns ERROR_INVALID_PARAMETER (87). This was the hardest bug to find.
- **MapVirtualKey needs `EntryPoint = "MapVirtualKeyW"`** — LibraryImport requires exact DLL export names
- **WaitForModifierKeysReleased** polls `GetAsyncKeyState` before simulating Ctrl+C — physical hotkey keys interfere with SendInput
- **SetForegroundWindow** restores focus to source app before Ctrl+C — hotkey processing can shift focus
- **API key encrypted with DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`)
- Services created once at startup, not per hotkey press (prevents HttpClient socket exhaustion)
- `ShutdownMode="OnExplicitShutdown"`, no StartupUri — app runs from system tray
- Named Mutex for single-instance enforcement

## Build & Run

```bash
dotnet build
dotnet run --project src/TextFix/TextFix.csproj
```

## Testing

```bash
dotnet test
```

10 tests: 6 AppSettings (including DPAPI migration), 4 AiClient.

## Releasing

Push a version tag to trigger a GitHub Actions build that publishes a self-contained single-file exe:

```bash
git tag v0.1.0
git push origin v0.1.0
```

This creates a GitHub Release with `TextFix-v0.1.0-win-x64.zip` attached.

## Settings

Stored at `%APPDATA%/TextFix/settings.json`. API key is DPAPI-encrypted; legacy plaintext keys are auto-migrated on load.
