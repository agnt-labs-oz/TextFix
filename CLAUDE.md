# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TextFix is a Windows desktop application that lets users quickly correct and improve typed text using AI. The core workflow: user types text in any app (Teams, editors, console, etc.), selects it, triggers TextFix via hotkey (default Ctrl+Shift+Z), and the app grabs the selected text, sends it to the configured AI provider for correction, and replaces the original text with the corrected version.

### Current State
Hotkey-triggered select-correct-replace with a floating interactive overlay, six correction modes switchable from overlay or tray, correction history, auto-apply countdown, pin-open toggle.

Text can be corrected by Anthropic, a local Ollama model, OpenAI, or any OpenAI-compatible endpoint (LM Studio, llama.cpp, OpenRouter, Groq, a corporate gateway). The provider is switchable from the overlay's "Via" dropdown and the tray menu, and each provider keeps its own base URL, model and API key.

A failed correction quotes the API's own explanation and always writes a log line at the default log level. See the diagnostics entries under Key Design Decisions before touching a provider's catch chain.

### Future
Real-time auto-correction, streaming responses, an in-app Ollama setup helper, Google Gemini (its API is not OpenAI-compatible, so it needs a real provider rather than a preset row).

## Tech Stack

- **.NET 10** with WPF + WinForms (for NotifyIcon), targeting `net10.0-windows`
- **C#** with `AllowUnsafeBlocks` (required for LibraryImport source-generated P/Invoke)
- **Anthropic C# SDK** (`Anthropic` NuGet v12.x) — uses `ContentBlock` union type with `TryPickText()`, not `OfType<TextBlock>()`
- **No SDK for the other providers** — Ollama, OpenAI and the rest are reached with `HttpClient` and `System.Text.Json` against `/v1/chat/completions`. Adding an OpenAI-compatible provider must not add a package.
- Win32 P/Invoke via `LibraryImport` (not `DllImport`)

## Architecture

```
App.xaml.cs (shell: tray icon, hotkey wiring, service lifecycle, overlay event routing)
├── Services/HotkeyListener.cs    — Win32 RegisterHotKey, parses "Ctrl+Shift+Z" format
├── Services/CorrectionService.cs — Pipeline orchestrator (capture → AI → paste)
│   ├── Services/ClipboardManager.cs — SendInput Ctrl+C/V, clipboard save/restore
│   ├── Services/FocusTracker.cs     — GetForegroundWindow, IsWindow, IsIconic, RestoreFocus
│   └── Services/Providers/          — One provider abstraction, two implementations
│       ├── IAiProvider.cs             — DisplayName, ProviderId, IsLocal, CorrectAsync, ListModelsAsync
│       ├── ProviderPreset.cs          — Preset record + KeyRequirement (None/Optional/Required)
│       ├── ProviderPresets.cs         — The four-row table: Anthropic, Ollama, OpenAI, Custom
│       ├── AnthropicProvider.cs       — AnthropicClient wrapper, assistant-prefill, typed exceptions
│       ├── OpenAiCompatibleProvider.cs — /v1/chat/completions for Ollama, OpenAI and anything else
│       ├── ApiErrorBody.cs            — Pulls the server's explanation out of an error response
│       └── ProviderFactory.cs         — Builds the active provider, caches on a hash of its config
├── Services/ResponseSanitizer.cs  — Strips model preamble; flags replies that still look chatty
├── Services/PromptTemplates.cs    — Shared user message + the two prompt suffixes
├── Services/DpapiString.cs        — Protect throws on failure; Unprotect returns "" instead
├── Services/DiffEngine.cs         — Word-level Myers/LCS over whitespace-preserving tokens
├── Services/CostEstimator.cs      — Per-model USD rates; local inference short-circuits to zero
├── Services/StatsTracker.cs       — Append-only JSONL aggregates behind the About window.
│                                    A SECOND store, separate from CorrectionHistory —
│                                    anything claiming to erase history must clear both
├── Services/AppLog.cs             — Daily-rolling log, 7-day retention. Formats exceptions by
│                                    hand rather than ToString(), which can leak auth headers.
│                                    Stamps a build header on the first line each process emits
├── Services/StartupRegistration.cs — HKCU\…\Run entry for launch-on-login
├── Services/UpdateService.cs      — Velopack check/download, applied on exit
├── Views/OverlayWindow.xaml       — Floating overlay (processing → diff → error → applied states)
│                                    Buttons, mode + provider selectors, elapsed counter, fade animation
├── Views/SettingsWindow.xaml      — Provider, base URL, key, model, test connection, hotkey, auto-apply
├── Views/CustomModeDialog.xaml    — Add / edit a user-defined correction mode
├── Views/AboutWindow.xaml         — Lifetime stats, per-mode breakdown, spend estimate
├── Models/AppSettings.cs          — JSON persistence, ActiveProviderId, per-provider configs
├── Models/ProviderConfig.cs       — Per-provider BaseUrl, Model, DPAPI-encrypted key
├── Models/CorrectionMode.cs       — Mode record (Name, SystemPrompt) with 6 built-in defaults
├── Models/CorrectionHistory.cs    — Fixed-size ring buffer of last 10 CorrectionResults
├── Models/CorrectionResult.cs     — Result record with Error() factory
├── Models/StatsAggregate.cs       — Rolled-up totals the About window renders
└── Interop/NativeMethods.cs       — All Win32 declarations
```

