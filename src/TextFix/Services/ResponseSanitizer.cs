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
    //
    // Every marker is deliberately multi-word or an unambiguous interjection. Bare
    // nouns like "output:" and "result:" are NOT markers — they are common labels in
    // real text (shell transcripts, Q&A notes), and stripping them silently deletes
    // user content, which is the one thing this class must never do.
    private static readonly string[] LeadInMarkers =
    [
        "here's the", "here is the", "sure", "certainly", "of course",
        "corrected text", "corrected version", "fixed text", "the corrected",
        "i've corrected", "i have corrected",
    ];

    private static readonly string[] ConversationalStarts =
    [
        "i'm unable", "i am unable", "i cannot", "i can't", "i won't",
        "sorry", "apologi", "unfortunately",
        "as an ai", "i'd be happy", "i would be happy",
    ];

    /// <summary>
    /// Tags this app itself puts in front of the model — <c>&lt;text&gt;</c> from
    /// <see cref="PromptTemplates.UserMessage"/> and <c>&lt;result&gt;</c> from the
    /// Anthropic prefill. Small models routinely echo the wrapper they were shown.
    /// </summary>
    private static readonly string[] WrapperTags = ["text", "result"];

    /// <param name="originalText">
    /// The text being corrected. Used only to tell our scaffolding apart from the
    /// user's own content — see <see cref="StripWrapperTags"/>. Optional, but pass it
    /// whenever it is available.
    /// </param>
    public static string Strip(string? raw, string? originalText = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var text = raw.Trim();
        text = StripFences(text);
        text = StripWrapperTags(text, originalText);
        text = StripLeadIn(text);
        text = StripWrappingQuotes(text);
        return text.Trim();
    }

    /// <summary>
    /// Removes a <c>&lt;text&gt;</c>/<c>&lt;result&gt;</c> wrapper the model copied
    /// from the prompt.
    /// </summary>
    /// <remarks>
    /// We wrap the input in <c>&lt;text&gt;…&lt;/text&gt;</c> to delimit it, and a 3B
    /// local model will happily hand the delimiters back — the very first real Ollama
    /// correction returned "&lt;text&gt;\nThe quick brown fox…\n&lt;/text&gt;". Teaching
    /// the model a tag obliges us to handle getting it back.
    ///
    /// The guard that matters: if the user's own selection used the tag — they are
    /// correcting XML that happens to contain a <c>&lt;text&gt;</c> element — then those
    /// tags are their content, and stripping them would silently corrupt the document.
    /// This class must never delete user content, so in that case we leave the response
    /// alone and accept the rarer cosmetic failure.
    ///
    /// Otherwise every occurrence goes, wherever it landed. An earlier version only
    /// unwrapped a tag pair enclosing the whole response, and a real reply slipped
    /// straight through it: "…over the lazy dog\n&lt;/text&gt; 🙂" — the model trailed an
    /// emoji after the closing tag, so the string no longer ended with it. These are
    /// delimiters this app invented; if the user's text did not contain them, anything
    /// coming back is echoed scaffolding no matter where it sits.
    /// </remarks>
    private static string StripWrapperTags(string text, string? originalText)
    {
        foreach (var tag in WrapperTags)
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";

            if (originalText is not null
                && (originalText.Contains(open, StringComparison.OrdinalIgnoreCase)
                    || originalText.Contains(close, StringComparison.OrdinalIgnoreCase)))
                continue;

            text = text.Replace(open, "", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(close, "", StringComparison.OrdinalIgnoreCase);
        }

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

        // No single-quote pair: an apostrophe is indistinguishable from a closing
        // single quote, so contractions (“can't”) would defeat the guard below on
        // almost every real sentence. Models wrap in double or curly quotes anyway.
        (char open, char close)[] pairs = [('"', '"'), ('“', '”')];
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
