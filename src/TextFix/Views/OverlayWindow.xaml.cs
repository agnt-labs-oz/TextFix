using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfMedia = System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool _showingApplied;
    private bool _keepOpen;
    private bool _suppressModeChange;

    public event Action<bool>? UserResponded;
    public event Action? RetryRequested;
    public event Action<bool>? KeepOpenChanged;
    public event Action<string>? ModeChanged;
    public event Action? OverlayHidden;

    public OverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PopulateModes();
    }

    private void PopulateModes()
    {
        _suppressModeChange = true;
        ModeBox.Items.Clear();
        foreach (var mode in CorrectionMode.Defaults)
        {
            ModeBox.Items.Add(new ComboBoxItem { Content = mode.Name, Tag = mode.Name });
        }
        _suppressModeChange = false;
    }

    public void SetActiveMode(string modeName)
    {
        _suppressModeChange = true;
        for (int i = 0; i < ModeBox.Items.Count; i++)
        {
            if (ModeBox.Items[i] is ComboBoxItem item && (string)item.Tag == modeName)
            {
                ModeBox.SelectedIndex = i;
                break;
            }
        }
        _suppressModeChange = false;
    }

    public void ShowProcessing()
    {
        _showingError = false;
        _showingApplied = false;
        StopAutoApply();
        Opacity = 1;
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
        Opacity = 1;
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

            FadeAndClose(2);
            return;
        }

        _showingError = false;
        _showingApplied = false;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;

        UpdatePinIcon();

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
        Opacity = 1;
        StopAutoApply();
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Visible;
        InfoIcon.Text = "\u2713";
        InfoIcon.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
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
        Opacity = 1;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Visible;
        InfoText.Text = "Focus changed \u2014 Ctrl+V to paste";
        InfoHint.Visibility = Visibility.Collapsed;

        FadeAndClose(_keepOpen ? 5 : 3);
    }

    private void UpdatePinIcon()
    {
        PinToggle.Text = _keepOpen ? "Pinned" : "Pin open";
        PinToggle.Foreground = _keepOpen
            ? new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x6C, 0x63, 0xFF))
            : new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x66, 0x66, 0x66));
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnPinToggle(object sender, MouseButtonEventArgs e)
    {
        _keepOpen = !_keepOpen;
        UpdatePinIcon();
        KeepOpenChanged?.Invoke(_keepOpen);
    }

    // --- Clickable button handlers ---

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        StopAutoApply();
        if (!_showingError && !_showingApplied)
            UserResponded?.Invoke(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        StopAutoApply();
        UserResponded?.Invoke(false);
        FadeOutAndHide();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        StopAutoApply();
        Hide();
        RetryRequested?.Invoke();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        StopAutoApply();
        FadeOutAndHide();
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModeChange) return;
        if (ModeBox.SelectedItem is ComboBoxItem item)
            ModeChanged?.Invoke((string)item.Tag);
    }

    // --- End button handlers ---

    public void FadeOutAndHide()
    {
        var fadeOut = (Storyboard)FindResource("FadeOut");
        var clone = fadeOut.Clone();
        clone.Completed += (_, _) =>
        {
            Hide();
            Opacity = 1;
            OverlayHidden?.Invoke();
        };
        clone.Begin(this);
    }

    private void FadeAndClose(int delaySeconds)
    {
        _autoApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySeconds) };
        _autoApplyTimer.Tick += (_, _) =>
        {
            StopAutoApply();
            FadeOutAndHide();
        };
        _autoApplyTimer.Start();
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
                FadeOutAndHide();
            }
            else
            {
                UserResponded?.Invoke(true);
            }
        }
        else if (e.Key == Key.Escape)
        {
            StopAutoApply();
            if (_showingError || _showingApplied)
            {
                FadeOutAndHide();
            }
            else
            {
                UserResponded?.Invoke(false);
                FadeOutAndHide();
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
            }
            else
            {
                CountdownText.Text = $"Auto-applying in {_countdownSeconds}s...";
            }
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
            if (Spinner.RenderTransform is WpfMedia.RotateTransform rt)
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
