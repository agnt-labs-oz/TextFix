# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TextFix is a Windows desktop application that lets users quickly correct and improve typed text using AI. The core workflow: user types text in any app (Teams, editors, console, etc.), selects it, triggers TextFix via hotkey (default Ctrl+Shift+C), and the app grabs the selected text, sends it to Claude for correction, and replaces the original text with the corrected version.

### Current State (v0.2)
Hotkey-triggered select-correct-replace with floating interactive overlay, six correction modes switchable from overlay or tray, correction history, auto-apply countdown, pin-open toggle.

### Future
Multiple AI providers, custom user-defined modes, real-time auto-correction, start-with-Windows.

## Tech Stack

- **.NET 10** with WPF + WinForms (for NotifyIcon), targeting `net10.0-windows`
- **C#** with `AllowUnsafeBlocks` (required for LibraryImport source-generated P/Invoke)
- **Anthropic C# SDK** (`Anthropic` NuGet v12.x) — uses `ContentBlock` union type with `TryPickText()`, not `OfType<TextBlock>()`
- Win32 P/Invoke via `LibraryImport` (not `DllImport`)

## Architecture

```
App.xaml.cs (shell: tray icon, hotkey wiring, service lifecycle, overlay event routing)
├── Services/HotkeyListener.cs    — Win32 RegisterHotKey, parses "Ctrl+Shift+C" format
├── Services/CorrectionService.cs — Pipeline orchestrator (capture → AI → paste)
│   ├── Services/ClipboardManager.cs — SendInput Ctrl+C/V, clipboard save/restore
│   ├── Services/FocusTracker.cs     — GetForegroundWindow, IsWindow, IsIconic, RestoreFocus
│   └── Services/AiClient.cs        — AnthropicClient wrapper, timeout vs cancellation handling
├── Views/OverlayWindow.xaml       — Floating overlay (processing → diff → error → applied states)
│                                    Clickable buttons, mode selector, pin toggle, fade animation
├── Views/SettingsWindow.xaml      — API key (PasswordBox + show/copy), hotkey, model, mode, auto-apply
├── Models/AppSettings.cs          — JSON persistence, DPAPI-encrypted API key
├── Models/CorrectionMode.cs       — Mode record (Name, SystemPrompt) with 6 built-in defaults
├── Models/CorrectionHistory.cs    — Fixed-size ring buffer of last 10 CorrectionResults
├── Models/CorrectionResult.cs     — Result record with Error() factory
└── Interop/NativeMethods.cs       — All Win32 declarations
```

### Key Design Decisions
- **INPUTUNION must have `Size = 32`** in StructLayout — matches MOUSEINPUT on x64. Without this, SendInput returns ERROR_INVALID_PARAMETER (87). This was the hardest bug to find.
- **MapVirtualKey needs `EntryPoint = "MapVirtualKeyW"`** — LibraryImport requires exact DLL export names
- **WaitForModifierKeysReleased** polls `GetAsyncKeyState` before simulating Ctrl+C — physical hotkey keys interfere with SendInput
- **SetForegroundWindow** restores focus to source app before Ctrl+C — hotkey processing can shift focus
- **Overlay must never double-hide** — `FadeOutAndHide()` animates opacity then calls `Hide()` on completion. Calling `Hide()` separately after `FadeOutAndHide()` corrupts window state and breaks subsequent `Show()` calls. The cancel path in `App.xaml.cs` must NOT call `_overlay.Hide()` — the overlay handles its own fade.
- **WPF ComboBox dark theme requires full custom ControlTemplate** — setting `Foreground`/`Background` on a ComboBox is ignored because the default template hardcodes colors. Both the ComboBox and ComboBoxItem need complete `ControlTemplate` overrides (see SettingsWindow.xaml and OverlayWindow.xaml for working examples).
- **HttpClient timeout throws TaskCanceledException** (subclass of OperationCanceledException), not HttpRequestException. Distinguish from user cancellation by checking `ct.IsCancellationRequested` in the catch clause.
- **API key encrypted with DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`)
- Services created once at startup, not per hotkey press (prevents HttpClient socket exhaustion)
- `ShutdownMode="OnExplicitShutdown"`, no StartupUri — app runs from system tray
- Named Mutex for single-instance enforcement

## Build & Run

```bash
dotnet build
dotnet run --project src/TextFix/TextFix.csproj
```

The app runs in the system tray — kill the running instance before rebuilding if DLLs are locked:

```bash
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
```

## Testing

```bash
dotnet test                                              # all 23 tests
dotnet test --filter FullyQualifiedName~AppSettingsTests  # single test class
```

23 tests: 10 AppSettings (DPAPI, migration, modes), 6 CorrectionMode, 3 CorrectionHistory, 4 AiClient.

## Releasing

Push a version tag to trigger a GitHub Actions build that publishes a self-contained single-file exe:

```bash
git tag v0.2.1
git push origin v0.2.1
```

This creates a GitHub Release with `TextFix-v0.2.1-win-x64.zip` attached.

## Settings

Stored at `%APPDATA%/TextFix/settings.json`. API key is DPAPI-encrypted; legacy plaintext keys are auto-migrated on load.
