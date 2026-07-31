# Multi-provider support: local models and OpenAI-compatible APIs

**Date:** 2026-08-01
**Status:** Approved design, pending implementation plan

## Problem

TextFix speaks only to Anthropic. Users who want to keep text on their own machine,
or who already pay for a different provider, cannot use the app at all. Every
correction sends the selected text to `api.anthropic.com` and costs money.

## Insight that keeps this small

Ollama, LM Studio, llama.cpp-server, OpenAI, OpenRouter, Groq and vLLM all expose the
same `POST /v1/chat/completions` wire format. "Local models" and "other APIs" are
therefore not two features. They are one HTTP client plus a table of presets that
differ only in base URL, whether a key is required, and a couple of parameter names.

No new NuGet package is needed. `System.Net.Http.Json` covers it.

## Goals

- Run corrections against a local model with no text leaving the machine.
- Run corrections against OpenAI, or any OpenAI-compatible endpoint.
- Switch provider from the overlay without opening Settings.
- Keep the Anthropic path exactly as good as it is today.

## Non-goals

- Streaming responses. Deferred; a per-provider timeout plus an elapsed counter
  covers the local-model latency problem at a fraction of the cost.
- Per-mode provider binding. One active provider at a time.
- Bundling or installing Ollama. We detect and report, we do not provision.
- Making cost estimation exact for unknown remote models (see Cost below).

## Architecture

New folder `src/TextFix/Services/Providers/`.

```
IAiProvider
├── AnthropicProvider          existing AiClient.cs, renamed
└── OpenAiCompatibleProvider   new; serves ollama, openai and custom presets

ProviderPresets    static table, one row per provider
ProviderFactory    ProviderConfig -> cached IAiProvider
```

### IAiProvider

```csharp
public interface IAiProvider
{
    string DisplayName { get; }
    Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
```

`CorrectAsync` never throws. It returns `CorrectionResult.Error(...)` for every failure
mode, preserving the contract `CorrectionService` already relies on.

`ListModelsAsync` drives the model dropdown and doubles as the connection test.
`AnthropicProvider` returns its static known-model list without a network call —
which means Test connection is **hidden for Anthropic**, since a call that cannot fail
is not a test. The Anthropic key continues to be validated on first correction, where
`AnthropicUnauthorizedException` already produces a clear message.

An `ActiveProviderId` that matches no preset — from a hand-edited settings file or a
downgrade — falls back to `anthropic` rather than throwing.

### AnthropicProvider

`AiClient.cs` renamed and moved. Unchanged behaviour:

- Anthropic SDK, `AnthropicClient`.
- Assistant prefill of `"<result>"` (`AiClient.cs:47`), which is what makes Claude
  reliably emit bare text. This is Anthropic-specific and stays here.
- The typed-exception ladder at `AiClient.cs:89-104` that produces the good error
  messages.
- Does **not** run `ResponseSanitizer`. Prefill already guarantees a bare response.

### OpenAiCompatibleProvider

```
POST {BaseUrl}/chat/completions   body: { model, messages[], <tokenParam>, temperature, stream:false }
GET  {BaseUrl}/models             -> data[].id
```

- `Authorization: Bearer {key}` header is added only when a key is present. For
  Ollama the header is omitted entirely rather than sent empty.
- Messages are `[{role:"system"}, {role:"user"}]`. No prefill — not supported.
- Reads `usage.prompt_tokens` / `usage.completion_tokens`, falling back to
  `usage.input_tokens` / `usage.output_tokens`. Some OpenAI-compatible servers use
  the latter; a missing usage object yields zeros rather than an error.
- Accepts an injected `HttpMessageHandler` so it can be tested against a stub.
- Shares one static `HttpClient` across instances, per the "services created once at
  startup" rule in CLAUDE.md, to avoid socket exhaustion.
- Always runs `ResponseSanitizer` on the response.

### ProviderPresets

All per-provider variation lives here, so adding Groq or LM Studio is a data change
with no new code.

