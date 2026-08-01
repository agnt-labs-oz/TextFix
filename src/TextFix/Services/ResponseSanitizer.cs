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
