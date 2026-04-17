# Overlay Action Row, Inline History & Tray Left-Click Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add quick-access controls (redo, copy, history, close) to the overlay, an inline history panel with session stats, and tray left-click to open the overlay in idle mode.

**Architecture:** Add `Timestamp` and `ModeName` to `CorrectionResult`, add `TotalCount`/`TodayCount` to `CorrectionHistory`. Extend the overlay XAML with two new panels (action row + history list) that appear in "applied" and "idle" states. Wire tray `MouseClick` to toggle the overlay.

**Tech Stack:** .NET 10, WPF, C#, xUnit

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/TextFix/Models/CorrectionResult.cs` | Modify | Add `Timestamp` and `ModeName` properties |
| `src/TextFix/Models/CorrectionHistory.cs` | Modify | Add `TotalCount`, `TodayCount` |
| `src/TextFix/Services/CorrectionService.cs` | Modify | Pass mode name when creating results |
| `src/TextFix/Views/OverlayWindow.xaml` | Modify | Add ActionRowPanel, HistoryPanel, IdlePanel XAML |
| `src/TextFix/Views/OverlayWindow.xaml.cs` | Modify | Add ShowIdle(), history toggle, copy/redo/close handlers |
| `src/TextFix/App.xaml.cs` | Modify | Add tray left-click handler, wire CopyRequested event |
| `tests/TextFix.Tests/Models/CorrectionResultTests.cs` | Modify | Add Timestamp/ModeName tests |
| `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs` | Modify | Add TotalCount/TodayCount tests |

---

### Task 1: Add Timestamp and ModeName to CorrectionResult

**Files:**
- Modify: `src/TextFix/Models/CorrectionResult.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionResultTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `tests/TextFix.Tests/Models/CorrectionResultTests.cs`:

```csharp
[Fact]
public void Timestamp_DefaultsToUtcNow()
{
    var before = DateTime.UtcNow;
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
    };
    var after = DateTime.UtcNow;

    Assert.InRange(result.Timestamp, before, after);
}

[Fact]
public void ModeName_DefaultsToEmpty()
{
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
    };
    Assert.Equal("", result.ModeName);
}

[Fact]
public void ModeName_CanBeSet()
{
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
        ModeName = "Professional",
    };
    Assert.Equal("Professional", result.ModeName);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "CorrectionResultTests"`
Expected: FAIL — `Timestamp` and `ModeName` do not exist

- [ ] **Step 3: Add properties to CorrectionResult**

Replace the full content of `src/TextFix/Models/CorrectionResult.cs`:

```csharp
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

    public static CorrectionResult Error(string originalText, string message) =>
        new() { OriginalText = originalText, CorrectedText = originalText, ErrorMessage = message };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "CorrectionResultTests"`
Expected: PASS (8 tests)

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/CorrectionResult.cs tests/TextFix.Tests/Models/CorrectionResultTests.cs
git commit -m "feat: add Timestamp and ModeName to CorrectionResult"
```

---

### Task 2: Add TotalCount and TodayCount to CorrectionHistory

**Files:**
- Modify: `src/TextFix/Models/CorrectionHistory.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`:

```csharp
[Fact]
public void TotalCount_IncrementsOnAdd()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 3; i++)
        history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

    Assert.Equal(3, history.TotalCount);
}

[Fact]
public void TotalCount_CountsEvictedItems()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 15; i++)
        history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

    Assert.Equal(10, history.Items.Count);
    Assert.Equal(15, history.TotalCount);
}

[Fact]
public void TotalCount_SkipsErrorsAndNoChanges()
{
    var history = new CorrectionHistory();
    history.Add(CorrectionResult.Error("x", "err"));
    history.Add(new CorrectionResult { OriginalText = "same", CorrectedText = "same" });
    history.Add(new CorrectionResult { OriginalText = "a", CorrectedText = "b" });

    Assert.Equal(1, history.TotalCount);
}

