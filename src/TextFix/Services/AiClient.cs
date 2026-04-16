using System.Net.Http;
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
                System = systemPrompt + "\n\nYou are a text transformation tool, not a chatbot. Output ONLY the transformed text — nothing else. Never explain, comment, apologize, ask questions, or refuse. If the input is unclear or nonsensical, return it unchanged.",
                Messages =
                [
                    new MessageParam { Role = Role.User, Content = $"Transform this text:\n<text>\n{text}\n</text>\n\nOutput only the result:" },
                    new MessageParam { Role = Role.Assistant, Content = "<result>" },
                ],
            };

            var message = await _client.Messages.Create(parameters, ct);
            var raw = message.Content
                .Select(block =>
                {
                    if (block.TryPickText(out var tb)) return tb.Text;
                    return null;
                })
                .Where(t => t is not null)
                .FirstOrDefault() ?? text;

            // Strip the closing </result> tag from the prefilled response
            var corrected = raw
                .Replace("</result>", "")
                .Trim();

            // Detect when the model returned a conversational response instead of
            // corrected text — this happens with ambiguous/nonsensical input.
            if (string.IsNullOrWhiteSpace(corrected) || LooksLikeRefusal(corrected))
                return CorrectionResult.Error(text, "Couldn't improve this text — try selecting a clearer passage.");

            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout throws TaskCanceledException (subclass of OperationCanceledException)
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
        catch (Exception ex)
        {
            return CorrectionResult.Error(text, $"Unexpected error: {ex.Message}");
        }
    }

    private static bool LooksLikeRefusal(string response)
    {
        // If the response is much longer than expected for a correction and
        // starts with common refusal/explanation patterns, it's not corrected text.
        var lower = response.TrimStart().ToLowerInvariant();
        string[] refusalStarts =
        [
            "i'm unable", "i am unable", "i cannot", "i can't",
            "the input", "the text", "this text", "this input",
            "sorry", "apologi", "unfortunately",
        ];
        foreach (var prefix in refusalStarts)
        {
            if (lower.StartsWith(prefix))
                return true;
        }
        return false;
    }
}
