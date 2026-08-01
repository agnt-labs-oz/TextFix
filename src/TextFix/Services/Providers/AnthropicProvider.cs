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
