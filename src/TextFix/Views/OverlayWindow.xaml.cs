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
    private int _countdownSeconds;
    private CorrectionResult? _currentResult;
    private bool _showingError;
    private bool _showingApplied;
    private bool _keepOpen;
    private bool _suppressModeChange;
    private bool _showingIdle;
    private bool _historyVisible;
    private CorrectionHistory? _history;

    public event Action<bool>? UserResponded;
    public event Action? RetryRequested;
    public event Action? CopyRequested;
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

    public void SetHistory(CorrectionHistory history) => _history = history;

    public void ShowProcessing()
    {
        _showingError = false;
        _showingApplied = false;
        _showingIdle = false;
        _historyVisible = false;
        StopAutoApply();
        Opacity = 1;
        ProcessingPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
        ActionRowPanel.Visibility = Visibility.Collapsed;

        StartSpinnerAnimation();
        Show();
        Dispatcher.InvokeAsync(PositionNearCursor, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ShowResult(CorrectionResult result, int autoApplySeconds, bool keepOpen = false)
    {
        _currentResult = result;
        _keepOpen = keepOpen;
        _showingIdle = false;
        _historyVisible = false;
        Opacity = 1;
        StopSpinnerAnimation();
        StopAutoApply();
        IdlePanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
        ActionRowPanel.Visibility = Visibility.Collapsed;

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
        _showingIdle = false;
        Opacity = 1;
        StopAutoApply();
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Visible;
        ActionRowPanel.Visibility = Visibility.Visible;
        InfoIcon.Text = "\u2713";
        InfoIcon.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
        InfoText.Text = "Applied!";
        InfoHint.Visibility = Visibility.Collapsed;

        RedoButton.IsEnabled = true;
        CopyButton.IsEnabled = true;

        Activate();
        Focus();
    }

    public void ShowFocusLost()
    {
        _showingError = false;
        _showingApplied = false;
        _showingIdle = false;
        Opacity = 1;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        ActionRowPanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
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
        HideImmediate();
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

    private void HideImmediate()
    {
        Hide();
        Opacity = 1;
        OverlayHidden?.Invoke();
    }

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
                HideImmediate();
                RetryRequested?.Invoke();
            }
            else if (_showingApplied || _showingIdle)
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
            if (_showingError || _showingApplied || _showingIdle)
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

    private Storyboard? _spinnerStoryboard;

    private void StartSpinnerAnimation()
    {
        if (_spinnerStoryboard is not null) return;
        var animation = new DoubleAnimation
        {
            From = 0, To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(1.5)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, Spinner);
        Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.Angle"));
        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(animation);
        _spinnerStoryboard.Begin(this);
    }

    private void StopSpinnerAnimation()
    {
        _spinnerStoryboard?.Stop(this);
        _spinnerStoryboard = null;
    }

    public void ShowIdle(CorrectionHistory history, CorrectionResult? lastResult)
    {
        _showingError = false;
        _showingApplied = false;
        _showingIdle = true;
        _history = history;
        StopAutoApply();
        StopSpinnerAnimation();
        Opacity = 1;

        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Visible;
        ActionRowPanel.Visibility = Visibility.Visible;

        IdleStatsText.Text = $"{history.TodayCount} today \u00b7 {history.TotalCount} total";

        RedoButton.IsEnabled = lastResult is not null && !lastResult.IsError;
        CopyButton.IsEnabled = lastResult is not null && !lastResult.IsError;

        Show();
        PositionNearTray();
        Activate();
        Focus();
    }

    private void PositionNearTray()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Dispatcher.InvokeAsync(() =>
        {
            Left = screen.Right - ActualWidth - 16;
            Top = screen.Bottom - ActualHeight - 16;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnActionRedo(object sender, RoutedEventArgs e)
    {
        _historyVisible = false;
        HistoryPanel.Visibility = Visibility.Collapsed;
        HideImmediate();
        RetryRequested?.Invoke();
    }

    private void OnActionCopy(object sender, RoutedEventArgs e)
    {
        CopyRequested?.Invoke();
        ShowCopiedFeedback();
    }

    private void OnActionHistoryToggle(object sender, RoutedEventArgs e)
    {
        _historyVisible = !_historyVisible;
        if (_historyVisible)
        {
            PopulateHistoryPanel();
            HistoryPanel.Visibility = Visibility.Visible;
            HistoryToggleButton.Background = new WpfMedia.SolidColorBrush(
                WpfMedia.Color.FromRgb(0x6C, 0x63, 0xFF));
            HistoryToggleButton.Foreground = new WpfMedia.SolidColorBrush(
                WpfMedia.Colors.White);
        }
        else
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            HistoryToggleButton.Background = new WpfMedia.SolidColorBrush(
                WpfMedia.Color.FromRgb(0x2A, 0x2A, 0x3E));
            HistoryToggleButton.Foreground = new WpfMedia.SolidColorBrush(
                WpfMedia.Color.FromRgb(0xAA, 0xAA, 0xAA));
        }
    }

    private void PopulateHistoryPanel()
    {
        HistoryList.Children.Clear();
        if (_history is null || _history.Items.Count == 0)
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = "No corrections yet.",
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x66, 0x66, 0x66)),
                FontFamily = new WpfMedia.FontFamily("Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
            });
            HistoryStatsText.Text = "";
            return;
        }

        HistoryStatsText.Text = $"{_history.TodayCount} today \u00b7 {_history.TotalCount} total";

        foreach (var item in _history.Items)
        {
            var text = item.CorrectedText.Length > 60
                ? item.CorrectedText[..60] + "\u2026"
                : item.CorrectedText;
            var age = FormatAge(item.Timestamp);
            var modeLine = string.IsNullOrEmpty(item.ModeName)
                ? age
                : $"{item.ModeName} \u00b7 {age}";

            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            var border = new Border
            {
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0, 0, 0, 0)),
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(3),
                Child = panel,
            };

            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                FontFamily = new WpfMedia.FontFamily("Segoe UI"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            panel.Children.Add(new TextBlock
            {
                Text = modeLine,
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x66, 0x66, 0x66)),
                FontFamily = new WpfMedia.FontFamily("Segoe UI"),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
            });

            var capturedText = item.CorrectedText;
            border.MouseEnter += (_, _) => border.Background =
                new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x2A, 0x2A, 0x3E));
            border.MouseLeave += (_, _) => border.Background =
                new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0, 0, 0, 0));
            border.MouseLeftButtonDown += (_, _) =>
            {
                System.Windows.Clipboard.SetText(capturedText);
                ShowCopiedFeedback();
            };

            HistoryList.Children.Add(border);
        }
    }

    private static string FormatAge(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        if (elapsed.TotalSeconds < 60) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} hr ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    private void ShowCopiedFeedback()
    {
        var original = CopyButton.Content;
        CopyButton.Content = "\u2713 Copied!";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyButton.Content = original;
        };
        timer.Start();
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
