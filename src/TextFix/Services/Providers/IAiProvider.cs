using TextFix.Models;

namespace TextFix.Services.Providers;

public interface IAiProvider
{
    /// <summary>Shown in the overlay and tray switcher, e.g. "Ollama (local)".</summary>
    string DisplayName { get; }

    /// <summary>Preset id, stamped onto results for cost attribution.</summary>
    string ProviderId { get; }

    /// <summary>True when inference happens on this machine, so cost is exactly zero.</summary>
    bool IsLocal { get; }

    /// <summary>
    /// Never throws. Every failure comes back as CorrectionResult.Error.
    /// </summary>
    Task<CorrectionResult> CorrectAsync(string text, string systemPrompt, CancellationToken ct = default);

    /// <summary>
    /// Models available from this provider. Also serves as the connection test.
    /// Throws on connection failure so the caller can report why.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
