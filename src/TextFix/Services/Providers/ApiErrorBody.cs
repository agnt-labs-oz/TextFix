using System.Text.Json;

namespace TextFix.Services.Providers;

/// <summary>
/// Pulls the human-readable explanation out of a failed request's response body.
/// </summary>
/// <remarks>
/// Every endpoint we speak to explains its refusals in prose — "your credit balance is
/// too low", "model 'x' not found, try pulling it first", "this model does not support
/// max_tokens" — and all of it used to be discarded in favour of a bare status code.
/// That is what made "An unexpected error occurred" the only thing a user ever saw for
/// the whole 400/403/422 family, with nothing written to the log either.
///
/// Two shapes cover every provider in <see cref="ProviderPresets"/>:
///   OpenAI, Anthropic, OpenRouter, Groq:  {"error": {"message": "..."}}
///   Ollama, llama.cpp-server:             {"error": "..."}
/// Anything else — notably the HTML error page a misconfigured proxy or a Base URL
/// pointing at a plain web server returns — yields null rather than a wall of markup.
/// </remarks>
public static class ApiErrorBody
{
    /// <summary>
    /// Longest server-supplied text we will put in front of the user or into the log.
    /// Long enough for a real explanation, short enough that a runaway body cannot blow
    /// out the overlay or the log file.
    /// </summary>
    public const int MaxLength = 300;

    /// <summary>
    /// Returns the server's explanation, truncated, or null when the body carries none
    /// in a shape we recognise.
    /// </summary>
    public static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;

            var message = error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object when error.TryGetProperty("message", out var m)
                    && m.ValueKind == JsonValueKind.String => m.GetString(),
                _ => null,
            };

            return string.IsNullOrWhiteSpace(message) ? null : Truncate(message.Trim());
        }
        catch (JsonException)
        {
            // An HTML page, a bare string, a truncated stream — none of it is an error
            // message we can quote, and a parse failure here must not mask the HTTP
            // failure the caller is already reporting.
            return null;
        }
    }

    /// <summary>Caps a string for display and logging.</summary>
    public static string Truncate(string text, int max = MaxLength) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "…");
}
