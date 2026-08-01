# Multi-Provider Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let TextFix run corrections against local models (Ollama) and any OpenAI-compatible API, alongside the existing Anthropic path, switchable from the overlay.

**Architecture:** An `IAiProvider` interface with two implementations — `AnthropicProvider` (the existing `AiClient`, renamed, keeping the SDK and its assistant-prefill trick) and `OpenAiCompatibleProvider` (raw `HttpClient` against `/v1/chat/completions`). Ollama, OpenAI and Custom are rows in a static preset table that all point at the same OpenAI-compatible implementation, so adding Groq or LM Studio later is a data change with no new code.

**Tech Stack:** .NET 10, WPF + WinForms, Anthropic SDK 12.13.0, `System.Net.Http.Json`, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-01-multi-provider-design.md`

## Global Constraints

- **No new NuGet packages.** `System.Net.Http.Json` and `System.Text.Json` are in the framework. If you find yourself wanting the OpenAI SDK, re-read the spec.
- **Providers never throw from `CorrectAsync`.** Every failure returns `CorrectionResult.Error(originalText, message)`. `CorrectionService` depends on this.
- **One shared static `HttpClient`** with `Timeout = Timeout.InfiniteTimeSpan`. Per-request timeouts come from a linked `CancellationTokenSource`, never from `HttpClient.Timeout` — a shared client cannot carry per-provider timeouts.
- **API keys are DPAPI-encrypted** with `DataProtectionScope.CurrentUser`. Never persist a plaintext key.
- **WPF ComboBox dark theme needs a full custom `ControlTemplate`** on both the ComboBox and its items. Setting `Foreground`/`Background` alone is silently ignored. Reuse `OverlayComboBox` / `OverlayComboBoxItem` from `OverlayWindow.xaml:110-170`.
- **Kill the running app before building** — it holds its DLLs: `taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build`
- **Test count is 105 before this work** (xUnit expands each `[Theory]`'s `InlineData` into its own case, so this is higher than the count of test *methods*). The per-task "expected N passing" figures below are approximate for the same reason — the binding requirement is that `dotnet test` is green and every new test passes, not that it hits a predicted total.
- Target framework `net10.0-windows`. Nullable enabled. File-scoped namespaces, matching the existing code.

---

## File Structure

**Create:**

| Path | Responsibility |
|---|---|
| `src/TextFix/Services/ResponseSanitizer.cs` | Pure string cleanup of chatty model output |
| `src/TextFix/Services/PromptTemplates.cs` | The two system-prompt suffixes (prefill / no-prefill) |
| `src/TextFix/Services/DpapiString.cs` | DPAPI protect/unprotect, extracted from `AppSettings` |
| `src/TextFix/Services/Providers/IAiProvider.cs` | The provider contract |
| `src/TextFix/Services/Providers/ProviderPreset.cs` | `ProviderPreset` record + `KeyRequirement` enum |
| `src/TextFix/Services/Providers/ProviderPresets.cs` | The static preset table + lookup |
| `src/TextFix/Services/Providers/AnthropicProvider.cs` | Moved from `AiClient.cs` |
| `src/TextFix/Services/Providers/OpenAiCompatibleProvider.cs` | `/v1/chat/completions` client |
| `src/TextFix/Services/Providers/ProviderFactory.cs` | `AppSettings` → cached `IAiProvider` |
| `src/TextFix/Models/ProviderConfig.cs` | Per-provider persisted state |

**Modify:** `Models/AppSettings.cs`, `Models/CorrectionResult.cs`, `Services/CostEstimator.cs`, `Services/StatsTracker.cs`, `Services/CorrectionService.cs`, `App.xaml.cs`, `Views/SettingsWindow.xaml{,.cs}`, `Views/OverlayWindow.xaml{,.cs}`, `CLAUDE.md`, `README.md`

**Delete:** `src/TextFix/Services/AiClient.cs` (becomes `AnthropicProvider.cs`), `tests/TextFix.Tests/Services/AiClientTests.cs` (becomes `AnthropicProviderTests.cs`)

---

## Task 1: ResponseSanitizer

Pure string functions with no I/O. Start here — it is the highest-value, lowest-risk piece, and nothing depends on it yet.

**Files:**
- Create: `src/TextFix/Services/ResponseSanitizer.cs`
- Test: `tests/TextFix.Tests/Services/ResponseSanitizerTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `ResponseSanitizer.Strip(string) -> string`, `ResponseSanitizer.LooksConversational(string) -> bool`

- [ ] **Step 1: Write the failing tests**

Create `tests/TextFix.Tests/Services/ResponseSanitizerTests.cs`:

```csharp
using TextFix.Services;

namespace TextFix.Tests.Services;

public class ResponseSanitizerTests
{
    [Theory]
    [InlineData("The quick brown fox", "The quick brown fox")]
    [InlineData("Sure! Here's the corrected text:\nThe quick brown fox", "The quick brown fox")]
    [InlineData("Here is the corrected version:\n\nThe quick brown fox", "The quick brown fox")]
    [InlineData("Corrected text:\nThe quick brown fox", "The quick brown fox")]
    public void Strip_RemovesLeadIn(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Theory]
    [InlineData("```\nThe quick brown fox\n```", "The quick brown fox")]
    [InlineData("```text\nThe quick brown fox\n```", "The quick brown fox")]
    public void Strip_RemovesCodeFences(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Theory]
    [InlineData("\"The quick brown fox\"", "The quick brown fox")]
    [InlineData("“The quick brown fox”", "The quick brown fox")]
    public void Strip_RemovesWrappingQuotes(string raw, string expected)
    {
        Assert.Equal(expected, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesInternalQuotes()
    {
        // Only balanced *wrapping* quotes go. A quoted phrase inside the text stays.
        var raw = "He said \"hello\" to her";
        Assert.Equal(raw, ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_PreservesMultilineBody()
    {
        var raw = "Sure, here you go:\nLine one\nLine two";
        Assert.Equal("Line one\nLine two", ResponseSanitizer.Strip(raw));
    }

    [Fact]
    public void Strip_HandlesEmptyAndWhitespace()
    {
        Assert.Equal("", ResponseSanitizer.Strip(""));
        Assert.Equal("", ResponseSanitizer.Strip("   \n  "));
    }

    [Theory]
    [InlineData("I'm unable to help with that")]
    [InlineData("I cannot process this request")]
    [InlineData("Sorry, the input is unclear")]
    [InlineData("Unfortunately this text cannot be corrected")]
    public void LooksConversational_DetectsRefusals(string text)
    {
        Assert.True(ResponseSanitizer.LooksConversational(text));
    }

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    [InlineData("Please review the attached document.")]
    [InlineData("")]
    public void LooksConversational_AllowsNormalText(string text)
    {
        Assert.False(ResponseSanitizer.LooksConversational(text));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ResponseSanitizerTests`
Expected: FAIL — build error, `ResponseSanitizer` does not exist.

- [ ] **Step 3: Implement `ResponseSanitizer`**

Create `src/TextFix/Services/ResponseSanitizer.cs`:

```csharp
namespace TextFix.Services;

/// <summary>
/// Cleans up chatty model output. Small local models often wrap the answer in a
/// lead-in, code fences or quotes instead of returning bare text. Anthropic does not
/// need this — its assistant-prefill already guarantees a bare response.
/// Pure functions, no I/O.
/// </summary>
public static class ResponseSanitizer
{
    // Matched case-insensitively against the first line. Only a *short* first line
    // ending in ':' is treated as a lead-in, so real text starting with "Here is the
    // report we discussed at length..." is never eaten.
    private static readonly string[] LeadInMarkers =
    [
        "here's the", "here is the", "sure", "certainly", "of course",
        "corrected text", "corrected version", "fixed text", "the corrected",
        "i've corrected", "i have corrected", "output", "result",
    ];

    private static readonly string[] ConversationalStarts =
    [
        "i'm unable", "i am unable", "i cannot", "i can't", "i won't",
        "sorry", "apologi", "unfortunately",
        "as an ai", "i'd be happy", "i would be happy",
    ];

    public static string Strip(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var text = raw.Trim();
        text = StripFences(text);
        text = StripLeadIn(text);
        text = StripWrappingQuotes(text);
        return text.Trim();
    }

    private static string StripFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return text;

        // Drop the opening fence line (which may carry a language tag) and any
        // trailing fence.
        var body = text[(firstNewline + 1)..].TrimEnd();
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3].TrimEnd();

        return body;
    }

    private static string StripLeadIn(string text)
    {
        var newline = text.IndexOf('\n');
        if (newline < 0) return text;

        var firstLine = text[..newline].Trim();

        // A lead-in is short and ends with a colon. Both conditions matter: without
        // the length cap, a genuine sentence ending in ':' would be discarded.
        if (firstLine.Length > 60 || !firstLine.EndsWith(':')) return text;

        var lower = firstLine.ToLowerInvariant();
        foreach (var marker in LeadInMarkers)
        {
            if (lower.StartsWith(marker, StringComparison.Ordinal))
                return text[(newline + 1)..].TrimStart();
        }

        return text;
    }

    private static string StripWrappingQuotes(string text)
    {
        if (text.Length < 2) return text;

        (char open, char close)[] pairs = [('"', '"'), ('\'', '\''), ('“', '”')];
        foreach (var (open, close) in pairs)
        {
            if (text[0] != open || text[^1] != close) continue;

            var inner = text[1..^1];
            // Only unwrap when the quotes genuinely wrap the whole string. If the
            // inner text still contains the closing quote, they were internal.
            if (!inner.Contains(close))
                return inner;
        }

        return text;
    }

    /// <summary>
    /// True when the text still reads like chat after stripping. Drives the overlay
    /// warning banner — it does not discard the result.
    /// </summary>
    public static bool LooksConversational(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var lower = text.TrimStart().ToLowerInvariant();
        foreach (var start in ConversationalStarts)
        {
            if (lower.StartsWith(start, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ResponseSanitizerTests`
Expected: PASS, 19 tests.

If `Strip_PreservesInternalQuotes` fails, the `inner.Contains(close)` guard in `StripWrappingQuotes` is missing or inverted.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: 119 passing (100 existing + 19 new).

- [ ] **Step 6: Commit**

```bash
git add src/TextFix/Services/ResponseSanitizer.cs tests/TextFix.Tests/Services/ResponseSanitizerTests.cs
git commit -m "feat: add ResponseSanitizer for chatty model output"
```

---

## Task 2: Provider presets

The data table that all per-provider variation lives in.