### Key Design Decisions
- **INPUTUNION must have `Size = 32`** in StructLayout — matches MOUSEINPUT on x64. Without this, SendInput returns ERROR_INVALID_PARAMETER (87). This was the hardest bug to find.
- **MapVirtualKey needs `EntryPoint = "MapVirtualKeyW"`** — LibraryImport requires exact DLL export names
- **WaitForModifierKeysReleased** polls `GetAsyncKeyState` before simulating Ctrl+C — physical hotkey keys interfere with SendInput
- **SetForegroundWindow** restores focus to source app before Ctrl+C — hotkey processing can shift focus
- **Overlay must never double-hide** — `FadeOutAndHide()` animates opacity then calls `Hide()` on completion. Calling `Hide()` separately after `FadeOutAndHide()` corrupts window state and breaks subsequent `Show()` calls. The cancel path in `App.xaml.cs` must NOT call `_overlay.Hide()` — the overlay handles its own fade.
- **WPF ComboBox dark theme requires full custom ControlTemplate** — setting `Foreground`/`Background` on a ComboBox is ignored because the default template hardcodes colors. Both the ComboBox and ComboBoxItem need complete `ControlTemplate` overrides (see SettingsWindow.xaml and OverlayWindow.xaml for working examples).
- **A custom ComboBox template needs `PART_EditableTextBox` before `IsEditable` does anything** — WPF resolves that template part by exact name. Without it the control degrades to selection-only *silently*: it compiles, it renders, and typing simply does nothing. SettingsWindow's template has the part; OverlayWindow's does not, which is fine only because nothing there is editable. Add it before setting `IsEditable` on an overlay ComboBox.
- **HttpClient timeout throws TaskCanceledException** (subclass of OperationCanceledException), not HttpRequestException. Distinguish from user cancellation by checking `ct.IsCancellationRequested` in the catch clause.
- **A failed correction must always leave a log line, and the catch-all must never be the whole story.** Both providers route every error return through a private `Fail` helper that logs before returning — mapped failures at Warn, genuine surprises at Error, so both survive the default `LogLevel` of `Warn` and diagnostics work without the user knowing a log level exists. Deliberate cancels are the one exception: they are routine, and logging them would bury real faults. Until v0.9.x nothing on the provider path logged at all, so `"An unexpected error occurred."` was the complete diagnostic record of a failure.
- **`AnthropicApiException` is the base of the whole 4xx family**, so catching only `AnthropicUnauthorizedException`/`RateLimit`/`5xx` leaves 400, 403, 404 and 422 falling through to the catch-all. That is what hid a plain "your credit balance is too low" behind "An unexpected error occurred" for an entire release. It carries `StatusCode` and `ResponseBody`; catch it *after* the specific types.
- **Quote the server's own explanation rather than a bare status code.** `ApiErrorBody` parses the two shapes that cover every endpoint here — `{"error":{"message":…}}` (OpenAI, Anthropic, OpenRouter, Groq) and `{"error":"…"}` (Ollama, llama.cpp) — and returns null for anything else, so a proxy's HTML error page can never be pasted into the overlay as if it were prose. Specific mappings still win where we know better than the server: a 401 always means the key, and a 404 naming a model becomes the exact `ollama pull` command.
- **User data lives in TWO stores, and a wipe must clear both.** `CorrectionHistory` (`history.json`, the ring buffer behind the tray submenu and overlay panel) and `StatsTracker` (`stats.jsonl`, the lifetime aggregates behind About). "Clear history" originally cleared only the first, so About kept reporting every correction the user had just erased — while the confirm dialog claimed the counters were reset. Both wipe paths now call `StatsTracker.ClearAsync`, and both prompts share `App.HistoryWipeWarning` so they cannot drift into promising different things. Add a third store and this list grows.
- **A failed wipe must never report success.** `StatsTracker.RecordAsync` swallows its errors deliberately — a lost stats line is a lost data point. `ClearAsync` does the opposite and lets the exception out, because a user told "history cleared" over a wipe that silently failed believes data is gone when it is not.
- **Never log the text being corrected.** Provider log lines carry the provider id, model, status and the server's message — never the user's content. `AppLog.FormatException` exists for the same reason at the header level: `Exception.ToString()` on HTTP-backed SDK exceptions can round-trip authorization headers into the file.
- **One OpenAI-compatible client serves Ollama, OpenAI and custom endpoints** — they share the `/v1/chat/completions` wire format, so adding a provider is a row in `ProviderPresets`, not new code. Anthropic keeps its own SDK for the assistant-prefill trick and typed exceptions, and is the reason `IAiProvider` exists rather than one client.
- **Timeouts are per-provider, but the two providers enforce them differently.** `OpenAiCompatibleProvider` uses a linked `CancellationTokenSource` with `CancelAfter`, because its `HttpClient` is shared and static (to avoid socket exhaustion) and so cannot carry a per-provider deadline. `AnthropicProvider` owns its `AnthropicClient` and just sets `Timeout` on it. Values come from `ProviderPreset.TimeoutSeconds`: Ollama and Custom 120s — a cold local model spends 10-20s loading into RAM before its first token — OpenAI 30s, Anthropic 10s.
- **`max_tokens` vs `max_completion_tokens`** — OpenAI deprecated the former and *rejects* it on o-series models; Ollama and llama.cpp understand only the former. This is why `ProviderPreset` carries `TokenParam` as data rather than the client picking one.
- **`ProviderFactory` caches on a SHA-256 hash of the whole config tuple, including the key's value** — an earlier version keyed on the key's *length*, which meant rotating a key to a same-length replacement (the normal case, since key formats are fixed-length) kept serving a provider holding the revoked credential. The digest persists in the cache key, never the secret.
- **API key encrypted with DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`)
- **Settings writes credentials per provider and never touches the legacy top-level `ApiKey`** — that field is read only by the one-time migration in `AppSettings.LoadAsync`. Anything asking "is this app set up yet?" must check `_aiClient is null`, not the legacy key: a user on Ollama has no key and needs none, and a legacy-key check traps them in the first-run Settings dialog forever.
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
dotnet test                                              # all 218 tests
dotnet test --filter FullyQualifiedName~AppSettingsTests  # single test class
```