| Id | Display | BaseUrl | Key | Default model | Timeout | Token param |
|---|---|---|---|---|---|---|
| `anthropic` | Anthropic | n/a (SDK) | required | claude-haiku-4-5-20251001 | 10s | n/a |
| `ollama` | Ollama (local) | `http://localhost:11434/v1` | none | first listed | 120s | `max_tokens` |
| `openai` | OpenAI | `https://api.openai.com/v1` | required | gpt-4o-mini | 30s | `max_completion_tokens` |
| `custom` | Custom (OpenAI-compatible) | user-entered | optional | user-entered | 120s | `max_tokens` |

The token-parameter column is load-bearing. Verified against OpenAI's live API
reference on 2026-08-01: `max_completion_tokens` replaces the deprecated `max_tokens`
and `max_tokens` is **not compatible with o-series models**. Ollama and llama.cpp
understand only `max_tokens`. Making this a preset field beats a runtime branch.

### Supporting components

**`ResponseSanitizer`** — pure string functions, no I/O.

- `Strip(string)` removes conversational lead-ins, triple-backtick fences, and
  wrapping quotes.
- `LooksConversational(string)` reports whether the stripped text still reads like
  chat, driving a warning banner rather than an error.

**`PromptTemplates`** — the "you are a text transformation tool, not a chatbot"
suffix currently inlined at `AiClient.cs:43` moves here in two variants: the prefill
version for Anthropic, and a stricter no-prefill version for everything else.

**`DpapiString`** — the DPAPI protect/unprotect helpers move out of `AppSettings` so
`ProviderConfig` can reuse them instead of duplicating the try/catch.

### Changes to existing code

`CorrectionService` changes by one type: `AiClient _aiClient` becomes
`IAiProvider _provider`, and `UpdateAiClient()` becomes `UpdateProvider()`. That
method already exists, which is what makes overlay quick-switching nearly free.

## Settings

### Schema

```csharp
public class ProviderConfig
{
    public string Id { get; set; } = "";          // anthropic | ollama | openai | custom
    public string BaseUrl { get; set; } = "";     // empty = use the preset's
    public string Model { get; set; } = "";       // empty = preset default, or first
                                                  //   from ListModelsAsync if it has none
    public string EncryptedApiKey { get; set; } = "";
}
```

`AppSettings` gains:

- `string ActiveProviderId` — defaults to `"anthropic"`.
- `List<ProviderConfig> Providers` — each provider remembers its own model and key,
  so switching away and back is lossless.

### Migration

Extends the chain already in `AppSettings.LoadAsync` (`AppSettings.cs:146`) by one link:

```
ApiKey (legacy plaintext) -> EncryptedApiKey -> Providers["anthropic"].EncryptedApiKey
                                                Providers["anthropic"].Model <- settings.Model
```

Runs once on load and writes back. The top-level `EncryptedApiKey` and `Model` fields
remain readable but are no longer written, so an older build still starts. Migration
must be idempotent: a second load must not create a duplicate `anthropic` entry.

### Settings window

The API-key row becomes a provider block that reshapes according to the selected preset:

```
Provider    [ Ollama (local)              v ]
Base URL    [ http://localhost:11434/v1     ]   editable for all but Anthropic
Model       [ llama3.2:3b                 v ] [refresh]
API key     hidden when the preset needs none
            [ Test connection ]  Connected - 4 models
```

`Test connection` calls `ListModelsAsync`. On failure it reports the specific cause,
e.g. `Cannot reach http://localhost:11434 - is Ollama running?` with a link to
ollama.com. This is the entire onboarding story. No wizard, no installer detection.

### Overlay and tray

A provider-and-model dropdown sits beside the existing mode selector in the overlay
footer, mirrored by a tray submenu. Switching provider sets what the **next** run
uses; it does not silently re-fire the correction currently on screen. The user hits
Redo. This keeps the switch predictable and free.

Per the ComboBox note in CLAUDE.md, this dropdown needs a full custom
`ControlTemplate` on both the ComboBox and its items, or it renders light-on-light
against the dark overlay.

## Error handling

`OpenAiCompatibleProvider` maps failures into the same friendly vocabulary
`AnthropicProvider` already uses, plus the cases unique to local models:

