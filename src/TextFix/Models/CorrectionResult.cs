namespace TextFix.Models;

public record CorrectionResult
{
    public required string OriginalText { get; init; }
    public required string CorrectedText { get; init; }
    public bool HasChanges => OriginalText != CorrectedText;
    public string? ErrorMessage { get; init; }
    public bool IsError => ErrorMessage is not null;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ModeName { get; init; } = "";
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public static CorrectionResult Error(string originalText, string message) =>
        new() { OriginalText = originalText, CorrectedText = originalText, ErrorMessage = message };
}
