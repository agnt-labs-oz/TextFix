using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using TextFix.Models;

namespace TextFix.Services;

public class AiClient
{
    private readonly AnthropicClient _client;
    private readonly AppSettings _settings;
    private const int MaxTextLength = 5000;

    public AiClient(AppSettings settings)
    {
        var apiKey = settings.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is not configured. Set your API key in Settings.");

        _settings = settings;
        _client = new AnthropicClient
        {
            ApiKey = apiKey,
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return CorrectionResult.Error(text, "Text is empty.");

        if (text.Length > MaxTextLength)
            return CorrectionResult.Error(text, $"Text too long ({text.Length} chars). Select a shorter passage (max {MaxTextLength}).");

        try
        {
            var parameters = new MessageCreateParams
            {
                Model = _settings.Model,
                MaxTokens = 4096,
                System = systemPrompt,
                Messages =
                [
                    new MessageParam { Role = Role.User, Content = text },
                ],
            };

            var message = await _client.Messages.Create(parameters, ct);
            var corrected = message.Content
                .Select(block =>
                {
                    if (block.TryPickText(out var tb)) return tb.Text;
                    return null;
                })
                .Where(t => t is not null)
                .FirstOrDefault() ?? text;

            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected.Trim(),
            };
        }
        catch (OperationCanceledException)
        {
            return CorrectionResult.Error(text, "Correction cancelled.");
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
            return CorrectionResult.Error(text, "Network error — check your internet connection.");
        }
        catch (Exception ex)
        {
            return CorrectionResult.Error(text, $"Unexpected error: {ex.Message}");
        }
    }
}
