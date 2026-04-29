using System.Collections.Generic;
using System.Linq;
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
    private bool _suppressModeChange;
    private bool _showingIdle;
    private bool _historyVisible;
    private bool _processingInline;
    private CorrectionHistory? _history;

    // Persisted across sessions so user-resized result windows stay that size and place.
    private double _resultPrefWidth = 640;
    private double _resultPrefHeight = 620;
    private double? _savedLeft;
    private double? _savedTop;
    // Set true while SetResultSizing is programmatically resizing the window —
    // suppresses OnWindowSizeChanged captures of the intermediate transition sizes.
    private bool _suppressSizeCapture;

    public event Action<bool>? UserResponded;
    public event Action? RetryRequested;
    public event Action? CopyRequested;
    public event Action<string>? ModeChanged;
    public event Action? OverlayHidden;
    public event Action<string>? ReapplyRequested;      // text to reapply
    public event Action<double, double, double, double>? BoundsChanged; // width, height, left, top

    public OverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PopulateModes();
    }

    private void PopulateModes(IReadOnlyList<CorrectionMode>? allModes = null)
    {
        _suppressModeChange = true;
        ModeBox.Items.Clear();
        var modes = allModes ?? CorrectionMode.Defaults;
        foreach (var mode in modes)
        {
            ModeBox.Items.Add(new ComboBoxItem { Content = mode.Name, Tag = mode.Name });
        }
        _suppressModeChange = false;
    }

    public void RefreshModes(IReadOnlyList<CorrectionMode> allModes, string activeName)
    {
        PopulateModes(allModes);
        SetActiveMode(activeName);
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

    public void ShowProcessing(string modeLabel = "")
    {
        var freshLabel = string.IsNullOrEmpty(modeLabel) ? "Correcting..." : $"Correcting with {modeLabel}...";
        var inlineLabel = string.IsNullOrEmpty(modeLabel) ? "Refining..." : $"Refining with {modeLabel}...";

        // Inline mode: dialog is already visible with a result — keep it, show spinner in header
        if (IsVisible && ResultPanel.Visibility == Visibility.Visible)
        {
            _processingInline = true;
            _showingError = false;
            _showingApplied = false;
            StopAutoApply();
            Opacity = 1;

            InlineSpinner.Visibility = Visibility.Visible;
            StatusIcon.Visibility = Visibility.Collapsed;
            StatusText.Text = inlineLabel;
            StatusText.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0));
            CountdownText.Visibility = Visibility.Collapsed;

            SetActionsEnabled(false);
            StartInlineSpinner();
            return;
        }

        // Fresh path: standalone small pill
        _processingInline = false;
        _showingError = false;
        _showingApplied = false;
        _showingIdle = false;
        _historyVisible = false;
        StopAutoApply();
        StopInlineSpinner();
        Opacity = 1;

        ProcessingText.Text = freshLabel;
        ProcessingPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
        ActionRowPanel.Visibility = Visibility.Collapsed;
        SetPillSizing();

        StartSpinnerAnimation();
        Show();
        Dispatcher.InvokeAsync(PositionNearCursor, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyEditableState(bool editable)
    {
        CorrectedText.IsReadOnly = !editable;
        if (editable)
        {
            CorrectedText.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x23, 0x23, 0x36));
            CorrectedText.BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x55, 0x55, 0x55));
            CorrectedText.BorderThickness = new Thickness(1);
            CorrectedText.Padding = new Thickness(6, 4, 6, 4);
        }
        else
        {
            CorrectedText.Background = WpfMedia.Brushes.Transparent;
            CorrectedText.BorderBrush = WpfMedia.Brushes.Transparent;
            CorrectedText.BorderThickness = new Thickness(0);
            CorrectedText.Padding = new Thickness(0);
        }
    }

    public string GetEditedText() => CorrectedText.Text;

    private TextFix.Services.DiffResult RenderDiff(string original, string corrected)
    {
        // Corrected tab is always plain editable text — the apply/copy source.
        CorrectedText.Text = corrected;

        // Diff tab is display-only: always render the inline word diff. Newlines
        // in the segments flow through as line breaks in the FlowDocument, so
        // multi-line corrections render the same way as single-line ones — just
        // with the original line breaks preserved. Normalize CRLF first so a
        // Windows-clipboard \r doesn't appear as a visible control glyph or
        // cause every line to mismatch a \n-only AI output.
        var diff = TextFix.Services.DiffEngine.Compute(
            (original ?? "").Replace("\r\n", "\n").Replace("\r", "\n"),
            (corrected ?? "").Replace("\r\n", "\n").Replace("\r", "\n"));
        RenderInlineWordDiff(diff);
        return diff;
    }

    private static readonly WpfMedia.Brush EqualBrush =
        new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly WpfMedia.Brush RemovedBrush =
        new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly WpfMedia.Brush AddedBrush =
        new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly WpfMedia.Brush RemovedBg =
        new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x1F, 0xF8, 0x71, 0x71));
    private static readonly WpfMedia.Brush AddedBg =
        new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x1F, 0x4A, 0xDE, 0x80));

    private void RenderInlineWordDiff(TextFix.Services.DiffResult diff)
    {
        var doc = new System.Windows.Documents.FlowDocument();
        var para = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };

        foreach (var seg in diff.Segments)
        {
            var run = new System.Windows.Documents.Run(seg.Text);
            switch (seg.Kind)
            {
                case TextFix.Services.DiffKind.Equal:
                    run.Foreground = EqualBrush;
                    break;
                case TextFix.Services.DiffKind.Removed:
                    run.Foreground = RemovedBrush;
                    run.Background = RemovedBg;
                    run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                    break;
                case TextFix.Services.DiffKind.Added:
                    run.Foreground = AddedBrush;
                    run.Background = AddedBg;
                    break;
            }
            para.Inlines.Add(run);
        }
        doc.Blocks.Add(para);
        DiffText.Document = doc;
    }

    // CorrectedText is a plain TextBox now — its .Text is what callers want.
    // The trailing "\r\n" guard from the prior RichTextBox era is no longer needed.

    private void SetActionsEnabled(bool enabled)
    {
        ApplyButton.IsEnabled = enabled;
        CancelButton.IsEnabled = enabled;
        RedoOrigButton.IsEnabled = enabled;
        RedoRefineButton.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled;
        HistoryToggleButton.IsEnabled = enabled;
        ModeBox.IsEnabled = enabled;
    }

    public void ShowResult(CorrectionResult result, int autoApplySeconds, bool editable = false)
    {
        // Inline error: preserve existing dialog, show error in status strip, re-enable actions
        if (_processingInline && result.IsError)
        {
            _processingInline = false;
            _showingError = true;
            _showingApplied = false;
            StopInlineSpinner();
            StopAutoApply();
            Opacity = 1;

            StatusIcon.Visibility = Visibility.Visible;
            StatusIcon.Text = "⚠";
            StatusIcon.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xF8, 0x71, 0x71));
            StatusText.Text = result.ErrorMessage;
            StatusText.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xF8, 0x71, 0x71));

            SetActionsEnabled(true);
            ApplyButton.IsEnabled = _currentResult is not null && !_currentResult.IsError;
            CancelButton.IsEnabled = _currentResult is not null && !_currentResult.IsError;

            // Ensure user always has a way to retry/copy/dismiss after inline error
            if (_currentResult is not null)
                ActionRowPanel.Visibility = Visibility.Visible;

            Activate();
            Focus();
            return;
        }

        _processingInline = false;
        _currentResult = result;
        _showingIdle = false;
        _historyVisible = false;
        Opacity = 1;
        StopSpinnerAnimation();
        StopInlineSpinner();
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
            SetPillSizing();

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
            SetPillSizing();

            FadeAndClose(2);
            return;
        }

        _showingError = false;
        _showingApplied = false;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        // Always land on the editable Corrected tab. Without this the user can land on
        // whichever tab was selected on the prior result.
        DiffTabs.SelectedIndex = 0;
        SetResultSizing();

        // Restore Apply/Cancel in case ShowApplied hid them
        ApplyButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        SetActionsEnabled(true);

        // Reset status header (may have been set to red warning by inline error)
        InlineSpinner.Visibility = Visibility.Collapsed;
        StatusIcon.Visibility = Visibility.Visible;
        StatusIcon.Text = "✓";
        StatusIcon.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
        StatusText.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0));

        OriginalText.Text = result.OriginalText;
        var diff = RenderDiff(result.OriginalText, result.CorrectedText);
        // Word-level Removed + Added segment counts — accurate even when words shift.
        var changeCount = Math.Max(1, diff.Stats.RemovedWordCount + diff.Stats.AddedWordCount);
        StatusText.Text = $"Fixed {changeCount} error{(changeCount == 1 ? "" : "s")}";
        ApplyEditableState(editable);

        Activate();
        Focus();

        if (autoApplySeconds > 0)
            StartAutoApplyCountdown(autoApplySeconds);
    }

    public void ShowApplied()
    {
        _processingInline = false;
        _showingError = false;
        _showingApplied = true;
        _showingIdle = false;
        Opacity = 1;
        StopAutoApply();
        StopInlineSpinner();
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;

        // Keep ResultPanel visible — unified dialog with diff, mode selector, action row
        ResultPanel.Visibility = Visibility.Visible;
        ActionRowPanel.Visibility = Visibility.Visible;

        // Update status to "Applied!" but keep the diff text visible
        InlineSpinner.Visibility = Visibility.Collapsed;
        StatusIcon.Visibility = Visibility.Visible;
        StatusIcon.Text = "\u2713";
        StatusIcon.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0x4A, 0xDE, 0x80));
        StatusText.Text = "Applied!";
        StatusText.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0xE0, 0xE0, 0xE0));

        // Hide Apply/Cancel since already applied, keep mode selector and action row
        ApplyButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CountdownText.Visibility = Visibility.Collapsed;

        RedoOrigButton.IsEnabled = true;
        RedoRefineButton.IsEnabled = true;
        CopyButton.IsEnabled = true;

        Activate();
        Focus();
    }

    public void ShowFocusLost()
    {
        _showingError = false;
        _showingApplied = false;
        _showingIdle = false;
        _processingInline = false;
        StopInlineSpinner();
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
        SetPillSizing();

        FadeAndClose(3);
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var beforeLeft = Left;
        var beforeTop = Top;
        DragMove();
        if (Left != beforeLeft || Top != beforeTop)
        {
            _savedLeft = Left;
            _savedTop = Top;
            BoundsChanged?.Invoke(ActualWidth, ActualHeight, Left, Top);
        }
    }

    public void LoadSavedBounds(double? width, double? height, double? left, double? top)
    {
        // Floor on restore — saved values from earlier buggy capture (intermediate transition sizes)
        // could land below a usable height, so always come up tall enough to be useful.
        if (width is > 0) _resultPrefWidth = Math.Max(480, width.Value);
        if (height is > 0) _resultPrefHeight = Math.Max(480, height.Value);
        if (left.HasValue && top.HasValue && !double.IsNaN(left.Value) && !double.IsNaN(top.Value))
        {
            _savedLeft = left;
            _savedTop = top;
        }
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
        {
            ModeChanged?.Invoke((string)item.Tag);
            // If in result or applied view, trigger re-run from original text
            if (!_showingError && !_showingIdle && _currentResult is not null)
            {
                ReapplyRequested?.Invoke(_currentResult.OriginalText);
            }
        }
    }

    // --- End button handlers ---

    private void HideImmediate()
    {
        Hide();
        BeginAnimation(OpacityProperty, null);
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
            // Release the animation hold so Opacity can be set normally again
            BeginAnimation(OpacityProperty, null);
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
        if (_savedLeft.HasValue && _savedTop.HasValue)
        {
            Left = _savedLeft.Value;
            Top = _savedTop.Value;
            ClampToScreen();
            return;
        }

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

    // Auto-fit the window to the small pill states (processing, error, idle, info).
    private void SetPillSizing()
    {
        ResizeGrip.Visibility = Visibility.Collapsed;
        if (SizeToContent != SizeToContent.WidthAndHeight)
        {
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            SizeToContent = SizeToContent.WidthAndHeight;
        }
    }

    // Switch to manual sizing for the result/diff view so it can hold long text + scroll, and resize.
    private void SetResultSizing()
    {
        _suppressSizeCapture = true;
        if (SizeToContent != SizeToContent.Manual)
            SizeToContent = SizeToContent.Manual;
        Width = _resultPrefWidth;
        Height = _resultPrefHeight;
        ResizeGrip.Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() =>
        {
            if (_savedLeft.HasValue && _savedTop.HasValue)
            {
                Left = _savedLeft.Value;
                Top = _savedTop.Value;
            }
            ClampToScreen();
            // Release suppression after layout settles so genuine user-driven resizes are captured.
            _suppressSizeCapture = false;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ClampToScreen()
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var screen = helper.Handle != IntPtr.Zero
            ? System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea
            : System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
              ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        if (Left + ActualWidth > screen.Right)
            Left = Math.Max(screen.Left, screen.Right - ActualWidth - 10);
        if (Top + ActualHeight > screen.Bottom)
            Top = Math.Max(screen.Top, screen.Bottom - ActualHeight - 10);
        if (Left < screen.Left) Left = screen.Left;
        if (Top < screen.Top) Top = screen.Top;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Skip programmatic resizes — those carry transition sizes from the pill→result switch.
        if (_suppressSizeCapture) return;

        // Persist user-driven resizes only while the result panel is the active manual-sized view.
        if (SizeToContent == SizeToContent.Manual
            && ResultPanel.Visibility == Visibility.Visible)
        {
            _resultPrefWidth = e.NewSize.Width;
            _resultPrefHeight = e.NewSize.Height;
            BoundsChanged?.Invoke(e.NewSize.Width, e.NewSize.Height,
                _savedLeft ?? Left, _savedTop ?? Top);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_processingInline)
        {
            e.Handled = true;
            return;
        }
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

    private Storyboard? _inlineSpinnerStoryboard;

    private void StartInlineSpinner()
    {
        if (_inlineSpinnerStoryboard is not null) return;
        var animation = new DoubleAnimation
        {
            From = 0, To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(1.5)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, InlineSpinner);
        Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.Angle"));
        _inlineSpinnerStoryboard = new Storyboard();
        _inlineSpinnerStoryboard.Children.Add(animation);
        _inlineSpinnerStoryboard.Begin(this);
    }

    private void StopInlineSpinner()
    {
        _inlineSpinnerStoryboard?.Stop(this);
        _inlineSpinnerStoryboard = null;
        InlineSpinner.Visibility = Visibility.Collapsed;
    }

    public void ShowIdle(CorrectionHistory history, CorrectionResult? lastResult)
    {
        _showingError = false;
        _showingApplied = false;
        _showingIdle = true;
        _history = history;
        StopAutoApply();
        StopSpinnerAnimation();
        StopInlineSpinner();
        Opacity = 1;

        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Visible;
        ActionRowPanel.Visibility = Visibility.Visible;
        SetPillSizing();

        var costStr = history.SessionCost > 0 ? $" \u00b7 ${history.SessionCost:F4} session" : "";
        IdleStatsText.Text = $"{history.TodayCount} today \u00b7 {history.TotalCount} total{costStr}";

        var hasResult = lastResult is not null && !lastResult.IsError;
        RedoOrigButton.IsEnabled = hasResult;
        RedoRefineButton.IsEnabled = hasResult;
        CopyButton.IsEnabled = hasResult;

        Show();
        PositionNearTray();
        Activate();
        Focus();
    }

    private void PositionNearTray()
    {
        if (_savedLeft.HasValue && _savedTop.HasValue)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Left = _savedLeft.Value;
                Top = _savedTop.Value;
                ClampToScreen();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Dispatcher.InvokeAsync(() =>
        {
            Left = screen.Right - ActualWidth - 16;
            Top = screen.Bottom - ActualHeight - 16;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnActionRedoOriginal(object sender, RoutedEventArgs e)
    {
        _historyVisible = false;
        HistoryPanel.Visibility = Visibility.Collapsed;
        if (_currentResult is not null)
            ReapplyRequested?.Invoke(_currentResult.OriginalText);
    }

    private void OnActionRedoRefine(object sender, RoutedEventArgs e)
    {
        _historyVisible = false;
        HistoryPanel.Visibility = Visibility.Collapsed;
        // Use current (possibly edited) text from the editor
        var text = CorrectedText.Text;
        if (!string.IsNullOrEmpty(text))
            ReapplyRequested?.Invoke(text);
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

        var costStr = _history.SessionCost > 0 ? $" \u00b7 ${_history.SessionCost:F4} session" : "";
        HistoryStatsText.Text = $"{_history.TodayCount} today \u00b7 {_history.TotalCount} total{costStr}";

        foreach (var item in _history.Items)
        {
            var text = item.CorrectedText.Length > 60
                ? item.CorrectedText[..60] + "\u2026"
                : item.CorrectedText;
            var age = FormatAge(item.Timestamp);
            var tokens = item.InputTokens + item.OutputTokens;
            var tokenStr = tokens > 0 ? $" \u00b7 {tokens} tokens" : "";
            var modeLine = string.IsNullOrEmpty(item.ModeName)
                ? $"{age}{tokenStr}"
                : $"{item.ModeName} \u00b7 {age}{tokenStr}";

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
}
