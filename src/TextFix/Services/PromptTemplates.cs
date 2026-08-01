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
