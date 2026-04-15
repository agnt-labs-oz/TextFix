using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextFix.Interop;
using TextFix.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TextFix.Views;

public partial class OverlayWindow : Window
{
    private DispatcherTimer? _autoApplyTimer;
    private DispatcherTimer? _spinnerTimer;
    private int _countdownSeconds;
    private CorrectionResult? _currentResult;
    private bool _showingError;
    private bool _showingApplied; // "Applied" confirmation when keep-open is on
    private bool _keepOpen;

    public event Action<bool>? UserResponded; // true = apply, false = cancel
    public event Action? RetryRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public void ShowProcessing()
    {
        _showingError = false;
        _showingApplied = false;
        StopAutoApply();
        ProcessingPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;

        PositionNearCursor();
        StartSpinnerAnimation();
        Show();
    }

    public void ShowResult(CorrectionResult result, int autoApplySeconds, bool keepOpen = false)
    {
        _currentResult = result;
        _keepOpen = keepOpen;
        StopSpinnerAnimation();
        StopAutoApply();

        if (result.IsError)
        {
            _showingError = true;
            _showingApplied = false;
            ProcessingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = result.ErrorMessage;

            Activate();
            Focus();
            return;
        }

        if (!result.HasChanges)
        {
            _showingError = false;
            _showingApplied = false;
            ProcessingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Visible;
            InfoText.Text = "No corrections needed.";
            InfoHint.Visibility = Visibility.Collapsed;

            StartAutoClose(2);
            return;
        }

        _showingError = false;
        _showingApplied = false;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;

        var changeCount = CountChanges(result.OriginalText, result.CorrectedText);
        StatusText.Text = $"Fixed {changeCount} error{(changeCount == 1 ? "" : "s")}";
        OriginalText.Text = result.OriginalText;
        CorrectedText.Text = result.CorrectedText;

        Activate();
        Focus();

        if (autoApplySeconds > 0)
            StartAutoApplyCountdown(autoApplySeconds);
    }

    public void ShowApplied()
    {
        _showingError = false;
        _showingApplied = true;
        StopAutoApply();
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Visible;
        InfoIcon.Text = "\u2713";
        InfoIcon.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
        InfoText.Text = "Applied! Waiting for next correction...";
        InfoHint.Text = "Press hotkey to correct more, Esc to close";
        InfoHint.Visibility = Visibility.Visible;

        Activate();
        Focus();
    }

    public void ShowFocusLost()
    {
        _showingError = false;
        _showingApplied = false;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Visible;
        InfoText.Text = "Focus changed — Ctrl+V to paste";
        InfoHint.Visibility = Visibility.Collapsed;

        if (_keepOpen)
            StartAutoClose(5);
        else
            StartAutoClose(3);
    }

    private void PositionNearCursor()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            Left = point.X + 10;
            Top = point.Y + 20;

            var cursorPoint = new System.Drawing.Point(point.X, point.Y);
            var screen = System.Windows.Forms.Screen.FromPoint(cursorPoint).WorkingArea;
            if (Left + ActualWidth > screen.Right)
                Left = screen.Right - ActualWidth - 10;
            if (Top + ActualHeight > screen.Bottom)
                Top = point.Y - ActualHeight - 10;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            StopAutoApply();
            if (_showingError)
            {
                Hide();
                RetryRequested?.Invoke();
            }
            else if (_showingApplied)
            {
                // Do nothing on Enter in applied state — user uses hotkey for next correction
            }
            else
            {
                UserResponded?.Invoke(true);
                // Don't hide here — App.xaml.cs will call ShowApplied() or Hide()
            }
        }
        else if (e.Key == Key.Escape)
        {
            StopAutoApply();
            if (_showingError || _showingApplied)
            {
                Hide();
            }
            else
            {
                UserResponded?.Invoke(false);
                Hide();
            }
        }
    }

    private void StartAutoApplyCountdown(int seconds)
    {
        _countdownSeconds = seconds;
        CountdownText.Text = $"Auto-applying in {_countdownSeconds}s...";
        CountdownText.Visibility = Visibility.Visible;

        _autoApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoApplyTimer.Tick += (_, _) =>
        {
            _countdownSeconds--;
            if (_countdownSeconds <= 0)
            {
                StopAutoApply();
                UserResponded?.Invoke(true);
                // Don't hide here — App.xaml.cs decides based on keepOpen
            }
            else
            {
                CountdownText.Text = $"Auto-applying in {_countdownSeconds}s...";
            }
        };
        _autoApplyTimer.Start();
    }

    private void StartAutoClose(int seconds)
    {
        _autoApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autoApplyTimer.Tick += (_, _) =>
        {
            StopAutoApply();
            Hide();
        };
        _autoApplyTimer.Start();
    }

    private void StopAutoApply()
    {
        _autoApplyTimer?.Stop();
        _autoApplyTimer = null;
        CountdownText.Visibility = Visibility.Collapsed;
    }

    private void StartSpinnerAnimation()
    {
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double angle = 0;
        _spinnerTimer.Tick += (_, _) =>
        {
            angle = (angle + 4) % 360;
            if (Spinner.RenderTransform is System.Windows.Media.RotateTransform rt)
                rt.Angle = angle;
        };
        _spinnerTimer.Start();
    }

    private void StopSpinnerAnimation()
    {
        _spinnerTimer?.Stop();
        _spinnerTimer = null;
    }

    private static int CountChanges(string original, string corrected)
    {
        var origWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var corrWords = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int changes = 0;
        int maxLen = Math.Max(origWords.Length, corrWords.Length);
        for (int i = 0; i < maxLen; i++)
        {
            if (i >= origWords.Length || i >= corrWords.Length ||
                !string.Equals(origWords[i], corrWords[i], StringComparison.Ordinal))
                changes++;
        }
        return Math.Max(changes, 1);
    }
}
