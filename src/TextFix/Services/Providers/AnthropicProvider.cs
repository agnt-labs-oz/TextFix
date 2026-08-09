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
    private readonly AppLog? _log;

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

    public AnthropicProvider(string apiKey, string model, int timeoutSeconds, AppLog? log = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is not configured. Set your API key in Settings.");

        _log = log;
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
            // A deliberate cancel is not a fault — nothing to log.
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient timeout throws TaskCanceledException, a subclass of
            // OperationCanceledException. Only reachable when the user did not cancel.
            return Fail(text, "Request timed out — check your connection.", ex);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            return Fail(text, "API key is invalid. Check your key in Settings.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            return Fail(text, "Rate limited — try again in a moment.", ex);
        }
        catch (Anthropic5xxException ex)
        {
            return Fail(text, "Claude service is unavailable. Try again later.", ex);
        }
        catch (AnthropicApiException ex)
        {
            // Everything left in the 4xx family — 400, 403, 404, 422, and any status the
            // SDK does not model — reaches here, and every one of them used to fall
            // through to the catch-all below as "An unexpected error occurred."
            //
            // That mattered most for the failure it hid best: a 400 whose body reads
            // "your credit balance is too low". Key valid, model valid, network fine,
            // and the app said only that something unexpected happened.
            var detail = ApiErrorBody.ExtractMessage(ex.ResponseBody);
            var status = (int)ex.StatusCode;
            return Fail(text, detail is not null
                ? $"Anthropic rejected the request ({status}): {detail}"
                : $"Anthropic rejected the request ({status}). See tray → Open log folder.", ex);
        }
        catch (AnthropicIOException ex)
        {
            return Fail(text, "Network error — check your connection.", ex);
        }
        catch (HttpRequestException ex)
        {
            return Fail(text, "Cannot reach API — check your connection.", ex);
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
    /// survive the default LogLevel of Warn. Diagnostics therefore work as shipped,
    /// without the user first having to know that a log level exists.
    ///
    /// The exception goes to <see cref="AppLog"/> rather than being interpolated into the
    /// message, because AppLog formats exceptions by hand specifically to avoid
    /// ToString() round-tripping authorization headers into the file.
    /// </remarks>
    private CorrectionResult Fail(string text, string message, Exception? ex = null, bool unexpected = false)
    {
        var line = $"[anthropic/{_model}] {message}";
        if (unexpected) _log?.Error(line, ex);
        else _log?.Warn(line, ex);
        return CorrectionResult.Error(text, message);
    }
}
