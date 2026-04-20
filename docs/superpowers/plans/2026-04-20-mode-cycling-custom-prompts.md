# Mode Cycling, Custom Prompts, Persistent History & Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users iterate on text by re-applying different modes, create/save custom prompts, persist history across sessions, show token/cost stats, and add Claude 4.7 models.

**Architecture:** Add token properties to `CorrectionResult`, persistence + cost tracking to `CorrectionHistory`, custom modes to `AppSettings`. Add `ReapplyAsync` to `CorrectionService` for mode cycling without re-capture. Extend overlay with inline prompt box and split redo buttons. Add custom mode CRUD to Settings.

**Tech Stack:** .NET 10, WPF, C#, xUnit, System.Text.Json

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/TextFix/Models/CorrectionResult.cs` | Modify | Add `InputTokens`, `OutputTokens` properties |
| `src/TextFix/Models/CorrectionHistory.cs` | Modify | MaxItems→50, `SaveAsync`/`LoadAsync`, `SessionCost`, persist TotalCount |
| `src/TextFix/Models/CorrectionMode.cs` | Modify | Add `CostPerInputMToken`/`CostPerOutputMToken` static pricing lookup |
| `src/TextFix/Models/AppSettings.cs` | Modify | Add `CustomModes` list, update `GetActiveMode()`, add `AllModes()` |
| `src/TextFix/Services/AiClient.cs` | Modify | Capture token usage from API response |
| `src/TextFix/Services/CorrectionService.cs` | Modify | Add `ReapplyAsync(string text)` and `ReapplyWithPromptAsync(string text, string prompt)` |
| `src/TextFix/Views/OverlayWindow.xaml` | Modify | Custom prompt TextBox, split Redo, save-mode UI |
| `src/TextFix/Views/OverlayWindow.xaml.cs` | Modify | Prompt submit, save-mode flow, mode-change-triggers-rerun, new events |
| `src/TextFix/Views/SettingsWindow.xaml` | Modify | Custom modes management section |
| `src/TextFix/Views/SettingsWindow.xaml.cs` | Modify | Add 4.7 models, custom mode CRUD, populate modes from AllModes |
| `src/TextFix/App.xaml.cs` | Modify | Wire ReapplyRequested, rebuild mode lists, load/save history |
| `tests/TextFix.Tests/Models/CorrectionResultTests.cs` | Modify | Token property tests |
| `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs` | Modify | Persistence tests, SessionCost, MaxItems=50 |
| `tests/TextFix.Tests/Models/AppSettingsTests.cs` | Modify | CustomModes persistence, GetActiveMode with customs |

---

### Task 1: Add InputTokens and OutputTokens to CorrectionResult

**Files:**
- Modify: `src/TextFix/Models/CorrectionResult.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionResultTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `tests/TextFix.Tests/Models/CorrectionResultTests.cs`:

```csharp
[Fact]
public void InputTokens_DefaultsToZero()
{
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
    };
    Assert.Equal(0, result.InputTokens);
}

[Fact]
public void OutputTokens_DefaultsToZero()
{
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
    };
    Assert.Equal(0, result.OutputTokens);
}

[Fact]
public void Tokens_CanBeSet()
{
    var result = new CorrectionResult
    {
        OriginalText = "hi",
        CorrectedText = "hello",
        InputTokens = 150,
        OutputTokens = 42,
    };
    Assert.Equal(150, result.InputTokens);
    Assert.Equal(42, result.OutputTokens);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "CorrectionResultTests"`
Expected: FAIL — `InputTokens` and `OutputTokens` do not exist

- [ ] **Step 3: Add properties to CorrectionResult**

In `src/TextFix/Models/CorrectionResult.cs`, add after the `ModeName` property:

```csharp
public int InputTokens { get; init; }
public int OutputTokens { get; init; }
```

The full file becomes:

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
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public static CorrectionResult Error(string originalText, string message) =>
        new() { OriginalText = originalText, CorrectedText = originalText, ErrorMessage = message };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "CorrectionResultTests"`
Expected: PASS (11 tests)

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/CorrectionResult.cs tests/TextFix.Tests/Models/CorrectionResultTests.cs
git commit -m "feat: add InputTokens and OutputTokens to CorrectionResult"
```

---

### Task 2: Capture token usage in AiClient

**Files:**
- Modify: `src/TextFix/Services/AiClient.cs`

- [ ] **Step 1: Update CorrectAsync to capture token counts from the API response**

In `src/TextFix/Services/AiClient.cs`, in the `CorrectAsync` method, update the success return block. Replace:

```csharp
return new CorrectionResult
{
    OriginalText = text,
    CorrectedText = corrected,
};
```

With:

```csharp
return new CorrectionResult
{
    OriginalText = text,
    CorrectedText = corrected,
    InputTokens = message.Usage?.InputTokens ?? 0,
    OutputTokens = message.Usage?.OutputTokens ?? 0,
};
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/AiClient.cs
git commit -m "feat: capture token usage from Anthropic API response"
```

---

### Task 3: Add pricing lookup and SessionCost to CorrectionHistory

**Files:**
- Modify: `src/TextFix/Models/CorrectionHistory.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`:

```csharp
[Fact]
public void SessionCost_SumsTokenCosts()
{
    var history = new CorrectionHistory();
    // Use haiku pricing: $0.80/M input, $4.00/M output
    history.Add(new CorrectionResult
    {
        OriginalText = "a",
        CorrectedText = "b",
        InputTokens = 1000,
        OutputTokens = 500,
        ModeName = "Fix errors",
    });

    // 1000 * 0.80/1M + 500 * 4.00/1M = 0.0008 + 0.002 = 0.0028
    // SessionCost uses the default model, not per-result model
    // Just verify it's > 0
    Assert.True(history.SessionCost > 0);
}

[Fact]
public void MaxItems_Is50()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 60; i++)
        history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

    Assert.Equal(50, history.Items.Count);
    Assert.Equal(60, history.TotalCount);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: FAIL — `SessionCost` does not exist, and MaxItems is still 10

- [ ] **Step 3: Implement changes**

Replace the full content of `src/TextFix/Models/CorrectionHistory.cs`:

```csharp
namespace TextFix.Models;