| Condition | Message |
|---|---|
| 401 / 403 | API key is invalid. Check your key in Settings. |
| 429 | Rate limited - try again in a moment. |
| 5xx | {Provider} is unavailable. Try again later. |
| 404 on endpoint | Endpoint not found - check the Base URL (should end in /v1). |
| Model not found | Model 'X' isn't available. Pull it with `ollama pull X` |
| Connection refused | Cannot reach Ollama at localhost:11434 - is it running? |
| Timeout | Timed out after 120s - the model may still be loading. |
| Cancelled | Correction cancelled. (unchanged) |

Connection-refused matters most: it will be the most common local failure, and the
generic "check your connection" the current code would emit is actively misleading
when the real problem is that Ollama is not running.

### Chatty model output

Small local models often reply `Sure! Here's the corrected text:` instead of the text.
Today `LooksLikeRefusal` (`AiClient.cs:115`) converts that into a hard error, which on
a 3B model would produce frequent dead ends with no visibility.

New behaviour for non-Anthropic providers:

1. `ResponseSanitizer.Strip` removes the lead-in, fences and wrapping quotes.
2. If `LooksConversational` still returns true, the result is shown in the diff with a
   warning banner rather than being discarded.
3. The user can read the diff and choose Apply, Redo or Discard.

A conversational result must **suppress the auto-apply countdown**, so a stray
"Sure, here you go" can never be pasted without an explicit click.

## Latency

Per-provider timeouts replace the hardcoded 10s at `AiClient.cs:25`. Local presets get
120s because a cold Ollama model can spend 10-20s loading into RAM before the first
token. The overlay processing state gains a live elapsed counter and the provider name,
so a 30s cold start reads as working rather than hung.

## Cost

`CostEstimator.Estimate` takes a provider id.

- `ollama`, or `custom` whose base URL host is `localhost`, `127.0.0.1` or `::1`,
  return exactly `0m`. A `custom` provider on a LAN address is treated as remote and
  priced with the fallback, since we cannot know it is free.
  This fixes a real bug: the mid-range fallback at `CostEstimator.cs:20`
  currently charges unknown models at Claude Sonnet rates, so local inference would
  accrue fake cost in the stats and About window.
- Rates are added for the OpenAI preset defaults.
- Genuinely unknown remote models keep today's fallback. It is imprecise but no worse
  than current behaviour.

Explicitly out of scope: making the estimate nullable so the UI can show a dash for
unknown models. That ripples through `StatsAggregate`, `StatsTracker`,
`CorrectionHistory` and the About window, and is a separate change.

## Testing

The suite currently has **100 tests** (CLAUDE.md claims 23; that line is stale and will
be corrected as part of this work). Roughly 30 new tests, weighted toward pure logic.

| Area | Count | Cases |
|---|---|---|
| `ResponseSanitizer` | ~8 | lead-ins, fences, wrapping quotes, `LooksConversational` both ways |
| `AppSettings` migration | ~5 | legacy key+model lands on `anthropic`; full plaintext->encrypted->provider chain; idempotent on second load; per-provider DPAPI round-trip |
| `OpenAiCompatibleProvider` | ~6 | happy path, 401, 429, connection refused, timeout, empty `choices[]` |
| `ProviderPresets` / `ProviderFactory` | ~7 | unique ids, correct implementation per id, instance caching, unknown id falls back to Anthropic |
| `CostEstimator` | ~4 | Ollama is 0, custom-on-localhost is 0, known OpenAI rate, unknown remote uses fallback |

`OpenAiCompatibleProvider` tests require the injected `HttpMessageHandler`. Note the
contrast: `AiClientTests` has only 4 tests today precisely because `AnthropicClient` is
constructed in the ctor and cannot be stubbed. The new provider must not repeat that.

### Manual verification

Not unit-testable, so verify by hand before release:

- Real Ollama round-trip with a 3B model, including a cold start, to confirm 120s is
  enough.
- Real OpenAI key against `gpt-4o-mini`.
- Overlay provider switch, then Redo, lands on the newly selected provider.
- Switching away from Anthropic and back preserves the Claude model and key.
- Ollama stopped: the connection-refused message appears, not "check your connection".

## Known follow-ups

- `SettingsWindow.KnownModels` (`SettingsWindow.xaml.cs:16`) is a hardcoded Claude list
  containing model IDs that have not been verified as real. Left alone here; correcting
  it is a separate concern from provider wiring.
- Streaming responses.
- Nullable cost estimates so unknown models display a dash.
