using TextFix.Models;

namespace TextFix.Services;

public class CorrectionService
{
    private readonly ClipboardManager _clipboard;
    private readonly FocusTracker _focusTracker;
    private readonly AppSettings _settings;
    private readonly CorrectionHistory _history = new();
    private AiClient _aiClient;
    private CancellationTokenSource? _cts;

    public CorrectionResult? LastResult { get; private set; }
    public CorrectionHistory History => _history;

    public CorrectionService(ClipboardManager clipboard, FocusTracker focusTracker, AiClient aiClient, AppSettings settings)
    {
        _clipboard = clipboard;
        _focusTracker = focusTracker;
        _aiClient = aiClient;
        _settings = settings;
    }

    public void UpdateAiClient(AiClient aiClient) => _aiClient = aiClient;

    public event Action? ProcessingStarted;
    public event Action<CorrectionResult>? CorrectionCompleted;
    public event Action? FocusLost;
    public event Action<string>? ErrorOccurred;

    public async Task TriggerCorrectionAsync()
    {
        Cancel();
        _cts = new CancellationTokenSource();

        _focusTracker.CaptureSourceWindow();
        _clipboard.SetSourceWindow(_focusTracker.SourceWindow);

        var selectedText = await _clipboard.CaptureSelectedTextAsync();
        if (selectedText is null)
        {
            ErrorOccurred?.Invoke("No text selected.");
            return;
        }

        ProcessingStarted?.Invoke();

        var mode = _settings.GetActiveMode();
        var result = await _aiClient.CorrectAsync(selectedText, mode.SystemPrompt, _cts.Token);

        if (_cts.Token.IsCancellationRequested)
            return;

        LastResult = result;
        _history.Add(result);
        CorrectionCompleted?.Invoke(result);
    }

    public async Task ApplyCorrectionAsync(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
        {
            _clipboard.RestoreClipboard();
            return;
        }

        if (_focusTracker.IsSourceWindowStillActive())
        {
            await _clipboard.PasteTextAsync(result.CorrectedText);
            // Small delay before restoring clipboard so paste completes
            await Task.Delay(200);
            _clipboard.RestoreClipboard();
        }
        else
        {
            _clipboard.SetClipboardText(result.CorrectedText);
            FocusLost?.Invoke();
        }
    }

    public void CancelAndRestore()
    {
        Cancel();
        _clipboard.RestoreClipboard();
    }

    private void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
