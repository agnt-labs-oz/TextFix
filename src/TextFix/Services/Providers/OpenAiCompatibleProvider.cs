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
    private readonly AppLog? _log;

    public string DisplayName => _preset.DisplayName;
    public string ProviderId => _preset.Id;
    public bool IsLocal => IsLocalUrl(_baseUrl);

    public OpenAiCompatibleProvider(
        ProviderPreset preset,
        string baseUrl,
        string model,
        string apiKey,
        HttpMessageHandler? handler = null,
        AppLog? log = null)
    {
        _log = log;
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
                // Log the raw body, not just the message we derived from it: when the
                // shape is one ApiErrorBody does not recognise, this is the only record
                // of what the endpoint actually said.
                _log?.Warn($"[{_preset.Id}/{_model}] HTTP {(int)response.StatusCode} from {_baseUrl} — "
                    + ApiErrorBody.Truncate(errorBody));
                return Fail(text, MapStatusToMessage(response.StatusCode, errorBody), logged: true);
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(timeoutCts.Token);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
                return Fail(text, "The model returned an empty response — try again.");

            // Pass the original so the sanitizer can tell our <text> scaffolding apart
            // from a user genuinely correcting XML that contains a <text> element.
            var corrected = ResponseSanitizer.Strip(content, text);
            if (string.IsNullOrWhiteSpace(corrected))
                return Fail(text, "The model returned an empty response — try again.");

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
            // A deliberate cancel is not a fault — nothing to log.
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            return Fail(text, $"Timed out after {_preset.TimeoutSeconds}s — the model may still be loading.", ex);
        }
        catch (HttpRequestException ex)
        {
            return Fail(text, MapConnectionException(ex), ex);
        }
        catch (JsonException ex)
        {
            return Fail(text, $"Unexpected response from {_baseUrl} — is it an OpenAI-compatible endpoint?", ex);
        }
        catch (Exception ex)
        {
            // Naming the type costs the user nothing and turns an unanswerable bug
            // report into a searchable one.
            return Fail(text, $"An unexpected error occurred ({ex.GetType().Name}). See tray → Open log folder.",
                ex, unexpected: true);
        }
    }

    /// <summary>
    /// Records a failure and returns it. Every error path goes through here, so a
    /// correction that fails always leaves a trace — the absence of one was the whole
    /// reason "An unexpected error occurred" was impossible to diagnose.
    /// </summary>
    /// <remarks>
    /// Mapped failures log at Warn and genuinely unexpected ones at Error, both of which
    /// survive the default LogLevel of Warn, so diagnostics work as shipped.
    /// <paramref name="logged"/> is for the non-success-status path, which has already
    /// written a richer line carrying the raw response body.
    /// </remarks>
    private CorrectionResult Fail(
        string text, string message, Exception? ex = null, bool unexpected = false, bool logged = false)
    {
        if (!logged)
        {
            var line = $"[{_preset.Id}/{_model}] {message}";
            if (unexpected) _log?.Error(line, ex);
            else _log?.Warn(line, ex);
        }
        return CorrectionResult.Error(text, message);
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

    /// <remarks>
    /// Where we have a better answer than the server's own wording — a 401 always means
    /// the key, a 404 mentioning a model is worth turning into the exact
    /// <c>ollama pull</c> command — the specific message wins. Everywhere else the
    /// server's explanation is quoted, because a bare "Request failed (400)" is precisely
    /// the dead end this whole change exists to remove.
    /// </remarks>
    private string MapStatusToMessage(HttpStatusCode status, string body)
    {
        var detail = ApiErrorBody.ExtractMessage(body);

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "API key is invalid. Check your key in Settings.",
            // OpenAI reports an EMPTY BALANCE as 429 ("insufficient_quota"), the same
            // status as transient rate limiting. Telling that user "try again in a
            // moment" is actively wrong — no amount of retrying fixes billing — so when
            // the body says quota, quote it instead.
            HttpStatusCode.TooManyRequests when detail is not null
                && (detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("billing", StringComparison.OrdinalIgnoreCase)) =>
                $"{_preset.DisplayName} rejected the request (429): {detail}",
            HttpStatusCode.TooManyRequests =>
                "Rate limited — try again in a moment.",
            HttpStatusCode.NotFound when body.Contains("model", StringComparison.OrdinalIgnoreCase) =>
                _preset.Id == ProviderPresets.OllamaId
                    ? $"Model '{_model}' isn't available. Pull it with: ollama pull {_model}"
                    : $"Model '{_model}' isn't available on this endpoint.",
            HttpStatusCode.NotFound =>
                $"Endpoint not found — check the Base URL (should end in /v1). Tried {_baseUrl}/chat/completions",
            >= HttpStatusCode.InternalServerError => detail is null
                ? $"{_preset.DisplayName} is unavailable. Try again later."
                : $"{_preset.DisplayName} is unavailable: {detail}",
            _ => detail is null
                ? $"Request failed ({(int)status}). Check the Base URL and model in Settings."
                : $"{_preset.DisplayName} rejected the request ({(int)status}): {detail}",
        };
    }

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
