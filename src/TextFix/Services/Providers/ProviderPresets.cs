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

        // Local: no key, and a long timeout because a cold model can spend 10-20s
        // loading into RAM before producing its first token.
        new(OllamaId, "Ollama (local)", "http://localhost:11434/v1", KeyRequirement.None,
            DefaultModel: "", TimeoutSeconds: 120, TokenParam: "max_tokens",
            IsOpenAiCompatible: true),

        new(OpenAiId, "OpenAI", "https://api.openai.com/v1", KeyRequirement.Required,
            "gpt-4o-mini", TimeoutSeconds: 30, TokenParam: "max_completion_tokens",
            IsOpenAiCompatible: true),

        // Covers LM Studio, llama.cpp, OpenRouter, Groq, vLLM and corporate endpoints.
        new(CustomId, "Custom (OpenAI-compatible)", "", KeyRequirement.Optional,
            DefaultModel: "", TimeoutSeconds: 120, TokenParam: "max_tokens",
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
