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

        // Local: no key, and a long timeout. Two costs stack — a cold model loads from
        // disk before its first token, and generation itself is slow without a GPU.
        //
        // 120s was a guess and it was wrong. Measured on a CPU-only box (32GB, no GPU):
        // llama3.2:3b took 26s for 1,000 characters, which extrapolates to ~130s at the
        // 5,000-character MaxTextLength — i.e. the app accepted an input its own timeout
        // could not finish. gemma4:26b on the same machine timed out on 1,000 characters
        // outright. 300s covers a 3B-7B model at full length on CPU; anything slower is
        // hardware the user needs to know about, not a deadline worth extending further.
        //
        // The overlay shows an elapsed counter and a cancel button throughout, so a
        // generous ceiling costs a waiting user nothing — they can see it and stop it.
        new(OllamaId, "Ollama (local)", "http://localhost:11434/v1", KeyRequirement.None,
            DefaultModel: "", TimeoutSeconds: 300, TokenParam: "max_tokens",
            IsOpenAiCompatible: true),

        new(OpenAiId, "OpenAI", "https://api.openai.com/v1", KeyRequirement.Required,
            "gpt-4o-mini", TimeoutSeconds: 30, TokenParam: "max_completion_tokens",
            IsOpenAiCompatible: true),

        // Covers LM Studio, llama.cpp, OpenRouter, Groq, vLLM and corporate endpoints.
        // Shares Ollama's ceiling: this row is just as likely to point at a local model
        // on CPU as at a fast hosted one, so it is sized for the slower case.
        new(CustomId, "Custom (OpenAI-compatible)", "", KeyRequirement.Optional,
            DefaultModel: "", TimeoutSeconds: 300, TokenParam: "max_tokens",
            IsOpenAiCompatible: true),
    ];

    /// <summary>
    /// Looks up a preset, falling back to Anthropic for an unknown id rather than
    /// throwing — a hand-edited settings file must not prevent startup.
    /// </summary>
    public static ProviderPreset Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(p => p.Id == AnthropicId);
}
