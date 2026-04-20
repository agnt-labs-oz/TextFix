// src/TextFix/Models/CorrectionMode.cs
namespace TextFix.Models;

public record CorrectionMode
{
    public required string Name { get; init; }
    public required string SystemPrompt { get; init; }

    public static readonly IReadOnlyList<CorrectionMode> Defaults =
    [
        new()
        {
            Name = "Fix errors",
            SystemPrompt = "Fix all typos, spelling, and grammar errors in the following text. Return only the corrected text with no explanation. Preserve the original meaning, tone, and formatting.",
        },
        new()
        {
            Name = "Professional",
            SystemPrompt = "Rewrite the following text in a professional, polished tone suitable for business communication. Fix any errors. Return only the rewritten text with no explanation.",
        },
        new()
        {
            Name = "Concise",
            SystemPrompt = "Rewrite the following text to be as concise as possible while preserving the meaning. Remove filler words and unnecessary phrases. Fix any errors. Return only the rewritten text with no explanation.",
        },
        new()
        {
            Name = "Friendly",
            SystemPrompt = "Rewrite the following text in a warm, friendly, conversational tone. Fix any errors. Return only the rewritten text with no explanation.",
        },
        new()
        {
            Name = "Expand",
            SystemPrompt = "Expand the following text to be more detailed and descriptive while preserving the original meaning. Fix any errors. Return only the expanded text with no explanation.",
        },
        new()
        {
            Name = "Prompt enhancer",
            SystemPrompt = "You are an expert at writing prompts for AI coding assistants like Claude Code. Take the following rough or lazily written instruction and rewrite it as a clear, specific, well-structured prompt that an AI coder will understand and execute correctly. Clarify intent, add relevant constraints, specify expected output format, and remove ambiguity. Keep the original goal intact. Return only the enhanced prompt with no explanation.",
        },
    ];
}