**Files:**
- Create: `src/TextFix/Services/Providers/ProviderPreset.cs`, `src/TextFix/Services/Providers/ProviderPresets.cs`
- Test: `tests/TextFix.Tests/Services/Providers/ProviderPresetsTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `enum KeyRequirement { None, Optional, Required }`
  - `record ProviderPreset(string Id, string DisplayName, string BaseUrl, KeyRequirement Key, string DefaultModel, int TimeoutSeconds, string TokenParam, bool IsOpenAiCompatible)`
  - `ProviderPresets.All -> IReadOnlyList<ProviderPreset>`
  - `ProviderPresets.Get(string? id) -> ProviderPreset` (falls back to anthropic)
  - `ProviderPresets.AnthropicId`, `.OllamaId`, `.OpenAiId`, `.CustomId` string constants

- [ ] **Step 1: Write the failing tests**

Create `tests/TextFix.Tests/Services/Providers/ProviderPresetsTests.cs`:

```csharp
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ProviderPresetsTests
{
    [Fact]
    public void All_ContainsFourProviders()
    {
        Assert.Equal(4, ProviderPresets.All.Count);
    }

    [Fact]
    public void All_IdsAreUnique()
    {
        var ids = ProviderPresets.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void All_HaveDisplayNames()
    {
        Assert.All(ProviderPresets.All, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
    }

    [Fact]
    public void Get_ReturnsMatchingPreset()
    {
        Assert.Equal(ProviderPresets.OllamaId, ProviderPresets.Get("ollama").Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-provider")]
    public void Get_UnknownId_FallsBackToAnthropic(string? id)
    {
        // A hand-edited settings file or a downgrade must not crash the app.
        Assert.Equal(ProviderPresets.AnthropicId, ProviderPresets.Get(id).Id);
    }

    [Fact]
    public void Anthropic_RequiresKeyAndIsNotOpenAiCompatible()
    {
        var p = ProviderPresets.Get(ProviderPresets.AnthropicId);
        Assert.Equal(KeyRequirement.Required, p.Key);
        Assert.False(p.IsOpenAiCompatible);
    }

    [Fact]
    public void Ollama_NeedsNoKeyAndHasLongTimeout()
    {
        var p = ProviderPresets.Get(ProviderPresets.OllamaId);
        Assert.Equal(KeyRequirement.None, p.Key);
        Assert.True(p.TimeoutSeconds >= 120, "cold model load can take 10-20s before first token");
        Assert.Equal("http://localhost:11434/v1", p.BaseUrl);
    }

    [Fact]
    public void OpenAi_UsesMaxCompletionTokens()
    {
        // Verified against OpenAI's API reference 2026-08-01: max_tokens is deprecated
        // and is rejected outright by o-series models.
        Assert.Equal("max_completion_tokens", ProviderPresets.Get(ProviderPresets.OpenAiId).TokenParam);
    }

    [Fact]
    public void LocalPresets_UseMaxTokens()
    {
        // Ollama and llama.cpp only understand max_tokens.
        Assert.Equal("max_tokens", ProviderPresets.Get(ProviderPresets.OllamaId).TokenParam);
        Assert.Equal("max_tokens", ProviderPresets.Get(ProviderPresets.CustomId).TokenParam);
    }

    [Fact]
    public void OpenAiCompatiblePresets_HaveTokenParam()
    {
        Assert.All(
            ProviderPresets.All.Where(p => p.IsOpenAiCompatible),
            p => Assert.False(string.IsNullOrWhiteSpace(p.TokenParam)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ProviderPresetsTests`
Expected: FAIL — build error, namespace `TextFix.Services.Providers` does not exist.

- [ ] **Step 3: Implement the preset types**

Create `src/TextFix/Services/Providers/ProviderPreset.cs`:

```csharp
namespace TextFix.Services.Providers;

public enum KeyRequirement
{
    /// <summary>Local providers. No Authorization header is sent at all.</summary>
    None,
    /// <summary>Custom endpoints — some need a key, some do not.</summary>
    Optional,
    Required,
}

/// <summary>
/// Everything that varies between providers. Adding a provider should be a new row
/// here and nothing else.
/// </summary>
/// <param name="BaseUrl">Empty for Anthropic, which goes through its SDK.</param>
/// <param name="TokenParam">
/// The JSON field naming the output-token cap. OpenAI deprecated <c>max_tokens</c> in
/// favour of <c>max_completion_tokens</c> and rejects the old name on o-series models;
/// Ollama and llama.cpp understand only <c>max_tokens</c>. Empty for Anthropic.
/// </param>
public record ProviderPreset(
    string Id,
    string DisplayName,
    string BaseUrl,
    KeyRequirement Key,
    string DefaultModel,
    int TimeoutSeconds,
    string TokenParam,
    bool IsOpenAiCompatible);
```

Create `src/TextFix/Services/Providers/ProviderPresets.cs`:

```csharp
namespace TextFix.Services.Providers;

public static class ProviderPresets
{
    public const string AnthropicId = "anthropic";
    public const string OllamaId = "ollama";
    public const string OpenAiId = "openai";
    public const string CustomId = "custom";

    public static IReadOnlyList<ProviderPreset> All { get; } =
    [
        new(AnthropicId, "Anthropic", "", KeyRequirement.Required,
            "claude-haiku-4-5-20251001", TimeoutSeconds: 10, TokenParam: "",
            IsOpenAiCompatible: false),

        // Local: no key, and a long timeout because a cold model can spend 10-20s
        // loading into RAM before producing its first token.
        new(OllamaId, "Ollama (local)", "http://localhost:11434/v1", KeyRequirement.None,
            DefaultModel: "", TimeoutSeconds: 120, TokenParam: "max_tokens",
            IsOpenAiCompatible: true),

        new(OpenAiId, "OpenAI", "https://api.openai.com/v1", KeyRequirement.Required,
            "gpt-4o-mini", TimeoutSeconds: 30, TokenParam: "max_completion_tokens",
            IsOpenAiCompatible: true),

        // Covers LM Studio, llama.cpp, OpenRouter, Groq, vLLM and corporate endpoints.
        new(CustomId, "Custom (OpenAI-compatible)", "", KeyRequirement.Optional,
            DefaultModel: "", TimeoutSeconds: 120, TokenParam: "max_tokens",
            IsOpenAiCompatible: true),
    ];

    /// <summary>
    /// Looks up a preset, falling back to Anthropic for an unknown id rather than
    /// throwing — a hand-edited settings file must not prevent startup.
    /// </summary>
    public static ProviderPreset Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ProviderPresetsTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Services/Providers/ tests/TextFix.Tests/Services/Providers/
git commit -m "feat: add provider preset table"
```

---

## Task 3: ProviderConfig and settings migration

**Files:**
- Create: `src/TextFix/Models/ProviderConfig.cs`, `src/TextFix/Services/DpapiString.cs`
- Modify: `src/TextFix/Models/AppSettings.cs`
- Test: `tests/TextFix.Tests/Models/AppSettingsTests.cs` (append)

**Interfaces:**
- Consumes: `ProviderPresets.AnthropicId` (Task 2)
- Produces:
  - `DpapiString.Protect(string) -> string`, `DpapiString.Unprotect(string) -> string`
  - `ProviderConfig { Id, BaseUrl, Model, EncryptedApiKey }` with `GetApiKey()` / `SetApiKey(string)`
  - `AppSettings.ActiveProviderId`, `AppSettings.Providers`, `AppSettings.GetProviderConfig(string id)`, `AppSettings.ActiveProvider`

- [ ] **Step 1: Write the failing tests**

Append to `tests/TextFix.Tests/Models/AppSettingsTests.cs` (inside the existing class):

```csharp
    [Fact]
    public async Task LoadAsync_MigratesLegacyKeyAndModelToAnthropicProvider()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new AppSettings { Model = "claude-opus-4-6" };
            legacy.SetApiKey("sk-ant-legacy");
            await legacy.SaveAsync(path);

            var loaded = await AppSettings.LoadAsync(path);

            var anthropic = loaded.GetProviderConfig("anthropic");
            Assert.Equal("sk-ant-legacy", anthropic.GetApiKey());
            Assert.Equal("claude-opus-4-6", anthropic.Model);
            Assert.Equal("anthropic", loaded.ActiveProviderId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_MigrationIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new AppSettings { Model = "claude-opus-4-6" };
            legacy.SetApiKey("sk-ant-legacy");
            await legacy.SaveAsync(path);

            var first = await AppSettings.LoadAsync(path);
            await first.SaveAsync(path);
            var second = await AppSettings.LoadAsync(path);

            // A second load must not append a duplicate anthropic entry.
            Assert.Single(second.Providers, p => p.Id == "anthropic");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ProviderConfig_ApiKeyRoundTripsThroughDpapi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            settings.GetProviderConfig("openai").SetApiKey("sk-openai-secret");
            await settings.SaveAsync(path);

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("sk-openai-secret", raw);

            var loaded = await AppSettings.LoadAsync(path);
            Assert.Equal("sk-openai-secret", loaded.GetProviderConfig("openai").GetApiKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void GetProviderConfig_CreatesAndReusesEntry()
    {
        var settings = new AppSettings();

        var first = settings.GetProviderConfig("ollama");
        first.Model = "llama3.2:3b";
        var second = settings.GetProviderConfig("ollama");

        Assert.Same(first, second);
        Assert.Equal("llama3.2:3b", second.Model);
    }

    [Fact]
    public void ActiveProvider_FollowsActiveProviderId()
    {
        var settings = new AppSettings { ActiveProviderId = "ollama" };
        settings.GetProviderConfig("ollama").Model = "qwen2.5:3b";

        Assert.Equal("qwen2.5:3b", settings.ActiveProvider.Model);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AppSettingsTests`
Expected: FAIL — `GetProviderConfig` and `ActiveProviderId` do not exist.

- [ ] **Step 3: Extract `DpapiString`**

Create `src/TextFix/Services/DpapiString.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace TextFix.Services;

/// <summary>
/// DPAPI string protection, scoped to the current Windows user. Extracted from
/// AppSettings so per-provider configs can reuse it instead of duplicating the
/// try/catch.
/// </summary>
public static class DpapiString
{
    /// <summary>Returns a base64 DPAPI blob, or "" for empty input.</summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to encrypt API key with DPAPI. Cannot store key securely.", ex);
        }
    }

    /// <summary>Returns "" when the blob is empty, corrupt, or from another user.</summary>
    public static string Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";

        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
        }
    }
}
```

- [ ] **Step 4: Create `ProviderConfig`**

Create `src/TextFix/Models/ProviderConfig.cs`:

```csharp
using System.Text.Json.Serialization;
using TextFix.Services;

namespace TextFix.Models;

/// <summary>
/// What one provider remembers. Each provider keeps its own model and key so
/// switching away and back is lossless.
/// </summary>
public class ProviderConfig
{
    public string Id { get; set; } = "";

    /// <summary>Empty means "use the preset's base URL".</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Empty means "use the preset's default model, or the first model returned by
    /// ListModelsAsync if the preset has no default" (Ollama's case).
    /// </summary>
    public string Model { get; set; } = "";

    public string EncryptedApiKey { get; set; } = "";

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrEmpty(EncryptedApiKey);

    public string GetApiKey() => DpapiString.Unprotect(EncryptedApiKey);

    public void SetApiKey(string? plainKey) => EncryptedApiKey = DpapiString.Protect(plainKey);
}
```

- [ ] **Step 5: Extend `AppSettings`**

In `src/TextFix/Models/AppSettings.cs`, add these members after `CustomModes` (around line 44):

```csharp
    /// <summary>Which provider corrections currently run against.</summary>
    public string ActiveProviderId { get; set; } = "anthropic";

    /// <summary>Per-provider model and key. Populated lazily by GetProviderConfig.</summary>
    public List<ProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Returns the config for <paramref name="id"/>, creating and storing an empty one
    /// on first use. Always returns the same instance for the same id, so callers can
    /// mutate the result directly.
    /// </summary>
    public ProviderConfig GetProviderConfig(string id)
    {
        var existing = Providers.FirstOrDefault(
            p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var created = new ProviderConfig { Id = id };
        Providers.Add(created);
        return created;
    }

    [JsonIgnore]
    public ProviderConfig ActiveProvider => GetProviderConfig(ActiveProviderId);
```

Replace the bodies of `GetApiKey` and `SetApiKey` (lines 73-115) to delegate to `DpapiString`, keeping the legacy-plaintext fallback:

```csharp
    public string GetApiKey()
    {
        var fromEncrypted = DpapiString.Unprotect(EncryptedApiKey);
        // Fall back to the legacy plaintext key during migration.
        return string.IsNullOrEmpty(fromEncrypted) ? ApiKey : fromEncrypted;
    }

    public void SetApiKey(string plainKey)
    {
        EncryptedApiKey = DpapiString.Protect(plainKey);
        ApiKey = ""; // Never re-persist the legacy plaintext field.
    }
```

Add `using TextFix.Services;` to the top of the file.

- [ ] **Step 6: Add the migration to `LoadAsync`**

In `LoadAsync`, immediately after the existing plaintext-to-encrypted migration block (currently ending at line 154, just before `return settings;`):

```csharp
            // Migrate the single top-level key+model onto the anthropic provider config.
            // Guarded on "no anthropic entry" rather than "Providers is empty" so it
            // stays idempotent across repeated loads.
            var hasAnthropic = settings.Providers.Any(
                p => string.Equals(p.Id, "anthropic", StringComparison.OrdinalIgnoreCase));
            if (!hasAnthropic)
            {
                var anthropic = settings.GetProviderConfig("anthropic");
                anthropic.EncryptedApiKey = settings.EncryptedApiKey;
                anthropic.Model = settings.Model;
                await settings.SaveAsync(path);
            }
```

Note: the top-level `EncryptedApiKey` and `Model` fields stay on the class and stay serialized, so an older build still starts against a migrated file. They are simply no longer the source of truth.

- [ ] **Step 7: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~AppSettingsTests`
Expected: PASS — 23 tests (18 existing + 5 new). The existing DPAPI tests must still pass; if they fail, `GetApiKey`'s legacy fallback was dropped.

- [ ] **Step 8: Run the whole suite and commit**

```bash
dotnet test
git add src/TextFix/Models/ src/TextFix/Services/DpapiString.cs tests/TextFix.Tests/Models/AppSettingsTests.cs
git commit -m "feat: per-provider settings with migration from single API key"
```

---

## Task 4: IAiProvider and AnthropicProvider

Rename-and-adapt. Behaviour must not change.

**Files:**
- Create: `src/TextFix/Services/Providers/IAiProvider.cs`, `src/TextFix/Services/Providers/AnthropicProvider.cs`, `src/TextFix/Services/PromptTemplates.cs`
- Delete: `src/TextFix/Services/AiClient.cs`, `tests/TextFix.Tests/Services/AiClientTests.cs`
- Create: `tests/TextFix.Tests/Services/Providers/AnthropicProviderTests.cs`

**Interfaces:**
- Consumes: `ResponseSanitizer` (Task 1), `ProviderPresets` (Task 2)
- Produces:
  - `IAiProvider { string DisplayName; string ProviderId; bool IsLocal; Task<CorrectionResult> CorrectAsync(string, string, CancellationToken); Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken); }`
  - `AnthropicProvider(string apiKey, string model, int timeoutSeconds)`
  - `PromptTemplates.PrefillSuffix`, `PromptTemplates.NoPrefillSuffix`, `PromptTemplates.UserMessage(string text)`
  - `PromptTemplates.MaxTextLength` = 5000 — lives here, not on either provider, so
    `OpenAiCompatibleProvider` does not have to reach into `AnthropicProvider` for it

- [ ] **Step 1: Write the failing tests**

Create `tests/TextFix.Tests/Services/Providers/AnthropicProviderTests.cs`:

```csharp
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class AnthropicProviderTests
{
    private static AnthropicProvider Make(string key = "sk-ant-test-key") =>
        new(key, "claude-haiku-4-5-20251001", timeoutSeconds: 10);

    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Make(""));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_Succeeds_WithApiKey()
    {
        Assert.NotNull(Make());
    }

    [Fact]
    public void ProviderId_IsAnthropic()
    {
        Assert.Equal(ProviderPresets.AnthropicId, Make().ProviderId);
    }

    [Fact]
    public void IsLocal_IsFalse()
    {
        Assert.False(Make().IsLocal);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextIsEmpty()
    {
        var result = await Make().CorrectAsync("", "Fix grammar.");

        Assert.True(result.IsError);
        Assert.Contains("empty", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var result = await Make().CorrectAsync(new string('a', 5001), "Fix grammar.");

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsKnownModelsWithoutNetwork()
    {
        var models = await Make().ListModelsAsync();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.StartsWith("claude-", m));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~AnthropicProviderTests`
Expected: FAIL — `AnthropicProvider` does not exist.

- [ ] **Step 3: Create `PromptTemplates`**

Create `src/TextFix/Services/PromptTemplates.cs`. The prefill suffix is lifted verbatim from `AiClient.cs:43`:

```csharp
namespace TextFix.Services;

/// <summary>
/// System-prompt suffixes. Anthropic can be pinned down with an assistant prefill;
/// OpenAI-compatible endpoints cannot, so they get a blunter instruction and their
/// output is run through <see cref="ResponseSanitizer"/>.
/// </summary>
public static class PromptTemplates
{
    /// <summary>Input cap, shared by every provider.</summary>
    public const int MaxTextLength = 5000;

    public const string PrefillSuffix =
        "\n\nYou are a text transformation tool, not a chatbot. Output ONLY the transformed text — nothing else. Never explain, comment, apologize, ask questions, or refuse. If the input is unclear or nonsensical, return it unchanged.";

    public const string NoPrefillSuffix =
        "\n\nYou are a text transformation tool, not a chatbot. Output ONLY the transformed text — nothing else. Do not add a preamble such as \"Here is the corrected text\". Do not wrap the output in quotes or code fences. Do not explain, comment, apologize, ask questions, or refuse. If the input is unclear or nonsensical, return it unchanged.";

    public static string UserMessage(string text) =>
        $"Transform this text:\n<text>\n{text}\n</text>\n\nOutput only the result:";
}
```

- [ ] **Step 4: Create `IAiProvider`**

Create `src/TextFix/Services/Providers/IAiProvider.cs`:

```csharp
using TextFix.Models;

namespace TextFix.Services.Providers;

public interface IAiProvider
{
    /// <summary>Shown in the overlay and tray switcher, e.g. "Ollama (local)".</summary>
    string DisplayName { get; }

    /// <summary>Preset id, stamped onto results for cost attribution.</summary>
    string ProviderId { get; }

    /// <summary>True when inference happens on this machine, so cost is exactly zero.</summary>
    bool IsLocal { get; }

    /// <summary>
    /// Never throws. Every failure comes back as CorrectionResult.Error.
    /// </summary>
    Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default);

    /// <summary>
    /// Models available from this provider. Also serves as the connection test.
    /// Throws on connection failure so the caller can report why.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Move `AiClient` to `AnthropicProvider`**

```bash
git mv src/TextFix/Services/AiClient.cs src/TextFix/Services/Providers/AnthropicProvider.cs
git rm tests/TextFix.Tests/Services/AiClientTests.cs
```

Then edit `AnthropicProvider.cs`: change the namespace to `TextFix.Services.Providers`, rename the class, implement the interface, and take explicit constructor parameters. The `CorrectAsync` body is unchanged apart from the prompt constant and the extra result fields:

```csharp
using System.Net.Http;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using TextFix.Models;

namespace TextFix.Services.Providers;

public class AnthropicProvider : IAiProvider
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public string DisplayName => "Anthropic";
    public string ProviderId => ProviderPresets.AnthropicId;
    public bool IsLocal => false;

    /// <summary>Offered in the model dropdown. Not fetched from the API.</summary>
    public static readonly string[] KnownModels =
    [
        "claude-haiku-4-5-20251001",
        "claude-sonnet-4-5-20250929",
        "claude-sonnet-4-6",
        "claude-opus-4-6",
    ];

    public AnthropicProvider(string apiKey, string model, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is not configured. Set your API key in Settings.");

        _model = string.IsNullOrWhiteSpace(model) ? KnownModels[0] : model;
        _client = new AnthropicClient
        {
            ApiKey = apiKey,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(KnownModels);

    public async Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return CorrectionResult.Error(text, "Text is empty.");

        if (text.Length > PromptTemplates.MaxTextLength)
            return CorrectionResult.Error(text, $"Text too long ({text.Length} chars). Select a shorter passage (max {PromptTemplates.MaxTextLength}).");

        try
        {
            var parameters = new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 4096,
                System = systemPrompt + PromptTemplates.PrefillSuffix,
                Messages =
                [
                    new MessageParam { Role = Role.User, Content = PromptTemplates.UserMessage(text) },
                    // Prefill: forces bare output. Anthropic-only — this is why
                    // AnthropicProvider does not need ResponseSanitizer.
                    new MessageParam { Role = Role.Assistant, Content = "<result>" },
                ],
            };

            var message = await _client.Messages.Create(parameters, ct);
            var raw = message.Content
                .Select(block => block.TryPickText(out var tb) ? tb.Text : null)
                .FirstOrDefault(t => t is not null) ?? text;

            var corrected = raw.Replace("</result>", "").Trim();

            if (string.IsNullOrWhiteSpace(corrected))
                return CorrectionResult.Error(text, "Couldn't improve this text — try selecting a clearer passage.");

            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected,
                Model = _model,
                ProviderId = ProviderId,
                IsLocal = false,
                InputTokens = (int)(message.Usage?.InputTokens ?? 0),
                OutputTokens = (int)(message.Usage?.OutputTokens ?? 0),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout throws TaskCanceledException, a subclass of
            // OperationCanceledException. Only reachable when the user did not cancel.
            return CorrectionResult.Error(text, "Request timed out — check your connection.");
        }
        catch (AnthropicUnauthorizedException)
        {
            return CorrectionResult.Error(text, "API key is invalid. Check your key in Settings.");
        }
        catch (AnthropicRateLimitException)
        {
            return CorrectionResult.Error(text, "Rate limited — try again in a moment.");
        }
        catch (Anthropic5xxException)
        {
            return CorrectionResult.Error(text, "Claude service is unavailable. Try again later.");
        }
        catch (AnthropicIOException)
        {
            return CorrectionResult.Error(text, "Network error — check your connection.");
        }
        catch (HttpRequestException)
        {
            return CorrectionResult.Error(text, "Cannot reach API — check your connection.");
        }
        catch (Exception)
        {
            return CorrectionResult.Error(text, "An unexpected error occurred.");
        }
    }
}
```

Note two deliberate changes: the old private `LooksLikeRefusal` is **gone** — that behaviour now lives in `ResponseSanitizer.LooksConversational` and applies only to non-Anthropic providers, per the spec. The `KnownModels` list drops the two IDs that were in `SettingsWindow.KnownModels` but are not verifiable model names.

- [ ] **Step 6: Add the new fields to `CorrectionResult`**

In `src/TextFix/Models/CorrectionResult.cs`, after `Model` (line 12):

```csharp
    public string ProviderId { get; init; } = "anthropic";
    public bool IsLocal { get; init; }
    /// <summary>Set when the model replied conversationally; drives the overlay warning.</summary>
    public bool LooksConversational { get; init; }
```

- [ ] **Step 7: Fix the two call sites so the build passes**

`App.xaml.cs:422` and `App.xaml.cs:456` construct `new AiClient(_settings)`, and `CorrectionService` holds an `AiClient`. Make the build green with the minimum change — Task 8 does the real wiring:

- In `CorrectionService.cs`, change the field, constructor parameter and `UpdateAiClient` parameter from `AiClient` to `IAiProvider`; rename the method to `UpdateProvider`. Add `using TextFix.Services.Providers;`.
- In `App.xaml.cs`, change the `_aiClient` field to `IAiProvider? _aiClient`, and both construction sites to:
  ```csharp
  new AnthropicProvider(_settings.GetApiKey(), _settings.Model, timeoutSeconds: 10)
  ```
  and `_correctionService?.UpdateAiClient(...)` to `UpdateProvider(...)`.

- [ ] **Step 8: Build and run the tests**

```bash
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
dotnet test
```
Expected: build clean, all tests pass (7 `AnthropicProviderTests` replace the 4 deleted `AiClientTests`).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: extract IAiProvider, rename AiClient to AnthropicProvider"
```

---

## Task 5: OpenAiCompatibleProvider

The core of the feature.

**Files:**
- Create: `src/TextFix/Services/Providers/OpenAiCompatibleProvider.cs`
- Test: `tests/TextFix.Tests/Services/Providers/OpenAiCompatibleProviderTests.cs`, `tests/TextFix.Tests/Services/Providers/StubHttpMessageHandler.cs`

**Interfaces:**
- Consumes: `IAiProvider`, `ProviderPreset` (Tasks 2, 4), `ResponseSanitizer` (Task 1), `PromptTemplates` (Task 4)
- Produces: `OpenAiCompatibleProvider(ProviderPreset preset, string baseUrl, string model, string apiKey, HttpMessageHandler? handler = null)`

- [ ] **Step 1: Write the stub handler**

Create `tests/TextFix.Tests/Services/Providers/StubHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace TextFix.Tests.Services.Providers;

/// <summary>
/// Returns a canned response, or throws a canned exception, for every request.
/// Records the last request body so tests can assert on the payload.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;
    private readonly Exception? _throw;
    private readonly TimeSpan _delay;

    public string? LastRequestBody { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode status, string body, TimeSpan? delay = null)
    {
        _status = status;
        _body = body;
        _delay = delay ?? TimeSpan.Zero;
    }

    public StubHttpMessageHandler(Exception toThrow) => _throw = toThrow;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throw is not null) throw _throw;

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/TextFix.Tests/Services/Providers/OpenAiCompatibleProviderTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class OpenAiCompatibleProviderTests
{
    private const string OkBody = """
    {
      "choices": [ { "message": { "role": "assistant", "content": "The quick brown fox" } } ],
      "usage": { "prompt_tokens": 42, "completion_tokens": 7 }
    }
    """;

    private static OpenAiCompatibleProvider Make(HttpMessageHandler handler, string? presetId = null)
    {
        var preset = ProviderPresets.Get(presetId ?? ProviderPresets.OllamaId);
        return new OpenAiCompatibleProvider(preset, preset.BaseUrl, "llama3.2:3b", apiKey: "", handler);
    }

    [Fact]
    public async Task CorrectAsync_HappyPath_ReturnsCorrectedTextAndTokens()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, OkBody));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        Assert.False(result.IsError);
        Assert.Equal("The quick brown fox", result.CorrectedText);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.True(result.IsLocal);
    }

    [Fact]
    public async Task CorrectAsync_StripsChattyPreamble()
    {
        const string chatty = """
        { "choices": [ { "message": { "content": "Sure! Here's the corrected text:\nThe quick brown fox" } } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, chatty));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        Assert.Equal("The quick brown fox", result.CorrectedText);
    }

    [Fact]
    public async Task CorrectAsync_FlagsConversationalOutputWithoutErroring()
    {
        const string refusal = """
        { "choices": [ { "message": { "content": "I cannot help with that request." } } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, refusal));

        var result = await provider.CorrectAsync("teh quick brown fox", "Fix errors.");

        // Shown with a warning banner, not swallowed as an error.
        Assert.False(result.IsError);
        Assert.True(result.LooksConversational);
    }

    [Fact]
    public async Task CorrectAsync_UsesPresetTokenParam()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        await Make(handler, ProviderPresets.OpenAiId).CorrectAsync("hi there", "Fix errors.");

        Assert.Contains("max_completion_tokens", handler.LastRequestBody!);
        Assert.DoesNotContain("\"max_tokens\"", handler.LastRequestBody!);
    }

    [Fact]
    public async Task CorrectAsync_OmitsAuthHeader_WhenNoKey()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        await Make(handler).CorrectAsync("hi there", "Fix errors.");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task CorrectAsync_SendsBearerToken_WhenKeyPresent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody);
        var preset = ProviderPresets.Get(ProviderPresets.OpenAiId);
        var provider = new OpenAiCompatibleProvider(preset, preset.BaseUrl, "gpt-4o-mini", "sk-test", handler);

        await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CorrectAsync_ReadsAlternateUsageFieldNames()
    {
        const string altUsage = """
        {
          "choices": [ { "message": { "content": "ok" } } ],
          "usage": { "input_tokens": 11, "output_tokens": 3 }
        }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, altUsage));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Equal(11, result.InputTokens);
        Assert.Equal(3, result.OutputTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.Forbidden, "API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited")]
    [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "unavailable")]
    public async Task CorrectAsync_MapsStatusCodesToFriendlyMessages(HttpStatusCode status, string expected)
    {
        var provider = Make(new StubHttpMessageHandler(status, "{}"));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains(expected, result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ModelNotFound_SuggestsOllamaPull()
    {
        const string body = """{ "error": { "message": "model 'llama3.2:3b' not found" } }""";
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.NotFound, body));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("ollama pull llama3.2:3b", result.ErrorMessage!);
    }

    [Fact]
    public async Task CorrectAsync_NotFoundWithoutModelHint_SuggestsCheckingBaseUrl()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.NotFound, "not found"));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.Contains("/v1", result.ErrorMessage!);
    }

    [Fact]
    public async Task CorrectAsync_ConnectionRefused_NamesTheHost()
    {
        var refused = new HttpRequestException("refused", new SocketException(10061));
        var provider = Make(new StubHttpMessageHandler(refused));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        // The generic "check your connection" would be actively misleading here.
        Assert.Contains("localhost:11434", result.ErrorMessage!);
        Assert.Contains("running", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_EmptyChoices_ReturnsError()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, """{ "choices": [] }"""));

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CorrectAsync_UserCancellation_ReportsCancelled()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var task = Make(handler).CorrectAsync("hi there", "Fix errors.", cts.Token);
        await cts.CancelAsync();

        var result = await task;

        Assert.True(result.IsError);
        Assert.Contains("cancelled", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_Timeout_MentionsModelLoading()
    {
        // Preset timeout of 1s against a handler that takes 5s.
        var preset = ProviderPresets.Get(ProviderPresets.OllamaId) with { TimeoutSeconds = 1 };
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, OkBody, TimeSpan.FromSeconds(5));
        var provider = new OpenAiCompatibleProvider(preset, preset.BaseUrl, "llama3.2:3b", "", handler);

        var result = await provider.CorrectAsync("hi there", "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("Timed out", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListModelsAsync_ParsesDataIds()
    {
        const string body = """
        { "data": [ { "id": "llama3.2:3b" }, { "id": "qwen2.5:7b" } ] }
        """;
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        var models = await provider.ListModelsAsync();

        Assert.Equal(["llama3.2:3b", "qwen2.5:7b"], models);
    }

    [Fact]
    public async Task ListModelsAsync_Throws_OnConnectionRefused()
    {
        // Unlike CorrectAsync, this surfaces the failure so Test Connection can report it.
        var refused = new HttpRequestException("refused", new SocketException(10061));
        var provider = Make(new StubHttpMessageHandler(refused));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.ListModelsAsync());
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var provider = Make(new StubHttpMessageHandler(HttpStatusCode.OK, OkBody));

        var result = await provider.CorrectAsync(new string('a', 5001), "Fix errors.");

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~OpenAiCompatibleProviderTests`
Expected: FAIL — `OpenAiCompatibleProvider` does not exist.

- [ ] **Step 4: Implement the provider**

Create `src/TextFix/Services/Providers/OpenAiCompatibleProvider.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextFix.Models;

namespace TextFix.Services.Providers;

/// <summary>
/// Speaks the /v1/chat/completions wire format shared by Ollama, LM Studio,
/// llama.cpp-server, OpenAI, OpenRouter, Groq and vLLM. One implementation serves
/// every non-Anthropic preset.
/// </summary>
public class OpenAiCompatibleProvider : IAiProvider
{
    // Shared across instances to avoid socket exhaustion. Timeout is infinite because
    // per-request deadlines come from a linked CancellationTokenSource — a shared
    // client cannot carry per-provider timeouts.
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly ProviderPreset _preset;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;

    public string DisplayName => _preset.DisplayName;
    public string ProviderId => _preset.Id;
    public bool IsLocal => IsLocalUrl(_baseUrl);

    public OpenAiCompatibleProvider(
        ProviderPreset preset,
        string baseUrl,
        string model,
        string apiKey,
        HttpMessageHandler? handler = null)
    {
        _preset = preset;
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? preset.BaseUrl : baseUrl).TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? preset.DefaultModel : model;
        _apiKey = apiKey ?? "";
        _http = handler is null ? SharedClient : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1"
            || host == "[::1]";
    }

    public async Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return CorrectionResult.Error(text, "Text is empty.");

        if (text.Length > PromptTemplates.MaxTextLength)
            return CorrectionResult.Error(text, $"Text too long ({text.Length} chars). Select a shorter passage (max {PromptTemplates.MaxTextLength}).");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_preset.TimeoutSeconds));

        try
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt + PromptTemplates.NoPrefillSuffix },
                    new { role = "user", content = PromptTemplates.UserMessage(text) },
                },
                ["temperature"] = 0.2,
                ["stream"] = false,
                // max_tokens vs max_completion_tokens — see ProviderPreset.TokenParam.
                [_preset.TokenParam] = 4096,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
            {
                Content = JsonContent.Create(body),
            };

            // Omitted entirely for local providers rather than sent empty.
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _http.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                return CorrectionResult.Error(text, MapStatusToMessage(response.StatusCode, errorBody));
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(timeoutCts.Token);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
                return CorrectionResult.Error(text, "The model returned an empty response — try again.");

            var corrected = ResponseSanitizer.Strip(content);
            if (string.IsNullOrWhiteSpace(corrected))
                return CorrectionResult.Error(text, "The model returned an empty response — try again.");

            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected,
                Model = _model,
                ProviderId = ProviderId,
                IsLocal = IsLocal,
                LooksConversational = ResponseSanitizer.LooksConversational(corrected),
                InputTokens = payload?.Usage?.InTokens ?? 0,
                OutputTokens = payload?.Usage?.OutTokens ?? 0,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (OperationCanceledException)
        {
            return CorrectionResult.Error(text,
                $"Timed out after {_preset.TimeoutSeconds}s — the model may still be loading.");
        }
        catch (HttpRequestException ex)
        {
            return CorrectionResult.Error(text, MapConnectionException(ex));
        }
        catch (JsonException)
        {
            return CorrectionResult.Error(text,
                $"Unexpected response from {_baseUrl} — is it an OpenAI-compatible endpoint?");
        }
        catch (Exception)
        {
            return CorrectionResult.Error(text, "An unexpected error occurred.");
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ModelsResponse>(timeoutCts.Token);
        return payload?.Data?
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList() ?? [];
    }

    private string MapStatusToMessage(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "API key is invalid. Check your key in Settings.",
        HttpStatusCode.TooManyRequests =>
            "Rate limited — try again in a moment.",
        HttpStatusCode.NotFound when body.Contains("model", StringComparison.OrdinalIgnoreCase) =>
            _preset.Id == ProviderPresets.OllamaId
                ? $"Model '{_model}' isn't available. Pull it with: ollama pull {_model}"
                : $"Model '{_model}' isn't available on this endpoint.",
        HttpStatusCode.NotFound =>
            $"Endpoint not found — check the Base URL (should end in /v1). Tried {_baseUrl}/chat/completions",
        >= HttpStatusCode.InternalServerError =>
            $"{_preset.DisplayName} is unavailable. Try again later.",
        _ => $"Request failed ({(int)status}). Check the Base URL and model in Settings.",
    };

    private string MapConnectionException(HttpRequestException ex)
    {
        // Connection-refused is the single most common local failure. The generic
        // "check your connection" is actively misleading when the real problem is
        // that Ollama simply is not running.
        if (ex.InnerException is SocketException && IsLocal)
        {
            var authority = Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri)
                ? $"{uri.Host}:{uri.Port}"
                : _baseUrl;
            var what = _preset.Id == ProviderPresets.OllamaId ? "Ollama" : "the local server";
            return $"Cannot reach {what} at {authority} — is it running?";
        }

        return $"Cannot reach {_baseUrl} — check your connection and the Base URL.";
    }

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChoiceMessage? Message);

    private sealed record ChoiceMessage(
        [property: JsonPropertyName("content")] string? Content);

    /// <summary>
    /// OpenAI uses prompt_tokens/completion_tokens; some compatible servers emit
    /// input_tokens/output_tokens. Accept both, default to zero.
    /// </summary>
    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens)
    {
        public int InTokens => PromptTokens ?? InputTokens ?? 0;
        public int OutTokens => CompletionTokens ?? OutputTokens ?? 0;
    }

    private sealed record ModelsResponse(
        [property: JsonPropertyName("data")] List<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string? Id);
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~OpenAiCompatibleProviderTests`
Expected: PASS, 20 tests.

If `CorrectAsync_Timeout_MentionsModelLoading` hangs, the linked CTS is not being passed to `SendAsync`.

- [ ] **Step 6: Run the whole suite and commit**

```bash
dotnet test
git add src/TextFix/Services/Providers/OpenAiCompatibleProvider.cs tests/TextFix.Tests/Services/Providers/
git commit -m "feat: add OpenAI-compatible provider for local and cloud endpoints"
```

---

## Task 6: ProviderFactory

**Files:**
- Create: `src/TextFix/Services/Providers/ProviderFactory.cs`
- Test: `tests/TextFix.Tests/Services/Providers/ProviderFactoryTests.cs`

**Interfaces:**
- Consumes: `AppSettings.ActiveProvider` (Task 3), both providers (Tasks 4, 5)
- Produces: `ProviderFactory(AppSettings settings)`, `.Create() -> IAiProvider?`

Returns `null` when a required key is missing — mirroring today's behaviour where `App._aiClient` is null until a key is set.

- [ ] **Step 1: Write the failing tests**

Create `tests/TextFix.Tests/Services/Providers/ProviderFactoryTests.cs`:

```csharp
using TextFix.Models;
using TextFix.Services.Providers;

namespace TextFix.Tests.Services.Providers;

public class ProviderFactoryTests
{
    [Fact]
    public void Create_ReturnsAnthropicProvider_ForAnthropicId()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.AnthropicId };
        settings.GetProviderConfig(ProviderPresets.AnthropicId).SetApiKey("sk-ant-test");

        Assert.IsType<AnthropicProvider>(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_ReturnsOpenAiCompatible_ForOllama()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };

        Assert.IsType<OpenAiCompatibleProvider>(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_ReturnsNull_WhenRequiredKeyMissing()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OpenAiId };

        Assert.Null(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_SucceedsWithoutKey_ForLocalProvider()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };

        Assert.NotNull(new ProviderFactory(settings).Create());
    }

    [Fact]
    public void Create_CachesInstance_WhenConfigUnchanged()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);

        Assert.Same(factory.Create(), factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenModelChanges()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.GetProviderConfig(ProviderPresets.OllamaId).Model = "qwen2.5:7b";

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_RebuildsInstance_WhenActiveProviderChanges()
    {
        var settings = new AppSettings { ActiveProviderId = ProviderPresets.OllamaId };
        var factory = new ProviderFactory(settings);
        var first = factory.Create();

        settings.ActiveProviderId = ProviderPresets.CustomId;
        settings.GetProviderConfig(ProviderPresets.CustomId).BaseUrl = "http://localhost:1234/v1";

        Assert.NotSame(first, factory.Create());
    }

    [Fact]
    public void Create_UnknownProviderId_FallsBackToAnthropic()
    {
        // Note the key goes on the *anthropic* config, not "bogus": Get("bogus")
        // resolves to the Anthropic preset, and the factory looks the config up by
        // preset.Id. A fallback to a keyless provider would correctly return null.
        var settings = new AppSettings { ActiveProviderId = "bogus" };
        settings.GetProviderConfig(ProviderPresets.AnthropicId).SetApiKey("sk-ant-test");

        Assert.IsType<AnthropicProvider>(new ProviderFactory(settings).Create());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ProviderFactoryTests`
Expected: FAIL — `ProviderFactory` does not exist.

- [ ] **Step 3: Implement**

Create `src/TextFix/Services/Providers/ProviderFactory.cs`:

```csharp
using TextFix.Models;

namespace TextFix.Services.Providers;

/// <summary>
/// Builds the active provider from settings, caching the instance until the
/// configuration that produced it changes.
/// </summary>
public class ProviderFactory(AppSettings settings)
{
    private IAiProvider? _cached;
    private string? _cacheKey;

    /// <summary>
    /// Returns null when the provider needs a key and none is configured — the caller
    /// treats that the same way it treats a missing Anthropic key today.
    /// </summary>
    public IAiProvider? Create()
    {
        var preset = ProviderPresets.Get(settings.ActiveProviderId);
        var config = settings.GetProviderConfig(preset.Id);
        var apiKey = config.GetApiKey();

        if (preset.Key == KeyRequirement.Required && string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Key length rather than the key itself, to keep secrets out of the cache key.
        var key = string.Join('|', preset.Id, config.BaseUrl, config.Model, apiKey.Length);
        if (_cached is not null && _cacheKey == key) return _cached;

        _cached = preset.IsOpenAiCompatible
            ? new OpenAiCompatibleProvider(preset, config.BaseUrl, config.Model, apiKey)
            : new AnthropicProvider(apiKey, config.Model, preset.TimeoutSeconds);
        _cacheKey = key;
        return _cached;
    }

    /// <summary>Forces a rebuild on the next Create call.</summary>
    public void Invalidate() => _cacheKey = null;
}
```

The config is looked up by `preset.Id`, not by the raw `ActiveProviderId`. That is what makes the unknown-id fallback land on the real Anthropic config rather than an empty one named after the bogus id.

- [ ] **Step 4: Run the tests and commit**

```bash
dotnet test --filter FullyQualifiedName~ProviderFactoryTests
dotnet test
git add src/TextFix/Services/Providers/ProviderFactory.cs tests/TextFix.Tests/Services/Providers/ProviderFactoryTests.cs
git commit -m "feat: add ProviderFactory with config-keyed caching"
```

---

## Task 7: Provider-aware cost estimation

Fixes a real bug: unknown models currently bill at Sonnet rates, so free local inference would accrue fake cost.

**Files:**
- Modify: `src/TextFix/Services/CostEstimator.cs`, `src/TextFix/Services/StatsTracker.cs:41`
- Test: `tests/TextFix.Tests/Services/CostEstimatorTests.cs` (append)

**Interfaces:**
- Consumes: `CorrectionResult.IsLocal` (Task 4)
- Produces: `CostEstimator.Estimate(string model, int inputTokens, int outputTokens, bool isLocal)`. The existing 3-argument overload stays, delegating with `isLocal: false`, so the 3 existing tests are untouched.

- [ ] **Step 1: Verify current OpenAI pricing**

Before writing rates, check <https://openai.com/api/pricing/> for the current per-million input/output prices of `gpt-4o-mini` and `gpt-4o`. Do not trust the numbers below without checking — `CostEstimator` already documents its rates as approximate, but being 10x wrong is not acceptable. Use the values you find in Step 3.

- [ ] **Step 2: Write the failing tests**

Append to `tests/TextFix.Tests/Services/CostEstimatorTests.cs`:

```csharp
    [Fact]
    public void Estimate_LocalModel_IsAlwaysFree()
    {
        // Local inference costs nothing. Without this, the mid-range fallback would
        // bill an Ollama run at Claude Sonnet rates.
        var cost = CostEstimator.Estimate("llama3.2:3b", 1_000_000, 1_000_000, isLocal: true);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_LocalFlagBeatsKnownModelName()
    {
        // A local server can serve a model whose name collides with a cloud one.
        var cost = CostEstimator.Estimate("claude-opus-4-6", 1_000_000, 1_000_000, isLocal: true);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_KnownOpenAiModel_UsesItsOwnRate()
    {
        // Must not fall back to the Sonnet mid-range rate.
        var cost = CostEstimator.Estimate("gpt-4o-mini", 1_000_000, 1_000_000, isLocal: false);
        Assert.True(cost > 0m);
        Assert.True(cost < 18.0m, "gpt-4o-mini must not be priced at the Sonnet fallback rate");
    }

    [Fact]
    public void Estimate_ThreeArgOverload_StillDefaultsToRemote()
    {
        Assert.Equal(
            CostEstimator.Estimate("claude-haiku-4-5-20251001", 1000, 1000, isLocal: false),
            CostEstimator.Estimate("claude-haiku-4-5-20251001", 1000, 1000));
    }
```

- [ ] **Step 3: Run to verify it fails, then implement**

Run: `dotnet test --filter FullyQualifiedName~CostEstimatorTests` → FAIL, no 4-argument overload.

Edit `src/TextFix/Services/CostEstimator.cs`. Add OpenAI rows to `Rates` — **replace these figures with what you found in Step 1**, they are a starting point, not verified:

```csharp
        // Source: https://openai.com/api/pricing — refresh when the model list changes.
        ["gpt-4o-mini"] = new(0.15m, 0.60m),
        ["gpt-4o"] = new(2.50m, 10m),
```

Then replace `Estimate`:

```csharp
    public static decimal Estimate(string model, int inputTokens, int outputTokens) =>
        Estimate(model, inputTokens, outputTokens, isLocal: false);

    /// <summary>
    /// Local inference is free regardless of model name — the flag wins over any
    /// rate-table match, since a local server can serve a cloud model's name.
    /// </summary>
    public static decimal Estimate(string model, int inputTokens, int outputTokens, bool isLocal)
    {
        if (isLocal) return 0m;

        var rate = Rates.GetValueOrDefault(model ?? "", Fallback);
        return inputTokens * rate.InputPerMillion / 1_000_000m
             + outputTokens * rate.OutputPerMillion / 1_000_000m;
    }
```

- [ ] **Step 4: Update the call site**

`src/TextFix/Services/StatsTracker.cs:41`:

```csharp
            CostEstimate = CostEstimator.Estimate(result.Model, result.InputTokens, result.OutputTokens, result.IsLocal),
```

- [ ] **Step 5: Run the tests and commit**

```bash
dotnet test
git add src/TextFix/Services/CostEstimator.cs src/TextFix/Services/StatsTracker.cs tests/TextFix.Tests/Services/CostEstimatorTests.cs
git commit -m "fix: local inference costs zero, add OpenAI rates"
```

---

## Task 8: Wire the factory into the app

**Files:**
- Modify: `src/TextFix/App.xaml.cs` (`SetupServicesAsync` ~line 417, `RebuildServices` ~line 454)

**Interfaces:**
- Consumes: `ProviderFactory` (Task 6)
- Produces: `App._providerFactory`, and a `SwitchProvider(string providerId)` method used by Tasks 9 and 10

- [ ] **Step 1: Replace direct construction with the factory**

In `App.xaml.cs`, replace the `_aiClient` field with:

```csharp
    private ProviderFactory? _providerFactory;
    private IAiProvider? _aiClient;
```

In `SetupServicesAsync`, replace the `if (!string.IsNullOrWhiteSpace(...)) _aiClient = new AiClient(_settings);` block with:

```csharp
        _providerFactory = new ProviderFactory(_settings);
        _aiClient = _providerFactory.Create();
```

Replace the body of `RebuildServices`:

```csharp
    private void RebuildServices()
    {
        _providerFactory?.Invalidate();
        _aiClient = _providerFactory?.Create();
        if (_aiClient is not null)
            _correctionService?.UpdateProvider(_aiClient);
    }
```

Add `using TextFix.Services.Providers;`.

- [ ] **Step 2: Add the provider switch entry point**

Add to `App.xaml.cs`, next to the existing mode-switching code:

```csharp
    /// <summary>
    /// Switches the active provider and persists it. Takes effect on the next
    /// correction — it deliberately does not re-run the result currently on screen.
    /// </summary>
    private async void SwitchProvider(string providerId)
    {
        _settings.ActiveProviderId = providerId;
        RebuildServices();
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }
```

Nothing calls `SwitchProvider` yet — Task 10 adds the overlay and tray callers, and adds the `RefreshProviderMenu()` call to this method at the same time. Do **not** add an empty `RefreshProviderMenu` stub here; an empty method with no caller is dead code, and Task 10 defines it properly.

- [ ] **Step 3: Handle the no-provider case on hotkey press**

Find where `OnHotkeyPressed` handles a null `_aiClient` and make the message provider-aware, so a user with Ollama selected is not told to check an API key:

```csharp
        if (_aiClient is null)
        {
            var preset = ProviderPresets.Get(_settings.ActiveProviderId);
            _overlay?.ShowProcessing(_settings.ActiveModeName);
            _overlay?.ShowResult(CorrectionResult.Error("",
                $"{preset.DisplayName} needs an API key. Add one in Settings."), 0);
            return;
        }
```

- [ ] **Step 4: Build, test, verify by hand**

```bash
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build
dotnet test
dotnet run --project src/TextFix/TextFix.csproj
```

Manual check: the app starts, the tray icon appears, and a correction against Anthropic still works exactly as before. This task changes no user-visible behaviour — that is the point.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/App.xaml.cs
git commit -m "refactor: build providers through ProviderFactory"
```

---

## Task 9: Settings window provider block

WPF UI. There is no meaningful unit test for XAML wiring, so this task is verified by hand — writing assertions against `ComboBox.Items` would be test theatre.

**Files:**
- Modify: `src/TextFix/Views/SettingsWindow.xaml` (API Key block, lines 150-175), `src/TextFix/Views/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `ProviderPresets`, `ProviderFactory`, `AppSettings.GetProviderConfig` (Tasks 2, 3, 6)
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Replace the API Key block in the XAML**

In `SettingsWindow.xaml`, replace the `<!-- API Key with show/copy buttons -->` StackPanel and the following Model StackPanel (lines 150-175) with:

```xml
        <!-- Provider -->
        <StackPanel>
            <Label Content="Provider"/>
            <ComboBox x:Name="ProviderBox" SelectionChanged="OnProviderChanged"/>
        </StackPanel>

        <!-- Base URL (hidden for Anthropic) -->
        <StackPanel x:Name="BaseUrlPanel">
            <Label Content="Base URL"/>
            <TextBox x:Name="BaseUrlBox" Style="{StaticResource FieldBox}"/>
        </StackPanel>

        <!-- API key (hidden when the provider needs none) -->
        <StackPanel x:Name="ApiKeyPanel">
            <Label Content="API Key"/>
            <Grid Margin="0,4,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <PasswordBox x:Name="ApiKeyBox" Grid.Column="0" Margin="0"/>
                <TextBox x:Name="ApiKeyTextBox" Grid.Column="0" Margin="0"
                         Style="{StaticResource FieldBox}" Visibility="Collapsed"
                         IsReadOnly="True"/>
                <Button Grid.Column="1" Content="&#x1F441;" Style="{StaticResource SmallButton}"
                        AutomationProperties.Name="Show or hide API key"
                        ToolTip="Show / hide key" Click="OnToggleKeyVisibility" Margin="4,0,0,0"/>
                <Button Grid.Column="2" Content="&#x1F4CB;" Style="{StaticResource SmallButton}"
                        AutomationProperties.Name="Copy API key to clipboard"
                        ToolTip="Copy key" Click="OnCopyKey" Margin="4,0,0,0"/>
            </Grid>
        </StackPanel>

        <!-- Model, with refresh + connection test -->
        <StackPanel>
            <Label Content="Model"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <ComboBox x:Name="ModelBox" Grid.Column="0" IsEditable="True"/>
                <Button x:Name="RefreshModelsButton" Grid.Column="1" Content="&#x21BB;"
                        Style="{StaticResource SmallButton}" Margin="4,0,0,0"
                        AutomationProperties.Name="Refresh model list"
                        ToolTip="Fetch available models" Click="OnRefreshModels"/>
            </Grid>
            <StackPanel x:Name="TestConnectionPanel" Orientation="Horizontal" Margin="0,6,0,0">
                <Button x:Name="TestConnectionButton" Content="Test connection"
                        Style="{StaticResource SmallButton}" Padding="8,2"
                        Click="OnTestConnection"/>
                <TextBlock x:Name="ConnectionStatusText" Margin="8,0,0,0" FontSize="11"
                           VerticalAlignment="Center" TextWrapping="Wrap"/>
            </StackPanel>
        </StackPanel>
```

- [ ] **Step 2: Rewrite the code-behind wiring**

In `SettingsWindow.xaml.cs`:

Delete the `KnownModels` array (lines 16-24) — it now lives on `AnthropicProvider`. Replace the model-population block in the constructor (lines 38-45) with:

```csharp
        foreach (var preset in ProviderPresets.All)
            ProviderBox.Items.Add(new ComboBoxItem { Content = preset.DisplayName, Tag = preset.Id });
        SelectProvider(settings.ActiveProviderId);
```

Add these members:

```csharp
    private string CurrentProviderId =>
        (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? ProviderPresets.AnthropicId;

    private void SelectProvider(string id)
    {
        for (var i = 0; i < ProviderBox.Items.Count; i++)
        {
            if (ProviderBox.Items[i] is ComboBoxItem item && (string)item.Tag == id)
            {
                ProviderBox.SelectedIndex = i;
                return;
            }
        }
        ProviderBox.SelectedIndex = 0;
    }

    /// <summary>Persists whatever is in the fields to the config for <paramref name="id"/>.</summary>
    private void StoreFieldsInto(string id)
    {
        var config = _settings.GetProviderConfig(id);
        config.BaseUrl = BaseUrlBox.Text.Trim();
        config.Model = (ModelBox.Text ?? "").Trim();
        var key = _keyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        config.SetApiKey(key);
    }

    private void LoadFieldsFrom(string id)
    {
        var preset = ProviderPresets.Get(id);
        var config = _settings.GetProviderConfig(id);

        BaseUrlBox.Text = string.IsNullOrWhiteSpace(config.BaseUrl) ? preset.BaseUrl : config.BaseUrl;
        ApiKeyBox.Password = config.GetApiKey();
        ApiKeyTextBox.Text = "";
        _keyVisible = false;
        ApiKeyBox.Visibility = Visibility.Visible;
        ApiKeyTextBox.Visibility = Visibility.Collapsed;

        ModelBox.Items.Clear();
        if (!preset.IsOpenAiCompatible)
        {
            foreach (var m in AnthropicProvider.KnownModels)
                ModelBox.Items.Add(m);
        }
        ModelBox.Text = string.IsNullOrWhiteSpace(config.Model) ? preset.DefaultModel : config.Model;

        // Anthropic goes through its SDK: no base URL to edit, and its ListModelsAsync
        // is a static list, so a connection test that cannot fail would be a lie.
        BaseUrlPanel.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        TestConnectionPanel.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        RefreshModelsButton.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = preset.Key == KeyRequirement.None ? Visibility.Collapsed : Visibility.Visible;

        ConnectionStatusText.Text = "";
    }

    private string? _loadedProviderId;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded && _loadedProviderId is null)
        {
            _loadedProviderId = CurrentProviderId;
            LoadFieldsFrom(_loadedProviderId);
            return;
        }

        // Keep the outgoing provider's edits before repainting for the incoming one.
        if (_loadedProviderId is not null)
            StoreFieldsInto(_loadedProviderId);

        _loadedProviderId = CurrentProviderId;
        LoadFieldsFrom(_loadedProviderId);
    }

    private async void OnRefreshModels(object sender, RoutedEventArgs e)
    {
        StoreFieldsInto(CurrentProviderId);
        var models = await TryListModelsAsync();
        if (models is null) return;

        var current = ModelBox.Text;
        ModelBox.Items.Clear();
        foreach (var m in models) ModelBox.Items.Add(m);
        ModelBox.Text = models.Contains(current) ? current : models.FirstOrDefault() ?? "";
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        StoreFieldsInto(CurrentProviderId);
        ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        ConnectionStatusText.Text = "Testing…";

        var models = await TryListModelsAsync();
        if (models is null) return;

        ConnectionStatusText.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        ConnectionStatusText.Text = $"Connected — {models.Count} model{(models.Count == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Lists models for the current provider, painting the failure into the status
    /// line and returning null. Never throws.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TryListModelsAsync()
    {
        var preset = ProviderPresets.Get(CurrentProviderId);
        var config = _settings.GetProviderConfig(preset.Id);

        try
        {
            var provider = new OpenAiCompatibleProvider(
                preset, config.BaseUrl, config.Model, config.GetApiKey());
            var models = await provider.ListModelsAsync();
            if (models.Count == 0)
            {
                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Goldenrod;
                ConnectionStatusText.Text = preset.Id == ProviderPresets.OllamaId
                    ? "Reachable, but no models pulled. Run: ollama pull llama3.2:3b"
                    : "Reachable, but the endpoint listed no models.";
                return null;
            }
            return models;
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            var url = string.IsNullOrWhiteSpace(config.BaseUrl) ? preset.BaseUrl : config.BaseUrl;
            ConnectionStatusText.Text = preset.Id == ProviderPresets.OllamaId
                ? $"Cannot reach {url} — is Ollama running? Install it from ollama.com"
                : $"Cannot reach {url} — {ex.Message}";
            return null;
        }
    }
```

Add `using TextFix.Services.Providers;`.

- [ ] **Step 3: Update `OnSave`**

In `OnSave` (line 269), replace the API-key and model lines (280-283) with:

```csharp
        StoreFieldsInto(CurrentProviderId);
        _settings.ActiveProviderId = CurrentProviderId;
        _settings.Hotkey = hotkeyText;
        _settings.ActiveModeName = ModeBox.SelectedItem as string ?? _settings.ActiveModeName;
```

- [ ] **Step 4: Build and verify by hand**

```bash
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build && dotnet run --project src/TextFix/TextFix.csproj
```

Verify each of these in the Settings window:

1. Provider dropdown lists all four providers.
2. Selecting **Anthropic** hides Base URL, hides Test connection, and shows the Claude model list.
3. Selecting **Ollama** shows Base URL prefilled with `http://localhost:11434/v1` and **hides** the API key field.
4. With Ollama running, **Test connection** shows `Connected — N models`, and the refresh button fills the model dropdown with your pulled models.
5. With Ollama stopped, Test connection shows the "is Ollama running?" message in red — **not** a generic network error.
6. Selecting **OpenAI** shows the API key field again with your OpenAI key, if previously saved.
7. Switch Anthropic → Ollama → Anthropic: the Claude model and key are still there.
8. Save, reopen Settings: everything persisted.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Views/SettingsWindow.xaml src/TextFix/Views/SettingsWindow.xaml.cs
git commit -m "feat: provider configuration in Settings with connection test"
```

---

## Task 10: Overlay and tray provider switcher

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml` (mode selector row ~line 388), `src/TextFix/Views/OverlayWindow.xaml.cs`, `src/TextFix/App.xaml.cs` (tray menu ~line 200)

**Interfaces:**
- Consumes: `App.SwitchProvider` (Task 8), `ProviderPresets` (Task 2)
- Produces: `OverlayWindow.ProviderChanged` event, `OverlayWindow.SetProviders(...)`

- [ ] **Step 1: Add the overlay dropdown**

In `OverlayWindow.xaml`, replace the mode selector `DockPanel` (line 388) with a two-column grid. Reuse the existing `OverlayComboBox` / `OverlayComboBoxItem` styles — a plain ComboBox renders light-on-light here:

```xml
                    <!-- Mode + provider selector row -->
                    <Grid Grid.Row="3" Margin="0,4,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <DockPanel Grid.Column="0" Margin="0,0,4,0">
                            <TextBlock Text="Mode:" Foreground="#888" FontSize="11"
                                       FontFamily="Segoe UI" VerticalAlignment="Center"
                                       Margin="0,0,6,0" DockPanel.Dock="Left"/>
                            <ComboBox x:Name="ModeBox" Style="{StaticResource OverlayComboBox}"
                                      SelectionChanged="OnModeChanged"/>
                        </DockPanel>
                        <DockPanel Grid.Column="1" Margin="4,0,0,0">
                            <TextBlock Text="Via:" Foreground="#888" FontSize="11"
                                       FontFamily="Segoe UI" VerticalAlignment="Center"
                                       Margin="0,0,6,0" DockPanel.Dock="Left"/>
                            <ComboBox x:Name="ProviderBox" Style="{StaticResource OverlayComboBox}"
                                      SelectionChanged="OnProviderChanged"/>
                        </DockPanel>
                    </Grid>
```

- [ ] **Step 2: Wire the overlay code-behind**

In `OverlayWindow.xaml.cs`, alongside the existing `ModeChanged` event (line 46):

```csharp
    public event Action<string>? ProviderChanged;

    /// <summary>Fills the provider dropdown and selects the active one.</summary>
    public void SetProviders(IReadOnlyList<(string Id, string Label)> providers, string activeId)
    {
        _suppressProviderEvent = true;
        ProviderBox.Items.Clear();
        foreach (var (id, label) in providers)
            ProviderBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });

        for (var i = 0; i < ProviderBox.Items.Count; i++)
        {
            if (ProviderBox.Items[i] is ComboBoxItem item && (string)item.Tag == activeId)
            {
                ProviderBox.SelectedIndex = i;
                break;
            }
        }
        _suppressProviderEvent = false;
    }

    private bool _suppressProviderEvent;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProviderEvent) return;
        if (ProviderBox.SelectedItem is ComboBoxItem item)
            ProviderChanged?.Invoke((string)item.Tag);
    }
```

Find the line that disables `ModeBox` while processing (line 235) and disable `ProviderBox` alongside it:

```csharp
        ModeBox.IsEnabled = enabled;
        ProviderBox.IsEnabled = enabled;
```

- [ ] **Step 3: Show the conversational warning**

In the method that populates the result state (`ShowResult`), after the diff is rendered, surface the flag. Add a `TextBlock` named `ConversationalWarning` above the diff in the XAML result panel:

```xml
                    <TextBlock x:Name="ConversationalWarning" Foreground="#FBBF24"
                               FontFamily="Segoe UI" FontSize="11" TextWrapping="Wrap"
                               Margin="0,0,0,6" Visibility="Collapsed"
                               Text="&#x26A0; The model may have replied conversationally — check the result before applying."/>
```

and in `ShowResult`:

```csharp
        ConversationalWarning.Visibility = result.LooksConversational
            ? Visibility.Visible
            : Visibility.Collapsed;
```

- [ ] **Step 4: Suppress auto-apply for conversational results**

In `App.xaml.cs`, in the `CorrectionCompleted` handler (~line 430), a conversational result must never auto-paste:

```csharp
                var autoApply = _settings.ManualApplyOnly || result.LooksConversational
                    ? 0
                    : _settings.OverlayAutoApplySeconds;
                _overlay?.ShowResult(result, autoApply, _settings.ManualApplyOnly || result.LooksConversational);
```

- [ ] **Step 5: Add the tray submenu**

In `App.xaml.cs`, after the mode menu is added (line 211):

```csharp
        var providerMenu = new ToolStripMenuItem("Provider");
        foreach (var preset in ProviderPresets.All)
        {
            var item = new ToolStripMenuItem(preset.DisplayName)
            {
                Tag = preset.Id,
                Checked = preset.Id == _settings.ActiveProviderId,
            };
            item.Click += (s, _) =>
            {
                if (s is ToolStripMenuItem mi && mi.Tag is string id) SwitchProvider(id);
            };
            providerMenu.DropDownItems.Add(item);
        }
        _trayIcon.ContextMenuStrip.Items.Add(providerMenu);
```

Add `RefreshProviderMenu`, and add a call to it inside `SwitchProvider` (from Task 8) right after `RebuildServices()`:

```csharp
    private void RefreshProviderMenu()
    {
        // ToolStripItemCollection is non-generic and not null-coalescible to [],
        // so guard the whole walk rather than the collection expression.
        var items = _trayIcon?.ContextMenuStrip?.Items;
        if (items is not null)
        {
            foreach (ToolStripItem top in items)
            {
                if (top is not ToolStripMenuItem { Text: "Provider" } providerMenu) continue;
                foreach (ToolStripMenuItem mi in providerMenu.DropDownItems)
                    mi.Checked = (string?)mi.Tag == _settings.ActiveProviderId;
            }
        }

        _overlay?.SetProviders(BuildProviderLabels(), _settings.ActiveProviderId);
    }

    /// <summary>Provider names with their configured model, e.g. "Ollama · llama3.2:3b".</summary>
    private List<(string Id, string Label)> BuildProviderLabels() =>
        ProviderPresets.All.Select(p =>
        {
            var config = _settings.GetProviderConfig(p.Id);
            var model = string.IsNullOrWhiteSpace(config.Model) ? p.DefaultModel : config.Model;
            return (p.Id, string.IsNullOrWhiteSpace(model) ? p.DisplayName : $"{p.DisplayName} · {model}");
        }).ToList();
```

Subscribe to the overlay event where `ModeChanged` is already wired:

```csharp
        _overlay.ProviderChanged += SwitchProvider;
```

and call `RefreshProviderMenu()` once during startup, after the overlay is created.

- [ ] **Step 6: Build and verify by hand**

```bash
taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build && dotnet run --project src/TextFix/TextFix.csproj
```

1. Trigger a correction — the overlay footer shows both Mode and Via dropdowns, dark-themed and readable.
2. Switch Via to Ollama, hit Redo — the correction runs against the local model.
3. The tray Provider submenu shows a check next to the active provider and stays in sync with the overlay.
4. Switching provider does **not** re-run the result currently on screen.
5. Both dropdowns are disabled while a correction is in flight.
6. Force a chatty reply on a small local model — the amber warning shows and the countdown does not start.

- [ ] **Step 7: Commit**

```bash
git add src/TextFix/Views/OverlayWindow.xaml src/TextFix/Views/OverlayWindow.xaml.cs src/TextFix/App.xaml.cs
git commit -m "feat: switch provider from the overlay and tray"
```

---

## Task 11: Elapsed counter in the processing state

Without this, a 30-second cold start reads as a hang.

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml` (processing panel), `src/TextFix/Views/OverlayWindow.xaml.cs`, `src/TextFix/App.xaml.cs` (`ProcessingStarted` handler ~line 427)

**Interfaces:**
- Consumes: nothing new
- Produces: `OverlayWindow.ShowProcessing(string modeName, string providerLabel, int timeoutSeconds)`

- [ ] **Step 1: Add the status line to the processing panel**

In `OverlayWindow.xaml`, inside the processing panel, below the existing "Correcting…" text:

```xml
                    <TextBlock x:Name="ProcessingDetailText" Foreground="#666666"
                               FontFamily="Segoe UI" FontSize="10" Margin="0,4,0,0"/>
```

- [ ] **Step 2: Drive it with a DispatcherTimer**

In `OverlayWindow.xaml.cs`:

```csharp
    private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };
    private DateTime _processingStartedAt;
    private string _processingProviderLabel = "";
    private int _processingTimeoutSeconds;
```

In the constructor:

```csharp
        _elapsedTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _processingStartedAt;
            // Showing the budget alongside the elapsed time is what turns a long
            // local cold start from "hung" into "working, and here's the deadline".
            var budget = _processingTimeoutSeconds > 0 ? $" / {_processingTimeoutSeconds}s" : "";
            ProcessingDetailText.Text =
                $"{_processingProviderLabel}   {elapsed.TotalSeconds:0.0}s{budget}";
        };
```

Change `ShowProcessing` to take the extra arguments and start the timer, keeping a single-argument overload so existing call sites keep compiling:

```csharp
    public void ShowProcessing(string modeName) => ShowProcessing(modeName, "", 0);

    public void ShowProcessing(string modeName, string providerLabel, int timeoutSeconds)
    {
        _processingStartedAt = DateTime.UtcNow;
        _processingProviderLabel = providerLabel;
        _processingTimeoutSeconds = timeoutSeconds;
        ProcessingDetailText.Text = providerLabel;
        _elapsedTimer.Start();

        // ... existing ShowProcessing body ...
    }
```

Stop the timer wherever the processing state ends — in `ShowResult`, the error path, and `FadeOutAndHide`:

```csharp
        _elapsedTimer.Stop();
```

Note the existing rule in CLAUDE.md: `FadeOutAndHide()` animates and calls `Hide()` itself. Do not add a separate `Hide()` call here.

- [ ] **Step 3: Pass the provider label from App**

In `App.xaml.cs`, in the `ProcessingStarted` handler:

```csharp
        _correctionService.ProcessingStarted += () =>
            Dispatcher.Invoke(() =>
            {
                var preset = ProviderPresets.Get(_settings.ActiveProviderId);
                var config = _settings.GetProviderConfig(preset.Id);
                var model = string.IsNullOrWhiteSpace(config.Model) ? preset.DefaultModel : config.Model;
                var label = string.IsNullOrWhiteSpace(model)
                    ? preset.DisplayName
                    : $"{preset.DisplayName} · {model}";
                _overlay?.ShowProcessing(_settings.ActiveModeName, label, preset.TimeoutSeconds);
            });
```

- [ ] **Step 4: Build and verify by hand**

Run a correction against Ollama with a model that is **not** currently loaded, so you get a genuine cold start:

```bash
ollama stop llama3.2:3b
```

1. The overlay shows `Ollama (local) · llama3.2:3b   4.3s / 120s`, ticking upward.
2. Cancel still works mid-flight.
3. The counter stops when the result appears and does not resume.
4. Note the actual cold-start time. If it exceeds 120s on your hardware, raise `TimeoutSeconds` for the Ollama preset in `ProviderPresets.cs` and say so in the commit message.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Views/OverlayWindow.xaml src/TextFix/Views/OverlayWindow.xaml.cs src/TextFix/App.xaml.cs
git commit -m "feat: show elapsed time and provider while correcting"
```

---

## Task 12: Documentation

**Files:**
- Modify: `CLAUDE.md`, `README.md`

- [ ] **Step 1: Update `CLAUDE.md`**

Four edits:

1. **Current State** — change "v0.2" to describe multi-provider support: hotkey-triggered select-correct-replace with a floating overlay, six correction modes, and a choice of Anthropic, Ollama, OpenAI or any OpenAI-compatible endpoint, switchable from the overlay.
2. **Future** — remove "Multiple AI providers" and "start-with-Windows" (both now done). Leave custom user-defined modes and real-time auto-correction; add streaming responses.
3. **Architecture tree** — replace the `Services/AiClient.cs` line with the `Services/Providers/` subtree, and add `Models/ProviderConfig.cs`.
4. **Testing** — replace the stale count. Run `dotnet test` and use the real number:

```markdown
## Testing

```bash
dotnet test                                              # all NNN tests
dotnet test --filter FullyQualifiedName~AppSettingsTests  # single test class
```
```

Add two entries to **Key Design Decisions**:

```markdown
- **One OpenAI-compatible client serves Ollama, OpenAI and custom endpoints** — they share the `/v1/chat/completions` wire format, so adding a provider is a row in `ProviderPresets`, not new code. Anthropic keeps its own SDK for the assistant-prefill trick and typed exceptions.
- **Per-provider timeouts come from a linked `CancellationTokenSource`, not `HttpClient.Timeout`** — the `HttpClient` is shared and static to avoid socket exhaustion, so it cannot carry a per-provider deadline.
- **`max_tokens` vs `max_completion_tokens`** — OpenAI deprecated the former and rejects it on o-series models; Ollama and llama.cpp understand only the former. This is the `TokenParam` field on `ProviderPreset`.
```

- [ ] **Step 2: Update `README.md`**

Add a "Providers" section after the setup instructions covering: the four options; that Ollama needs `ollama pull <model>` first and no API key; that Custom covers LM Studio, llama.cpp, OpenRouter and Groq by pasting their base URL; and that local models cost nothing and never send text off the machine. Move "Multiple AI providers" out of the Planned list.

- [ ] **Step 3: Commit**

```bash
dotnet test
git add CLAUDE.md README.md
git commit -m "docs: multi-provider setup and corrected test count"
```

---

## Final verification

- [ ] `taskkill /IM TextFix.exe /F 2>/dev/null; dotnet build` — clean, no warnings introduced
- [ ] `dotnet test` — all green
- [ ] Anthropic correction works exactly as before the change
- [ ] Ollama correction works end to end, including a cold start
- [ ] OpenAI correction works with a real key
- [ ] Ollama stopped → the "is Ollama running?" message, not a generic network error
- [ ] Switching Anthropic → Ollama → Anthropic preserves each provider's model and key
- [ ] The About window shows $0.00 session cost after local-only corrections
- [ ] Settings persist across an app restart