public class CorrectionHistory
{
    private readonly List<CorrectionResult> _items = [];
    private const int MaxItems = 50;

    public IReadOnlyList<CorrectionResult> Items => _items;
    public int TotalCount { get; set; }

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

    /// <summary>
    /// Session cost in USD, computed from token counts and default haiku pricing.
    /// Resets on app restart.
    /// </summary>
    public decimal SessionCost { get; private set; }

    // Default pricing (haiku) — used for session cost estimate
    private const decimal DefaultInputPricePerMToken = 0.80m;
    private const decimal DefaultOutputPricePerMToken = 4.00m;

    public void Add(CorrectionResult result)
    {
        if (result.IsError || !result.HasChanges)
            return;

        TotalCount++;
        SessionCost += (result.InputTokens * DefaultInputPricePerMToken
                      + result.OutputTokens * DefaultOutputPricePerMToken) / 1_000_000m;
        _items.Insert(0, result);

        if (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }
}
```

Note: `TotalCount` setter changed from `private set` to `set` to support persistence (LoadAsync will set it). `SessionCost` is private set — it's computed on Add and resets on restart.

- [ ] **Step 4: Fix the existing test Add_CapsAt10 — it now caps at 50**

In `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`, rename and update the existing test:

Replace:
```csharp
[Fact]
public void Add_CapsAt10()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 15; i++)
    {
        history.Add(new CorrectionResult
        {
            OriginalText = $"orig{i}",
            CorrectedText = $"fixed{i}",
        });
    }

    Assert.Equal(10, history.Items.Count);
    Assert.Equal("fixed14", history.Items[0].CorrectedText);
    Assert.Equal("fixed5", history.Items[9].CorrectedText);
}
```

With:
```csharp
[Fact]
public void Add_CapsAt50()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 55; i++)
    {
        history.Add(new CorrectionResult
        {
            OriginalText = $"orig{i}",
            CorrectedText = $"fixed{i}",
        });
    }

    Assert.Equal(50, history.Items.Count);
    Assert.Equal("fixed54", history.Items[0].CorrectedText);
    Assert.Equal("fixed5", history.Items[49].CorrectedText);
}
```

Also update `TotalCount_CountsEvictedItems` which asserts `Items.Count == 10`:

Replace:
```csharp
[Fact]
public void TotalCount_CountsEvictedItems()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 15; i++)
        history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

    Assert.Equal(10, history.Items.Count);
    Assert.Equal(15, history.TotalCount);
}
```

With:
```csharp
[Fact]
public void TotalCount_CountsEvictedItems()
{
    var history = new CorrectionHistory();
    for (int i = 0; i < 55; i++)
        history.Add(new CorrectionResult { OriginalText = $"a{i}", CorrectedText = $"b{i}" });

    Assert.Equal(50, history.Items.Count);
    Assert.Equal(55, history.TotalCount);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: PASS (11 tests)

- [ ] **Step 6: Commit**

```bash
git add src/TextFix/Models/CorrectionHistory.cs tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
git commit -m "feat: MaxItems=50, SessionCost tracking on CorrectionHistory"
```

---

### Task 4: Add persistent history (SaveAsync/LoadAsync)

**Files:**
- Modify: `src/TextFix/Models/CorrectionHistory.cs`
- Modify: `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `tests/TextFix.Tests/Models/CorrectionHistoryTests.cs`. First add `using System.IO;` and `using System.Text.Json;` at the top, then add these tests:

```csharp
[Fact]
public async Task SaveAndLoad_RoundTripsItems()
{
    var dir = Path.Combine(Path.GetTempPath(), $"TextFixHistTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "history.json");
        var history = new CorrectionHistory();
        history.Add(new CorrectionResult
        {
            OriginalText = "a",
            CorrectedText = "b",
            ModeName = "Fix errors",
            InputTokens = 100,
            OutputTokens = 50,
        });
        history.Add(new CorrectionResult
        {
            OriginalText = "c",
            CorrectedText = "d",
            ModeName = "Professional",
        });

        await history.SaveAsync(path);
        var loaded = await CorrectionHistory.LoadAsync(path);

        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal(2, loaded.TotalCount);
        Assert.Equal("d", loaded.Items[0].CorrectedText);
        Assert.Equal("b", loaded.Items[1].CorrectedText);
        Assert.Equal("Professional", loaded.Items[0].ModeName);
        Assert.Equal(100, loaded.Items[1].InputTokens);
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

[Fact]
public async Task LoadAsync_ReturnsEmpty_WhenFileDoesNotExist()
{
    var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
    var loaded = await CorrectionHistory.LoadAsync(path);

    Assert.Empty(loaded.Items);
    Assert.Equal(0, loaded.TotalCount);
}

[Fact]
public async Task LoadAsync_ReturnsEmpty_WhenFileIsCorrupted()
{
    var dir = Path.Combine(Path.GetTempPath(), $"TextFixHistTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "bad.json");
        await File.WriteAllTextAsync(path, "not valid json {{{");
        var loaded = await CorrectionHistory.LoadAsync(path);

        Assert.Empty(loaded.Items);
        Assert.Equal(0, loaded.TotalCount);
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: FAIL — `SaveAsync` and `LoadAsync` do not exist

- [ ] **Step 3: Implement SaveAsync and LoadAsync**

Add the following usings to the top of `src/TextFix/Models/CorrectionHistory.cs`:

```csharp
using System.IO;
using System.Text.Json;
```

Add a private DTO class and the methods inside `CorrectionHistory`:

```csharp
private class HistoryData
{
    public int TotalCount { get; set; }
    public List<CorrectionResult> Items { get; set; } = [];
}

private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
};

public static string DefaultPath =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TextFix",
        "history.json");

public async Task SaveAsync(string? path = null)
{
    path ??= DefaultPath;
    var dir = Path.GetDirectoryName(path)!;
    if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

    var data = new HistoryData
    {
        TotalCount = TotalCount,
        Items = [.. _items],
    };
    var json = JsonSerializer.Serialize(data, JsonOptions);
    await File.WriteAllTextAsync(path, json);
}

public static async Task<CorrectionHistory> LoadAsync(string? path = null)
{
    path ??= DefaultPath;
    if (!File.Exists(path))
        return new CorrectionHistory();

    try
    {
        var json = await File.ReadAllTextAsync(path);
        var data = JsonSerializer.Deserialize<HistoryData>(json, JsonOptions);
        if (data is null)
            return new CorrectionHistory();

        var history = new CorrectionHistory();
        history.TotalCount = data.TotalCount;
        foreach (var item in data.Items)
            history._items.Add(item);
        return history;
    }
    catch
    {
        return new CorrectionHistory();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "CorrectionHistoryTests"`
Expected: PASS (14 tests)

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/CorrectionHistory.cs tests/TextFix.Tests/Models/CorrectionHistoryTests.cs
git commit -m "feat: persistent correction history with SaveAsync/LoadAsync"
```

---

### Task 5: Add CustomModes to AppSettings

**Files:**
- Modify: `src/TextFix/Models/AppSettings.cs`
- Modify: `tests/TextFix.Tests/Models/AppSettingsTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `tests/TextFix.Tests/Models/AppSettingsTests.cs`:

```csharp
[Fact]
public void CustomModes_DefaultsToEmpty()
{
    var settings = new AppSettings();
    Assert.Empty(settings.CustomModes);
}

[Fact]
public void GetActiveMode_FindsCustomMode()
{
    var settings = new AppSettings
    {
        ActiveModeName = "My Custom",
        CustomModes =
        [
            new CorrectionMode { Name = "My Custom", SystemPrompt = "Do custom stuff" },
        ],
    };
    var mode = settings.GetActiveMode();
    Assert.Equal("My Custom", mode.Name);
    Assert.Equal("Do custom stuff", mode.SystemPrompt);
}

[Fact]
public void GetActiveMode_DefaultsWin_OverCustom_WhenNameMatchesBoth()
{
    // If a custom mode has the same name as a built-in, built-in wins
    var settings = new AppSettings
    {
        ActiveModeName = "Fix errors",
        CustomModes =
        [
            new CorrectionMode { Name = "Fix errors", SystemPrompt = "custom override" },
        ],
    };
    var mode = settings.GetActiveMode();
    Assert.NotEqual("custom override", mode.SystemPrompt);
}

[Fact]
public async Task RoundTrip_PreservesCustomModes()
{
    var original = new AppSettings
    {
        CustomModes =
        [
            new CorrectionMode { Name = "Sarcastic", SystemPrompt = "Make it sarcastic" },
            new CorrectionMode { Name = "Pirate", SystemPrompt = "Talk like a pirate" },
        ],
    };
    var path = Path.Combine(_tempDir, "custom_modes.json");

    await original.SaveAsync(path);
    var loaded = await AppSettings.LoadAsync(path);

    Assert.Equal(2, loaded.CustomModes.Count);
    Assert.Equal("Sarcastic", loaded.CustomModes[0].Name);
    Assert.Equal("Talk like a pirate", loaded.CustomModes[1].SystemPrompt);
}

[Fact]
public void AllModes_ReturnsBothDefaultsAndCustom()
{
    var settings = new AppSettings
    {
        CustomModes =
        [
            new CorrectionMode { Name = "Custom1", SystemPrompt = "test" },
        ],
    };
    var all = settings.AllModes();
    Assert.Equal(CorrectionMode.Defaults.Count + 1, all.Count);
    Assert.Equal("Custom1", all[^1].Name);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AppSettingsTests"`
Expected: FAIL — `CustomModes` and `AllModes()` do not exist

- [ ] **Step 3: Implement CustomModes and AllModes**

In `src/TextFix/Models/AppSettings.cs`, add the property after `ActiveModeName`:

```csharp
public List<CorrectionMode> CustomModes { get; set; } = [];
```

Update `GetActiveMode()`:

```csharp
public CorrectionMode GetActiveMode()
{
    return CorrectionMode.Defaults.FirstOrDefault(m => m.Name == ActiveModeName)
        ?? CustomModes.FirstOrDefault(m => m.Name == ActiveModeName)
        ?? CorrectionMode.Defaults[0];
}
```

Add the `AllModes()` helper:

```csharp
public IReadOnlyList<CorrectionMode> AllModes()
{
    var list = new List<CorrectionMode>(CorrectionMode.Defaults);
    list.AddRange(CustomModes);
    return list;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "AppSettingsTests"`
Expected: PASS (15 tests)

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/AppSettings.cs tests/TextFix.Tests/Models/AppSettingsTests.cs
git commit -m "feat: add CustomModes and AllModes to AppSettings"
```

---

### Task 6: Add ReapplyAsync to CorrectionService

**Files:**
- Modify: `src/TextFix/Services/CorrectionService.cs`

- [ ] **Step 1: Add ReapplyAsync method**

Add two new methods to `CorrectionService`:

```csharp
public async Task ReapplyAsync(string text)
{
    Cancel();
    _cts = new CancellationTokenSource();

    ProcessingStarted?.Invoke();

    var mode = _settings.GetActiveMode();
    var result = await _aiClient.CorrectAsync(text, mode.SystemPrompt, _cts.Token);
    result = result with { ModeName = mode.Name };

    if (_cts.Token.IsCancellationRequested)
        return;

    LastResult = result;
    _history.Add(result);
    CorrectionCompleted?.Invoke(result);
}

public async Task ReapplyWithPromptAsync(string text, string customPrompt)
{
    Cancel();
    _cts = new CancellationTokenSource();

    ProcessingStarted?.Invoke();

    var result = await _aiClient.CorrectAsync(text, customPrompt, _cts.Token);
    result = result with { ModeName = "Custom" };

    if (_cts.Token.IsCancellationRequested)
        return;

    LastResult = result;
    _history.Add(result);
    CorrectionCompleted?.Invoke(result);
}
```

- [ ] **Step 2: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/CorrectionService.cs
git commit -m "feat: add ReapplyAsync and ReapplyWithPromptAsync to CorrectionService"
```

---

### Task 7: Update model list and Settings custom modes UI

**Files:**
- Modify: `src/TextFix/Views/SettingsWindow.xaml.cs`
- Modify: `src/TextFix/Views/SettingsWindow.xaml`

- [ ] **Step 1: Add 4.7 models to KnownModels**

In `src/TextFix/Views/SettingsWindow.xaml.cs`, update the `KnownModels` array:

```csharp
public static readonly string[] KnownModels =
[
    "claude-haiku-4-5-20251001",
    "claude-sonnet-4-5-20250514",
    "claude-sonnet-4-6",
    "claude-sonnet-4-7",
    "claude-opus-4-6",
    "claude-opus-4-7",
];
```

- [ ] **Step 2: Update mode population to include custom modes**

In `SettingsWindow` constructor, replace:

```csharp
foreach (var mode in CorrectionMode.Defaults)
    ModeBox.Items.Add(mode.Name);
ModeBox.SelectedItem = settings.ActiveModeName;
```

With:

```csharp
foreach (var mode in settings.AllModes())
    ModeBox.Items.Add(mode.Name);
ModeBox.SelectedItem = settings.ActiveModeName;

PopulateCustomModesList();
```

- [ ] **Step 3: Add custom modes management section to XAML**

In `src/TextFix/Views/SettingsWindow.xaml`, add a new row definition and custom modes section. Update the Grid.RowDefinitions to add a row for custom modes. Replace:

```xml
<RowDefinition Height="*"/>
<RowDefinition Height="Auto"/>
```

With:

```xml
<RowDefinition Height="Auto"/>
<RowDefinition Height="*"/>
<RowDefinition Height="Auto"/>
```

Move the "Keep overlay open" checkbox to Grid.Row="7" and the Save/Cancel buttons to Grid.Row="8".

Add the custom modes section at Grid.Row="6":

```xml
<StackPanel Grid.Row="6" Margin="0,0,0,12">
    <DockPanel Margin="0,0,0,4">
        <Button Content="+ Add Mode" DockPanel.Dock="Right"
                Background="#6C63FF" Foreground="White" BorderBrush="Transparent"
                FontSize="11" Cursor="Hand" Padding="8,2" Click="OnAddCustomMode"/>
        <Label Content="Custom Modes"/>
    </DockPanel>
    <ScrollViewer MaxHeight="120" VerticalScrollBarVisibility="Auto">
        <StackPanel x:Name="CustomModesList"/>
    </ScrollViewer>
</StackPanel>
```

Update Grid.Row attributes:
- Keep overlay checkbox: `Grid.Row="7"`
- Save/Cancel buttons: `Grid.Row="8"`

- [ ] **Step 4: Implement custom mode CRUD in code-behind**

Add to `SettingsWindow.xaml.cs`:

```csharp
private void PopulateCustomModesList()
{
    CustomModesList.Children.Clear();
    foreach (var mode in _settings.CustomModes)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var deleteBtn = new System.Windows.Controls.Button
        {
            Content = "\u2715",
            Width = 24, Height = 24,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
        };
        var editBtn = new System.Windows.Controls.Button
        {
            Content = "\u270E",
            Width = 24, Height = 24,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
        };

        DockPanel.SetDock(deleteBtn, Dock.Right);
        DockPanel.SetDock(editBtn, Dock.Right);

        var nameText = new TextBlock
        {
            Text = mode.Name,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var capturedMode = mode;
        deleteBtn.Click += (_, _) => OnDeleteCustomMode(capturedMode);
        editBtn.Click += (_, _) => OnEditCustomMode(capturedMode);

        row.Children.Add(deleteBtn);
        row.Children.Add(editBtn);
        row.Children.Add(nameText);

        CustomModesList.Children.Add(row);
    }
}

private void OnAddCustomMode(object sender, RoutedEventArgs e)
{
    var dialog = new CustomModeDialog();
    dialog.Owner = this;
    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ModeName))
    {
        _settings.CustomModes.Add(new CorrectionMode
        {
            Name = dialog.ModeName,
            SystemPrompt = dialog.ModePrompt,
        });
        RefreshModeBox();
        PopulateCustomModesList();
    }
}

private void OnEditCustomMode(CorrectionMode mode)
{
    var dialog = new CustomModeDialog(mode.Name, mode.SystemPrompt);
    dialog.Owner = this;
    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ModeName))
    {
        var idx = _settings.CustomModes.FindIndex(m => m.Name == mode.Name);
        if (idx >= 0)
        {
            _settings.CustomModes[idx] = new CorrectionMode
            {
                Name = dialog.ModeName,
                SystemPrompt = dialog.ModePrompt,
            };
        }
        RefreshModeBox();
        PopulateCustomModesList();
    }
}

private void OnDeleteCustomMode(CorrectionMode mode)
{
    _settings.CustomModes.RemoveAll(m => m.Name == mode.Name);
    RefreshModeBox();
    PopulateCustomModesList();
}

private void RefreshModeBox()
{
    var selected = ModeBox.SelectedItem as string;
    ModeBox.Items.Clear();
    foreach (var m in _settings.AllModes())
        ModeBox.Items.Add(m.Name);
    ModeBox.SelectedItem = selected ?? _settings.ActiveModeName;
}
```

- [ ] **Step 5: Create the CustomModeDialog**

Create `src/TextFix/Views/CustomModeDialog.xaml`:

```xml
<Window x:Class="TextFix.Views.CustomModeDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Custom Mode"
        Width="400" Height="300"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        Background="#1E1E2E"
        Foreground="#E0E0E0"
        FontFamily="Segoe UI">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Mode Name" Foreground="#AAAAAA" FontSize="12" Margin="0,0,0,4"/>
        <TextBox x:Name="NameBox" Grid.Row="1"
                 Background="#2D2D3F" Foreground="#E0E0E0" BorderBrush="#555"
                 CaretBrush="#E0E0E0" Padding="6,4" Margin="0,0,0,12"/>

        <TextBlock Grid.Row="2" Text="System Prompt" Foreground="#AAAAAA" FontSize="12" Margin="0,0,0,4"/>
        <TextBox x:Name="PromptBox" Grid.Row="3"
                 Background="#2D2D3F" Foreground="#E0E0E0" BorderBrush="#555"
                 CaretBrush="#E0E0E0" Padding="6,4" Margin="0,0,0,12"
                 TextWrapping="Wrap" AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto"/>

        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Save" Width="80" Height="30"
                    Background="#6C63FF" Foreground="White" BorderBrush="Transparent"
                    FontSize="13" Cursor="Hand" Click="OnSave" Margin="0,0,8,0"/>
            <Button Content="Cancel" Width="80" Height="30"
                    Background="#444" Foreground="#CCC" BorderBrush="Transparent"
                    FontSize="13" Cursor="Hand" Click="OnCancel"/>
        </StackPanel>
    </Grid>
</Window>
```

Create `src/TextFix/Views/CustomModeDialog.xaml.cs`:

```csharp
using System.Windows;

namespace TextFix.Views;

public partial class CustomModeDialog : Window
{
    public string ModeName => NameBox.Text.Trim();
    public string ModePrompt => PromptBox.Text.Trim();

    public CustomModeDialog(string name = "", string prompt = "")
    {
        InitializeComponent();
        NameBox.Text = name;
        PromptBox.Text = prompt;
        if (!string.IsNullOrEmpty(name))
            Title = "Edit Mode";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Mode name is required.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
```

- [ ] **Step 6: Build to verify everything compiles**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/TextFix/Views/SettingsWindow.xaml src/TextFix/Views/SettingsWindow.xaml.cs src/TextFix/Views/CustomModeDialog.xaml src/TextFix/Views/CustomModeDialog.xaml.cs
git commit -m "feat: add 4.7 models, custom mode CRUD in Settings"
```

---

### Task 8: Add overlay custom prompt box and split Redo buttons

**Files:**
- Modify: `src/TextFix/Views/OverlayWindow.xaml`
- Modify: `src/TextFix/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Add custom prompt XAML**

In `src/TextFix/Views/OverlayWindow.xaml`, replace the ActionRowPanel section:

```xml
<!-- Action row: redo, copy, history, close -->
<StackPanel x:Name="ActionRowPanel" Orientation="Horizontal"
            Visibility="Collapsed" Margin="0,8,0,0">
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
```

With:

```xml
<!-- Custom prompt box -->
<DockPanel x:Name="PromptPanel" Visibility="Collapsed" Margin="0,8,0,0">
    <Button x:Name="PromptGoButton" Content="Go" DockPanel.Dock="Right"
            Background="#6C63FF" Foreground="White" Margin="4,0,0,0"
            Style="{StaticResource SmallActionButton}" Click="OnPromptGo"/>
    <TextBox x:Name="PromptBox"
             Background="#2A2A3E" Foreground="#E0E0E0" BorderBrush="#555"
             CaretBrush="#E0E0E0" FontFamily="Segoe UI" FontSize="11"
             Padding="6,3" VerticalAlignment="Center"
             KeyDown="OnPromptKeyDown"/>
</DockPanel>

<!-- Save as mode (shown after custom prompt success) -->
<StackPanel x:Name="SaveModePanel" Orientation="Horizontal"
            Visibility="Collapsed" Margin="0,4,0,0">
    <TextBlock x:Name="SaveModeLink" Text="Save as mode..."
               Foreground="#6C63FF" FontSize="11" FontFamily="Segoe UI"
               Cursor="Hand" VerticalAlignment="Center"
               MouseLeftButtonDown="OnSaveModeClick"/>
    <TextBox x:Name="SaveModeNameBox" Visibility="Collapsed"
             Background="#2A2A3E" Foreground="#E0E0E0" BorderBrush="#555"
             CaretBrush="#E0E0E0" FontFamily="Segoe UI" FontSize="11"
             Padding="6,3" Width="140" Margin="4,0,0,0"
             KeyDown="OnSaveModeNameKeyDown"/>
</StackPanel>

<!-- Action row: redo original, redo refine, copy, history -->
<StackPanel x:Name="ActionRowPanel" Orientation="Horizontal"
            Visibility="Collapsed" Margin="0,8,0,0">
    <Button x:Name="RedoOrigButton" Content="&#x21BB; Original"
            Background="#2A2A3E" Foreground="#AAAAAA"
            Style="{StaticResource SmallActionButton}" Click="OnActionRedoOriginal"/>
    <Button x:Name="RedoRefineButton" Content="&#x21BB; Refine"
            Background="#2A2A3E" Foreground="#AAAAAA"
            Style="{StaticResource SmallActionButton}" Click="OnActionRedoRefine"/>
    <Button x:Name="CopyButton" Content="&#x2398; Copy"
            Background="#2A2A3E" Foreground="#AAAAAA"
            Style="{StaticResource SmallActionButton}" Click="OnActionCopy"/>
    <Button x:Name="HistoryToggleButton" Content="&#x29D6; History"
            Background="#2A2A3E" Foreground="#AAAAAA"
            Style="{StaticResource SmallActionButton}" Click="OnActionHistoryToggle"/>
</StackPanel>
```

- [ ] **Step 2: Add watermark/placeholder behavior for PromptBox**

Add a `GotFocus`/`LostFocus` approach in the XAML TextBox. Update the PromptBox attributes:

Actually, WPF TextBox doesn't have native placeholder. Instead, set the initial text in code-behind when showing panels. We'll handle it in Step 4.

- [ ] **Step 3: Add new events and fields to code-behind**

In `src/TextFix/Views/OverlayWindow.xaml.cs`, add new events after existing events:

```csharp
public event Action<string>? ReapplyRequested;      // string = text to reapply
public event Action<string, string>? CustomPromptRequested; // text, prompt
public event Action<string, string>? SaveModeRequested;     // name, prompt
```

Add a field to track the last custom prompt:

```csharp
private string? _lastCustomPrompt;
```

- [ ] **Step 4: Implement prompt box handlers**

Add to `OverlayWindow.xaml.cs`:

```csharp
private void OnPromptKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(PromptBox.Text))
    {
        e.Handled = true;
        SubmitCustomPrompt();
    }
}

private void OnPromptGo(object sender, RoutedEventArgs e)
{
    if (!string.IsNullOrWhiteSpace(PromptBox.Text))
        SubmitCustomPrompt();
}

private void SubmitCustomPrompt()
{
    var prompt = PromptBox.Text.Trim();
    _lastCustomPrompt = prompt;
    SaveModePanel.Visibility = Visibility.Collapsed;

    // Determine which text to use based on state
    string? text = null;
    if (_currentResult is not null)
    {
        // In result view: use original. In applied/idle: use corrected.
        text = (_showingApplied || _showingIdle)
            ? _currentResult.CorrectedText
            : _currentResult.OriginalText;
    }

    if (text is not null)
        CustomPromptRequested?.Invoke(text, prompt);
}

private void OnSaveModeClick(object sender, MouseButtonEventArgs e)
{
    SaveModeLink.Visibility = Visibility.Collapsed;
    SaveModeNameBox.Visibility = Visibility.Visible;
    SaveModeNameBox.Text = "";
    SaveModeNameBox.Focus();
}

private void OnSaveModeNameKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(SaveModeNameBox.Text) && _lastCustomPrompt is not null)
    {
        e.Handled = true;
        SaveModeRequested?.Invoke(SaveModeNameBox.Text.Trim(), _lastCustomPrompt);
        SaveModePanel.Visibility = Visibility.Collapsed;
        SaveModeNameBox.Visibility = Visibility.Collapsed;
        SaveModeLink.Visibility = Visibility.Visible;
    }
    else if (e.Key == Key.Escape)
    {
        e.Handled = true;
        SaveModeNameBox.Visibility = Visibility.Collapsed;
        SaveModeLink.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 5: Replace old Redo handler with split handlers**

Remove the old `OnActionRedo` method. Add:

```csharp
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
    if (_currentResult is not null)
        ReapplyRequested?.Invoke(_currentResult.CorrectedText);
}
```

- [ ] **Step 6: Update ShowResult to show prompt panel and handle mode-change re-run**

In `ShowResult`, after the success path sets up the diff display (after `CorrectedText.Text = result.CorrectedText;`), add:

```csharp
PromptPanel.Visibility = Visibility.Visible;
PromptBox.Text = "";
SaveModePanel.Visibility = Visibility.Collapsed;
```

Update `OnModeChanged` to trigger a re-run when in result view:

Replace:
```csharp
private void OnModeChanged(object sender, SelectionChangedEventArgs e)
{
    if (_suppressModeChange) return;
    if (ModeBox.SelectedItem is ComboBoxItem item)
        ModeChanged?.Invoke((string)item.Tag);
}
```

With:
```csharp
private void OnModeChanged(object sender, SelectionChangedEventArgs e)
{
    if (_suppressModeChange) return;
    if (ModeBox.SelectedItem is ComboBoxItem item)
    {
        ModeChanged?.Invoke((string)item.Tag);
        // If in result view, trigger re-run from original text
        if (!_showingError && !_showingApplied && !_showingIdle && _currentResult is not null)
        {
            ReapplyRequested?.Invoke(_currentResult.OriginalText);
        }
    }
}
```

- [ ] **Step 7: Show prompt panel and save-mode link in ShowApplied and ShowIdle**

In `ShowApplied()`, after `CopyButton.IsEnabled = true;` add:

```csharp
PromptPanel.Visibility = Visibility.Visible;
PromptBox.Text = "";
if (_lastCustomPrompt is not null)
    SaveModePanel.Visibility = Visibility.Visible;
```

In `ShowIdle()`, after `CopyButton.IsEnabled = ...` add:

```csharp
PromptPanel.Visibility = Visibility.Visible;
PromptBox.Text = "";
SaveModePanel.Visibility = Visibility.Collapsed;
```

- [ ] **Step 8: Reset new panels in ShowProcessing, ShowResult error/no-change, ShowFocusLost**

In `ShowProcessing()`, add after `ActionRowPanel.Visibility = Visibility.Collapsed;`:

```csharp
PromptPanel.Visibility = Visibility.Collapsed;
SaveModePanel.Visibility = Visibility.Collapsed;
```

In `ShowResult` error branch and no-change branch, the `PromptPanel` stays collapsed (already collapsed by ShowProcessing earlier).

In `ShowFocusLost()`, add after `HistoryPanel.Visibility = Visibility.Collapsed;`:

```csharp
PromptPanel.Visibility = Visibility.Collapsed;
SaveModePanel.Visibility = Visibility.Collapsed;
```

- [ ] **Step 9: Update PopulateModes to include custom modes**

Replace `PopulateModes()`:

```csharp
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
```

Add a public method to refresh modes from outside:

```csharp
public void RefreshModes(IReadOnlyList<CorrectionMode> allModes, string activeName)
{
    PopulateModes(allModes);
    SetActiveMode(activeName);
}
```

- [ ] **Step 10: Update OnKeyDown to not intercept Enter when PromptBox is focused**

In `OnKeyDown`, at the very beginning add:

```csharp
// Don't intercept Enter/Escape when typing in prompt or save-mode boxes
if (PromptBox.IsFocused || SaveModeNameBox.IsFocused)
    return;
```

- [ ] **Step 11: Update history stats display to include token counts and cost**

In `PopulateHistoryPanel()`, update the `modeLine` to include tokens:

Replace:
```csharp
var modeLine = string.IsNullOrEmpty(item.ModeName)
    ? age
    : $"{item.ModeName} \u00b7 {age}";
```

With:
```csharp
var tokens = item.InputTokens + item.OutputTokens;
var tokenStr = tokens > 0 ? $" \u00b7 {tokens} tokens" : "";
var modeLine = string.IsNullOrEmpty(item.ModeName)
    ? $"{age}{tokenStr}"
    : $"{item.ModeName} \u00b7 {age}{tokenStr}";
```

Update `IdleStatsText` in `ShowIdle()` and `HistoryStatsText` in `PopulateHistoryPanel()` to include session cost:

In `ShowIdle()`, replace:
```csharp
IdleStatsText.Text = $"{history.TodayCount} today \u00b7 {history.TotalCount} total";
```

With:
```csharp
var costStr = history.SessionCost > 0 ? $" \u00b7 ${history.SessionCost:F4} session" : "";
IdleStatsText.Text = $"{history.TodayCount} today \u00b7 {history.TotalCount} total{costStr}";
```

In `PopulateHistoryPanel()`, replace:
```csharp
HistoryStatsText.Text = $"{_history.TodayCount} today \u00b7 {_history.TotalCount} total";
```

With:
```csharp
var costStr = _history.SessionCost > 0 ? $" \u00b7 ${_history.SessionCost:F4} session" : "";
HistoryStatsText.Text = $"{_history.TodayCount} today \u00b7 {_history.TotalCount} total{costStr}";
```

- [ ] **Step 12: Build to verify everything compiles**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 13: Commit**

```bash
git add src/TextFix/Views/OverlayWindow.xaml src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "feat: add custom prompt box, split redo, mode-change re-run, token stats display"
```

---

### Task 9: Wire everything in App.xaml.cs

**Files:**
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Load history at startup**

In `OnStartup`, replace:

```csharp
CreateHiddenWindow();
SetupTrayIcon();
SetupOverlay();
SetupServices();
```

With:

```csharp
CreateHiddenWindow();
SetupTrayIcon();
SetupOverlay();
await SetupServicesAsync();
```

- [ ] **Step 2: Make SetupServices async to load history**

Rename `SetupServices` to `SetupServicesAsync` and make it async:

```csharp
private async Task SetupServicesAsync()
{
    _clipboardManager = new ClipboardManager();
    _focusTracker = new FocusTracker();

    if (!string.IsNullOrWhiteSpace(_settings.GetApiKey()))
        _aiClient = new AiClient(_settings);

    var history = await CorrectionHistory.LoadAsync();
    _correctionService = new CorrectionService(_clipboardManager, _focusTracker, _aiClient!, _settings, history);

    _correctionService.ProcessingStarted += () =>
        Dispatcher.Invoke(() => _overlay?.ShowProcessing());

    _correctionService.CorrectionCompleted += result =>
        Dispatcher.Invoke(async () =>
        {
            _overlay?.ShowResult(result, _settings.OverlayAutoApplySeconds, _settings.KeepOverlayOpen);
            RefreshHistoryMenu();
            await _correctionService.History.SaveAsync();
        });

    _correctionService.ErrorOccurred += msg =>
        Dispatcher.Invoke(() =>
        {
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(CorrectionResult.Error("", msg), 0);
        });

    _correctionService.FocusLost += () =>
        Dispatcher.Invoke(() => _overlay?.ShowFocusLost());
}
```

- [ ] **Step 3: Update CorrectionService constructor to accept history**

In `src/TextFix/Services/CorrectionService.cs`, update the constructor to accept an optional history parameter:

Replace:
```csharp
private readonly CorrectionHistory _history = new();
```

With:
```csharp
private readonly CorrectionHistory _history;
```

Replace the constructor:
```csharp
public CorrectionService(ClipboardManager clipboard, FocusTracker focusTracker, AiClient aiClient, AppSettings settings)
{
    _clipboard = clipboard;
    _focusTracker = focusTracker;
    _aiClient = aiClient;
    _settings = settings;
}
```

With:
```csharp
public CorrectionService(ClipboardManager clipboard, FocusTracker focusTracker, AiClient aiClient, AppSettings settings, CorrectionHistory? history = null)
{
    _clipboard = clipboard;
    _focusTracker = focusTracker;
    _aiClient = aiClient;
    _settings = settings;
    _history = history ?? new CorrectionHistory();
}
```

- [ ] **Step 4: Wire new overlay events in SetupOverlay**

In `SetupOverlay()`, add after existing event subscriptions:

```csharp
_overlay.ReapplyRequested += OnReapplyRequested;
_overlay.CustomPromptRequested += OnCustomPromptRequested;
_overlay.SaveModeRequested += OnSaveModeRequested;
_overlay.RefreshModes(_settings.AllModes(), _settings.ActiveModeName);
```

- [ ] **Step 5: Implement new event handlers**

Add these methods:

```csharp
private async void OnReapplyRequested(string text)
{
    if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return;

    try
    {
        if (string.IsNullOrWhiteSpace(_settings.GetApiKey()))
        {
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(
                CorrectionResult.Error("", "Set up your API key in Settings."), 0);
            return;
        }

        await _correctionService!.ReapplyAsync(text);
    }
    catch (Exception ex)
    {
        LogError(ex);
        _overlay?.ShowProcessing();
        _overlay?.ShowResult(CorrectionResult.Error("", "An unexpected error occurred."), 0);
    }
    finally
    {
        Interlocked.Exchange(ref _isBusy, 0);
    }
}

private async void OnCustomPromptRequested(string text, string prompt)
{
    if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return;

    try
    {
        if (string.IsNullOrWhiteSpace(_settings.GetApiKey()))
        {
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(
                CorrectionResult.Error("", "Set up your API key in Settings."), 0);
            return;
        }

        await _correctionService!.ReapplyWithPromptAsync(text, prompt);
    }
    catch (Exception ex)
    {
        LogError(ex);
        _overlay?.ShowProcessing();
        _overlay?.ShowResult(CorrectionResult.Error("", "An unexpected error occurred."), 0);
    }
    finally
    {
        Interlocked.Exchange(ref _isBusy, 0);
    }
}

private async void OnSaveModeRequested(string name, string prompt)
{
    _settings.CustomModes.Add(new CorrectionMode
    {
        Name = name,
        SystemPrompt = prompt,
    });
    await _settings.SaveAsync();
    RebuildModeMenus();
}
```

- [ ] **Step 6: Add RebuildModeMenus helper**

```csharp
private void RebuildModeMenus()
{
    // Refresh overlay mode selector
    _overlay?.RefreshModes(_settings.AllModes(), _settings.ActiveModeName);

    // Refresh tray mode submenu
    if (_trayIcon?.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
    {
        modeMenu.DropDownItems.Clear();
        foreach (var mode in _settings.AllModes())
        {
            var item = new ToolStripMenuItem(mode.Name)
            {
                Checked = mode.Name == _settings.ActiveModeName,
                Tag = mode.Name,
            };
            item.Click += OnModeSelected;
            modeMenu.DropDownItems.Add(item);
        }
    }
}
```

- [ ] **Step 7: Update OnOverlayModeChanged to also re-run when in result state**

The overlay now fires `ReapplyRequested` when mode changes during result view, so `OnOverlayModeChanged` just needs to save the setting (which it already does). No change needed.

- [ ] **Step 8: Update OpenSettings to rebuild modes after settings close**

In `OpenSettings()`, add `RebuildModeMenus()` after the existing calls:

Replace:
```csharp
if (window.SettingsChanged)
{
    RebuildServices();
    RegisterHotkey();
    SyncTrayState();
}
```

With:
```csharp
if (window.SettingsChanged)
{
    RebuildServices();
    RegisterHotkey();
    SyncTrayState();
    RebuildModeMenus();
}
```

- [ ] **Step 9: Update SetupTrayIcon to use AllModes**

In `SetupTrayIcon()`, replace:

```csharp
foreach (var mode in CorrectionMode.Defaults)
```

With:

```csharp
foreach (var mode in _settings.AllModes())
```

- [ ] **Step 10: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: All tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/TextFix/App.xaml.cs src/TextFix/Services/CorrectionService.cs
git commit -m "feat: wire mode cycling, custom prompts, history persistence in App shell"
```

---

### Task 10: Build, Test, and Release

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 2: Build Release**

Run: `dotnet build -c Release`
Expected: Build succeeds.

- [ ] **Step 3: Manual smoke test**

Run: `taskkill /IM TextFix.exe /F 2>/dev/null; dotnet run --project src/TextFix/TextFix.csproj`

Verify:
1. Left-click tray → idle overlay shows stats with cost ($0.00 initially)
2. Use hotkey to correct text → result view shows diff, prompt box, and mode selector
3. Change mode in result view → re-runs automatically from original text
4. Type "make this sarcastic" in prompt box, Enter → processes with custom prompt
5. After custom prompt result, "Save as mode..." link appears
6. Click "Save as mode..." → type name → Enter → saved (check tray menu)
7. Click "Redo (original)" → re-runs from original captured text
8. Click "Redo (refine)" → re-runs from most recent corrected text
9. Settings → new 4.7 models visible in dropdown
10. Settings → custom modes section shows saved modes with edit/delete
11. Close and restart app → history persists, TotalCount preserved
12. History items show token counts

- [ ] **Step 4: Tag and push release**

```bash
git tag v0.3.0
git push origin master
git push origin v0.3.0
```
