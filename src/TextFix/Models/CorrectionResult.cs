namespace TextFix.Models;

public record CorrectionResult
{
    public required string OriginalText { get; init; }
    public required string CorrectedText { get; init; }
    public bool HasChanges => OriginalText != CorrectedText;
    public string? ErrorMessage { get; init; }
    public bool IsError => ErrorMessage is not null;

    public static CorrectionResult Error(string originalText, string message) =>
        new() { OriginalText = originalText, CorrectedText = originalText, ErrorMessage = message };
}