[Fact]
public void TodayCount_CountsOnlyTodaysCorrections()
{
    var history = new CorrectionHistory();
    // Add a result with today's timestamp (default)
    history.Add(new CorrectionResult { OriginalText = "a", CorrectedText = "b" });
    // Add a result with yesterday's timestamp
    history.Add(new CorrectionResult
    {
        OriginalText = "c",
        CorrectedText = "d",
        Timestamp = DateTime.UtcNow.AddDays(-1),
    });

    Assert.Equal(1, history.TodayCount);
    Assert.Equal(2, history.TotalCount);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: FAIL — `TotalCount` and `TodayCount` do not exist

- [ ] **Step 3: Implement TotalCount and TodayCount**

Replace the full content of `src/TextFix/Models/CorrectionHistory.cs`:

```csharp
namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private const int MaxItems = 10;

    public IReadOnlyList<CorrectionResult> Items => _items;
    public int TotalCount { get; private set; }

    public int TodayCount
    {
        get
        {
            var todayUtc = DateTime.UtcNow.Date;
            int count = 0;
            foreach (var item in _items)
            {
                if (item.Timestamp.Date == todayUtc)
                    count++;
            }
            return count;
        }
    }

    public void Add(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
            return;

        TotalCount++;
        _items.Insert(0, result);

        if (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: PASS (9 tests)

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/CorrectionHistory.cs tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
git commit -m "feat: add TotalCount and TodayCount to CorrectionHistory"
```

---

### Task 3: Pass ModeName through CorrectionService

**Files:**
- Modify: `src/TextFix/Services/CorrectionService.cs`

- [ ] **Step 1: Update TriggerCorrectionAsync to set ModeName on the result**

In `src/TextFix/Services/CorrectionService.cs`, the `TriggerCorrectionAsync` method currently calls `_aiClient.CorrectAsync(selectedText, mode.SystemPrompt, _cts.Token)`. The returned `CorrectionResult` has `ModeName = ""` by default. We need to set the mode name on it.

After the line `var result = await _aiClient.CorrectAsync(...)`, add a `with` expression to set ModeName:

Replace the block:
```csharp
var mode = _settings.GetActiveMode();
var result = await _aiClient.CorrectAsync(selectedText, mode.SystemPrompt, _cts.Token);
```

With:
```csharp
var mode = _settings.GetActiveMode();
var result = await _aiClient.CorrectAsync(selectedText, mode.SystemPrompt, _cts.Token);
result = result with { ModeName = mode.Name };
```

- [ ] **Step 2: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: All 47 tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/CorrectionService.cs
git commit -m "feat: set ModeName on CorrectionResult in pipeline"
```

---

### Task 4: Add Action Row and History Panel XAML

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml`

- [ ] **Step 1: Add a SmallActionButton style to Window.Resources**

Add this style inside `<Window.Resources>`, right after the existing `ActionButton` style (after line 50):

```xml
<Style x:Key="SmallActionButton" TargetType="Button">
    <Setter Property="Padding" Value="6,3"/>
    <Setter Property="Margin" Value="0,0,4,0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FontFamily" Value="Segoe UI"/>
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border x:Name="Bd" Background="{TemplateBinding Background}"
                        CornerRadius="4" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Opacity" Value="0.85"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="Bd" Property="Opacity" Value="0.7"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="Bd" Property="Opacity" Value="0.4"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 2: Add the IdlePanel, HistoryPanel, and ActionRowPanel XAML**

Inside the main `<StackPanel>` (child of `MainBorder`), add these three panels after the `InfoPanel` (after line 246, before `</StackPanel></Border>`):

```xml
<!-- Idle state: opened from tray, no active correction -->
<StackPanel x:Name="IdlePanel" Visibility="Collapsed">
    <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
        <TextBlock Text="Tx" Foreground="#6C63FF"
                   FontSize="16" FontWeight="Bold" FontFamily="Segoe UI"
                   Margin="0,0,8,0" VerticalAlignment="Center"/>
        <TextBlock Text="TextFix" Foreground="#E0E0E0"
                   FontFamily="Segoe UI" FontSize="14" FontWeight="SemiBold"
                   VerticalAlignment="Center"/>
    </StackPanel>
    <TextBlock x:Name="IdleStatsText" Foreground="#666666"
               FontFamily="Segoe UI" FontSize="11" Margin="0,0,0,4"/>
</StackPanel>

<!-- History panel: toggled by History button in action row -->
<StackPanel x:Name="HistoryPanel" Visibility="Collapsed" Margin="0,8,0,0">
    <DockPanel Margin="0,0,0,6">
        <TextBlock x:Name="HistoryStatsText" Foreground="#666"
                   FontFamily="Segoe UI" FontSize="11"
                   DockPanel.Dock="Right" VerticalAlignment="Center"/>
        <TextBlock Text="Recent corrections" Foreground="#E0E0E0"
                   FontFamily="Segoe UI" FontSize="12" VerticalAlignment="Center"/>
    </DockPanel>
    <ScrollViewer MaxHeight="160" VerticalScrollBarVisibility="Auto">
        <StackPanel x:Name="HistoryList"/>
    </ScrollViewer>
</StackPanel>

<!-- Action row: redo, copy, history, close -->
<StackPanel x:Name="ActionRowPanel" Orientation="Horizontal"
            Visibility="Collapsed" Margin="0,8,0,0">
    <Border BorderThickness="0,1,0,0" BorderBrush="#333" Padding="0,8,0,0">
        <StackPanel Orientation="Horizontal">
            <Button x:Name="RedoButton" Content="&#x21BB; Redo"
                    Background="#2A2A3E" Foreground="#AAAAAA"
                    Style="{StaticResource SmallActionButton}" Click="OnActionRedo"/>
            <Button x:Name="CopyButton" Content="&#x2398; Copy"
                    Background="#2A2A3E" Foreground="#AAAAAA"
                    Style="{StaticResource SmallActionButton}" Click="OnActionCopy"/>
            <Button x:Name="HistoryToggleButton" Content="&#x29D6; History"
                    Background="#2A2A3E" Foreground="#AAAAAA"
                    Style="{StaticResource SmallActionButton}" Click="OnActionHistoryToggle"/>
        </StackPanel>
    </Border>
</StackPanel>
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build`
Expected: Build succeeds (new handlers `OnActionRedo`, `OnActionCopy`, `OnActionHistoryToggle` don't exist yet — this will fail). We'll add them in the next task.

Actually, the build WILL fail because Click handlers are referenced but don't exist. That's expected — we add them in Task 5. Skip the build step here.

- [ ] **Step 4: Commit (XAML only, will not build yet)**

```bash
git add src/TextFix/Views/OverlayWindow.xaml
git commit -m "feat: add action row, history panel, and idle panel XAML"
```

---

### Task 5: Implement Overlay Code-Behind for New Panels

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Add new fields, events, and state flag**

Add to the field declarations at the top of `OverlayWindow` class (after `_suppressModeChange`):

```csharp
private bool _showingIdle;
private bool _historyVisible;
private CorrectionHistory? _history;
```

Add a new event alongside the existing events:

```csharp
public event Action? CopyRequested;
```

- [ ] **Step 2: Add SetHistory method**

Add this method to supply the history reference from App.xaml.cs:

```csharp
public void SetHistory(CorrectionHistory history) => _history = history;
```

- [ ] **Step 3: Add ShowIdle method**

```csharp
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
```

- [ ] **Step 4: Add PositionNearTray method**

```csharp
private void PositionNearTray()
{
    var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
        ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
    // Position in bottom-right, offset from the edge
    Dispatcher.InvokeAsync(() =>
    {
        Left = screen.Right - ActualWidth - 16;
        Top = screen.Bottom - ActualHeight - 16;
    }, System.Windows.Threading.DispatcherPriority.Loaded);
}
```

- [ ] **Step 5: Modify ShowApplied to show the action row**

Replace the `ShowApplied` method:

```csharp
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
```

- [ ] **Step 6: Add action row button handlers**

```csharp
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
    // Temporarily change the copy button text to "Copied!"
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
```

- [ ] **Step 7: Update state resets in existing Show methods**

In `ShowProcessing()`, after `_showingApplied = false;` add:
```csharp
_showingIdle = false;
_historyVisible = false;
HistoryPanel.Visibility = Visibility.Collapsed;
IdlePanel.Visibility = Visibility.Collapsed;
ActionRowPanel.Visibility = Visibility.Collapsed;
```

In `ShowResult()`, after `StopAutoApply();` add:
```csharp
_showingIdle = false;
_historyVisible = false;
HistoryPanel.Visibility = Visibility.Collapsed;
IdlePanel.Visibility = Visibility.Collapsed;
ActionRowPanel.Visibility = Visibility.Collapsed;
```

In `ShowFocusLost()`, after `_showingApplied = false;` add:
```csharp
_showingIdle = false;
ActionRowPanel.Visibility = Visibility.Collapsed;
HistoryPanel.Visibility = Visibility.Collapsed;
IdlePanel.Visibility = Visibility.Collapsed;
```

- [ ] **Step 8: Update OnKeyDown for idle state**

In `OnKeyDown`, update the Escape handler to also handle idle state. Change:
```csharp
if (_showingError || _showingApplied)
```
To:
```csharp
if (_showingError || _showingApplied || _showingIdle)
```

Update Enter handler — add idle state to the applied branch:
```csharp
else if (_showingApplied || _showingIdle)
{
    FadeOutAndHide();
}
```

- [ ] **Step 9: Build to verify everything compiles**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 10: Commit**

```bash
git add src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "feat: implement overlay action row, history panel, and idle state"
```

---

### Task 6: Wire Tray Left-Click and CopyRequested in App.xaml.cs

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Add tray left-click handler in SetupTrayIcon**

In `SetupTrayIcon()`, after the `_trayIcon` initialization block (after line 98, before `// Mode submenu`), add:

```csharp
_trayIcon.MouseClick += OnTrayClick;
```

- [ ] **Step 2: Implement OnTrayClick**

Add this method to the App class:

```csharp
private void OnTrayClick(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;

    // Toggle: if overlay is visible, hide it; otherwise show idle
    if (_overlay?.IsVisible == true)
    {
        _overlay.FadeOutAndHide();
    }
    else
    {
        _overlay?.ShowIdle(
            _correctionService?.History ?? new CorrectionHistory(),
            _correctionService?.LastResult);
    }
}
```

- [ ] **Step 3: Wire CopyRequested event in SetupOverlay**

In `SetupOverlay()`, after the existing event subscriptions, add:

```csharp
_overlay.CopyRequested += OnCopyRequested;
```

- [ ] **Step 4: Implement OnCopyRequested**

Add this method:

```csharp
private void OnCopyRequested()
{
    CopyLastCorrection();
}
```

- [ ] **Step 5: Pass history to overlay in ShowApplied path**

In `OnUserResponded`, update the ShowApplied call to also set the history. Change:

```csharp
if (_settings.KeepOverlayOpen)
    _overlay?.ShowApplied();
```

To:

```csharp
if (_settings.KeepOverlayOpen)
{
    if (_correctionService is not null)
        _overlay?.SetHistory(_correctionService.History);
    _overlay?.ShowApplied();
}
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/TextFix/App.xaml.cs
git commit -m "feat: wire tray left-click to toggle overlay idle mode"
```

---

### Task 7: Build, Test, and Final Commit

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass (38 existing + 7 new = 45 tests).

- [ ] **Step 2: Build Release**

Run: `dotnet build -c Release`
Expected: Build succeeds.

- [ ] **Step 3: Manual smoke test**

Run: `taskkill /IM TextFix.exe /F 2>/dev/null; dotnet run --project src/TextFix/TextFix.csproj`

Verify:
1. Left-click tray icon → overlay appears near tray showing "TextFix" title, stats line, action row
2. Click "History" → toggles history panel (empty if no corrections yet)
3. Press Esc → overlay closes
4. Left-click tray again → overlay reappears (toggle works)
5. Use hotkey to correct text → normal flow works unchanged
6. After applying (with keep-open) → "Applied!" state shows action row with Redo/Copy/History/Close
7. Click "Copy" → copies last correction, shows "Copied!" feedback
8. Click "History" → shows corrections with mode name and time
9. Click a history item → copies that text
10. Click "Redo" → hides overlay, re-runs correction
11. Click "Close" → fades out

- [ ] **Step 4: Tag and push release**

```bash
git tag v0.2.5
git push origin master
git push origin v0.2.5
```