218 cases. Note that xUnit expands every `[Theory]`/`[InlineData]` pair into its own case, so counting attributes in the source undercounts — trust `dotnet test`.

Covered: settings persistence, DPAPI round-trips and legacy migration; correction modes, history and results; the provider preset table and factory caching; response sanitizing; cost estimation; diffing; stats; logging; hotkey parsing.

`OpenAiCompatibleProvider` is tested end to end against a stubbed `HttpMessageHandler` — no test ever reaches a real endpoint. `AnthropicProvider` is only covered for its guard clauses and properties; its success path goes through the SDK's own client, which the tests do not stub, so `CorrectAsync` is never exercised against a response. Worth closing if that class grows.

Not covered by tests, by design: WPF and WinForms UI wiring. Assertions against `ComboBox.Items` would be test theatre — the overlay and Settings window are verified by hand.

## Releasing

Push a version tag to trigger a GitHub Actions build that publishes a self-contained single-file exe:

```bash
git tag v0.2.1
git push origin v0.2.1
```

This creates a GitHub Release with `TextFix-v0.2.1-win-x64.zip` attached.

## Settings

Stored at `%APPDATA%/TextFix/settings.json`. Each provider has its own entry under `Providers` holding its base URL, model and DPAPI-encrypted key; `ActiveProviderId` selects which one is used. Legacy plaintext keys, and the pre-multi-provider top-level `ApiKey`/`Model` fields, are migrated on load.

**This file contains a live API key.** Do not run the app from an agent session or test harness — starting TextFix loads and rewrites it.
