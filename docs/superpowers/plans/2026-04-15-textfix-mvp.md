# TextFix MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows system tray app that corrects selected text in any application via a global hotkey and the Claude API.

**Architecture:** Single WPF process, no main window. System tray icon with context menu. Global hotkey triggers a pipeline: capture selected text via clipboard, send to Claude Haiku, show result in a floating overlay, paste back on approval. All components live in one project with clear class boundaries.

**Tech Stack:** .NET 10, WPF, C#, Anthropic NuGet package (v12.x), System.Text.Json

---

## File Structure

```
TextFix/
├── TextFix.sln
├── src/
│   └── TextFix/
│       ├── TextFix.csproj
│       ├── App.xaml                    # Application entry, no main window
│       ├── App.xaml.cs                 # Startup: single instance, tray, hotkey
│       ├── Services/
│       │   ├── HotkeyListener.cs       # Win32 RegisterHotKey wrapper
│       │   ├── ClipboardManager.cs     # Save/restore clipboard, simulate Ctrl+C/V
│       │   ├── FocusTracker.cs         # Track source window handle
│       │   ├── CorrectionService.cs    # Orchestrates the full correction pipeline
│       │   └── AiClient.cs            # Anthropic SDK wrapper
│       ├── Models/
│       │   ├── AppSettings.cs          # Settings POCO + load/save
│       │   └── CorrectionResult.cs     # Result of a correction attempt
│       ├── Views/
│       │   ├── OverlayWindow.xaml      # Floating overlay (pill → diff toast)
│       │   ├── OverlayWindow.xaml.cs
│       │   ├── SettingsWindow.xaml     # Settings form
│       │   └── SettingsWindow.xaml.cs
│       └── Interop/
│           └── NativeMethods.cs        # All P/Invoke declarations
├── tests/
│   └── TextFix.Tests/
│       ├── TextFix.Tests.csproj
│       ├── Services/
│       │   ├── AiClientTests.cs
│       │   ├── CorrectionServiceTests.cs
│       │   └── AppSettingsTests.cs
│       └── Models/
│           └── AppSettingsTests.cs
```

---

### Task 1: Project Scaffold

**Files:**
- Create: `TextFix.sln`
- Create: `src/TextFix/TextFix.csproj`
- Create: `tests/TextFix.Tests/TextFix.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet new sln -n TextFix
mkdir -p src/TextFix
dotnet new wpf -n TextFix -o src/TextFix --framework net10.0
mkdir -p tests/TextFix.Tests
dotnet new xunit -n TextFix.Tests -o tests/TextFix.Tests --framework net10.0
dotnet sln add src/TextFix/TextFix.csproj
dotnet sln add tests/TextFix.Tests/TextFix.Tests.csproj
dotnet add tests/TextFix.Tests reference src/TextFix
```

- [ ] **Step 2: Add NuGet packages**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet add src/TextFix package Anthropic
dotnet add tests/TextFix.Tests package Moq
```

- [ ] **Step 3: Configure TextFix.csproj for system tray app**

Edit `src/TextFix/TextFix.csproj` — set output type to WinExe, add an app icon placeholder, and target Windows:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationIcon />
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Anthropic" Version="12.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create .gitignore**

Create `.gitignore` in repo root:

```
bin/
obj/
.vs/
*.user
*.suo
.superpowers/
```

- [ ] **Step 5: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add TextFix.sln src/ tests/ .gitignore
git commit -m "feat: scaffold TextFix solution with WPF app and test projects"
```

---

### Task 2: Native Interop (P/Invoke Declarations)

**Files:**
- Create: `src/TextFix/Interop/NativeMethods.cs`

- [ ] **Step 1: Create NativeMethods with all Win32 imports**

Create `src/TextFix/Interop/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace TextFix.Interop;

internal static partial class NativeMethods
{
    // Hotkey registration
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // Focus tracking
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    // Cursor position for overlay placement
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    // Simulate keyboard input
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // Hotkey modifier flags
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_C = 0x43;
    public const ushort VK_V = 0x56;

    // SendInput constants
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // Window message for hotkey
    public const int WM_HOTKEY = 0x0312;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Interop/
git commit -m "feat: add Win32 P/Invoke declarations for hotkey, clipboard, and focus APIs"
```

---

### Task 3: Settings Model

**Files:**
- Create: `src/TextFix/Models/AppSettings.cs`
- Create: `tests/TextFix.Tests/Models/AppSettingsTests.cs`

- [ ] **Step 1: Write failing tests for AppSettings**

Create `tests/TextFix.Tests/Models/AppSettingsTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using TextFix.Models;

namespace TextFix.Tests.Models;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TextFixTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Defaults_HasExpectedValues()
    {
        var settings = new AppSettings();

        Assert.Equal("", settings.ApiKey);
        Assert.Equal("Ctrl+Shift+C", settings.Hotkey);
        Assert.Equal("claude-haiku-4-5-20251001", settings.Model);
        Assert.Equal(3, settings.OverlayAutoApplySeconds);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(
            "Fix all typos, spelling, and grammar errors in the following text. Return only the corrected text with no explanation. Preserve the original meaning, tone, and formatting.",
            settings.SystemPrompt);
    }

    [Fact]
    public async Task Save_CreatesJsonFile()
    {
        var settings = new AppSettings { ApiKey = "test-key-123" };
        var path = Path.Combine(_tempDir, "settings.json");

        await settings.SaveAsync(path);

        Assert.True(File.Exists(path));
        var json = await File.ReadAllTextAsync(path);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(loaded);
        Assert.Equal("test-key-123", loaded.ApiKey);
    }

    [Fact]
    public async Task Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");

        var settings = await AppSettings.LoadAsync(path);

        Assert.Equal("", settings.ApiKey);
        Assert.Equal("Ctrl+Shift+C", settings.Hotkey);
    }

    [Fact]
    public async Task Load_ReturnsDefaults_WhenFileIsCorrupted()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(path, "not valid json {{{");

        var settings = await AppSettings.LoadAsync(path);

        Assert.Equal("", settings.ApiKey);
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var original = new AppSettings
        {
            ApiKey = "sk-ant-test",
            Hotkey = "Ctrl+Alt+F",
            Model = "claude-haiku-4-5-20251001",
            SystemPrompt = "Custom prompt",
            OverlayAutoApplySeconds = 5,
            StartWithWindows = true,
        };
        var path = Path.Combine(_tempDir, "settings.json");

        await original.SaveAsync(path);
        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(original.ApiKey, loaded.ApiKey);
        Assert.Equal(original.Hotkey, loaded.Hotkey);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.SystemPrompt, loaded.SystemPrompt);
        Assert.Equal(original.OverlayAutoApplySeconds, loaded.OverlayAutoApplySeconds);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet test tests/TextFix.Tests --filter "FullyQualifiedName~AppSettingsTests" -v minimal
```

Expected: Build failure — `AppSettings` does not exist.

- [ ] **Step 3: Implement AppSettings**

Create `src/TextFix/Models/AppSettings.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextFix.Models;

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ApiKey { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Shift+C";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";

    public string SystemPrompt { get; set; } =
        "Fix all typos, spelling, and grammar errors in the following text. Return only the corrected text with no explanation. Preserve the original meaning, tone, and formatting.";

    public int OverlayAutoApplySeconds { get; set; } = 3;
    public bool StartWithWindows { get; set; }

    [JsonIgnore]
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix",
            "settings.json");

    public async Task SaveAsync(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public static async Task<AppSettings> LoadAsync(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet test tests/TextFix.Tests --filter "FullyQualifiedName~AppSettingsTests" -v minimal
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/Models/AppSettings.cs tests/TextFix.Tests/Models/
git commit -m "feat: add AppSettings model with JSON persistence and defaults"
```

---

### Task 4: AI Client

**Files:**
- Create: `src/TextFix/Models/CorrectionResult.cs`
- Create: `src/TextFix/Services/AiClient.cs`
- Create: `tests/TextFix.Tests/Services/AiClientTests.cs`

- [ ] **Step 1: Create CorrectionResult model**

Create `src/TextFix/Models/CorrectionResult.cs`:

```csharp
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
```

- [ ] **Step 2: Write failing tests for AiClient**

Create `tests/TextFix.Tests/Services/AiClientTests.cs`:

```csharp
using TextFix.Models;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class AiClientTests
{
    [Fact]
    public void Constructor_ThrowsOnEmptyApiKey()
    {
        var settings = new AppSettings { ApiKey = "" };

        var ex = Assert.Throws<InvalidOperationException>(() => new AiClient(settings));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_Succeeds_WithApiKey()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };

        var client = new AiClient(settings);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextIsEmpty()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };
        var client = new AiClient(settings);

        var result = await client.CorrectAsync("");

        Assert.True(result.IsError);
        Assert.Contains("empty", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsError_WhenTextTooLong()
    {
        var settings = new AppSettings { ApiKey = "sk-ant-test-key" };
        var client = new AiClient(settings);
        var longText = new string('a', 5001);

        var result = await client.CorrectAsync(longText);

        Assert.True(result.IsError);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet test tests/TextFix.Tests --filter "FullyQualifiedName~AiClientTests" -v minimal
```

Expected: Build failure — `AiClient` does not exist.

- [ ] **Step 4: Implement AiClient**

Create `src/TextFix/Services/AiClient.cs`:

```csharp
using Anthropic;
using Anthropic.Models.Messages;
using TextFix.Models;

namespace TextFix.Services;

public class AiClient
{
    private readonly AnthropicClient _client;
    private readonly AppSettings _settings;
    private const int MaxTextLength = 5000;

    public AiClient(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("API key is not configured. Set your API key in Settings.");

        _settings = settings;
        _client = new AnthropicClient
        {
            ApiKey = settings.ApiKey,
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<CorrectionResult> CorrectAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return CorrectionResult.Error(text, "Text is empty.");

        if (text.Length > MaxTextLength)
            return CorrectionResult.Error(text, $"Text too long ({text.Length} chars). Select a shorter passage (max {MaxTextLength}).");

        try
        {
            var parameters = new MessageCreateParams
            {
                Model = _settings.Model,
                MaxTokens = 4096,
                System = _settings.SystemPrompt,
                Messages =
                [
                    new() { Role = Role.User, Content = text },
                ],
            };

            var message = await _client.Messages.Create(parameters, ct);
            var corrected = message.Content
                .OfType<TextBlock>()
                .Select(b => b.Text)
                .FirstOrDefault() ?? text;

            return new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected.Trim(),
            };
        }
        catch (OperationCanceledException)
        {
            return CorrectionResult.Error(text, "Correction cancelled.");
        }
        catch (AnthropicUnauthorizedException)
        {
            return CorrectionResult.Error(text, "API key is invalid. Check your key in Settings.");
        }
        catch (AnthropicRateLimitException)
        {
            return CorrectionResult.Error(text, "Rate limited — try again in a moment.");
        }
        catch (Anthropic5xxException)
        {
            return CorrectionResult.Error(text, "Claude service is unavailable. Try again later.");
        }
        catch (AnthropicIOException)
        {
            return CorrectionResult.Error(text, "Network error — check your internet connection.");
        }
        catch (Exception ex)
        {
            return CorrectionResult.Error(text, $"Unexpected error: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet test tests/TextFix.Tests --filter "FullyQualifiedName~AiClientTests" -v minimal
```

Expected: All 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/TextFix/Models/CorrectionResult.cs src/TextFix/Services/AiClient.cs tests/TextFix.Tests/Services/
git commit -m "feat: add AiClient with Anthropic SDK integration and CorrectionResult model"
```

---

### Task 5: Hotkey Listener

**Files:**
- Create: `src/TextFix/Services/HotkeyListener.cs`

- [ ] **Step 1: Implement HotkeyListener**

Create `src/TextFix/Services/HotkeyListener.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TextFix.Interop;

namespace TextFix.Services;

public class HotkeyListener : IDisposable
{
    private const int HotkeyId = 9000;
    private IntPtr _windowHandle;
    private HwndSource? _source;

    public event Action? HotkeyPressed;

    public bool Register(Window window, string hotkeyString)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        var (modifiers, vk) = ParseHotkey(hotkeyString);
        if (vk == 0) return false;

        return NativeMethods.RegisterHotKey(
            _windowHandle,
            HotkeyId,
            modifiers | NativeMethods.MOD_NOREPEAT,
            vk);
    }

    public void Unregister()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _source?.RemoveHook(HwndHook);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public static (uint modifiers, uint vk) ParseHotkey(string hotkeyString)
    {
        uint modifiers = 0;
        uint vk = 0;

        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "WIN":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var key))
                    {
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                    break;
            }
        }

        return (modifiers, vk);
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/HotkeyListener.cs
git commit -m "feat: add HotkeyListener with Win32 RegisterHotKey and configurable key parsing"
```

---

### Task 6: Focus Tracker

**Files:**
- Create: `src/TextFix/Services/FocusTracker.cs`

- [ ] **Step 1: Implement FocusTracker**

Create `src/TextFix/Services/FocusTracker.cs`:

```csharp
using TextFix.Interop;

namespace TextFix.Services;

public class FocusTracker
{
    private IntPtr _sourceWindow;

    public void CaptureSourceWindow()
    {
        _sourceWindow = NativeMethods.GetForegroundWindow();
    }

    public bool IsSourceWindowStillActive()
    {
        if (_sourceWindow == IntPtr.Zero)
            return false;

        if (!NativeMethods.IsWindow(_sourceWindow))
            return false;

        if (NativeMethods.IsIconic(_sourceWindow))
            return false;

        return NativeMethods.GetForegroundWindow() == _sourceWindow;
    }

    public IntPtr SourceWindow => _sourceWindow;
}
```

- [ ] **Step 2: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/FocusTracker.cs
git commit -m "feat: add FocusTracker for source window detection"
```

---

### Task 7: Clipboard Manager

**Files:**
- Create: `src/TextFix/Services/ClipboardManager.cs`

- [ ] **Step 1: Implement ClipboardManager**

Create `src/TextFix/Services/ClipboardManager.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using TextFix.Interop;

namespace TextFix.Services;

public class ClipboardManager
{
    private string? _savedClipboardText;
    private const int ClipboardRetryCount = 3;
    private const int ClipboardRetryDelayMs = 50;
    private const int PostKeystrokeDelayMs = 100;

    public async Task<string?> CaptureSelectedTextAsync()
    {
        _savedClipboardText = GetClipboardText();

        SimulateCtrlC();
        await Task.Delay(PostKeystrokeDelayMs);

        var newText = GetClipboardText();

        if (newText == _savedClipboardText || string.IsNullOrEmpty(newText))
            return null;

        return newText;
    }

    public async Task PasteTextAsync(string text)
    {
        SetClipboardText(text);
        await Task.Delay(50);
        SimulateCtrlV();
        await Task.Delay(PostKeystrokeDelayMs);
    }

    public void RestoreClipboard()
    {
        if (_savedClipboardText is not null)
        {
            try
            {
                SetClipboardText(_savedClipboardText);
            }
            catch
            {
                // Best effort — don't crash if restore fails
            }
        }
        _savedClipboardText = null;
    }

    public void SetClipboardText(string text)
    {
        for (int i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException) when (i < ClipboardRetryCount - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }
    }

    private string? GetClipboardText()
    {
        for (int i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException) when (i < ClipboardRetryCount - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }
        return null;
    }

    private static void SimulateCtrlC()
    {
        SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_C);
    }

    private static void SimulateCtrlV()
    {
        SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
    }

    private static void SendKeyCombo(ushort modifierVk, ushort keyVk)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            MakeKeyInput(modifierVk, false),
            MakeKeyInput(keyVk, false),
            MakeKeyInput(keyVk, true),
            MakeKeyInput(modifierVk, true),
        };

        NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/ClipboardManager.cs
git commit -m "feat: add ClipboardManager with capture, paste, and restore via SendInput"
```

---

### Task 8: Floating Overlay Window

**Files:**
- Create: `src/TextFix/Views/OverlayWindow.xaml`
- Create: `src/TextFix/Views/OverlayWindow.xaml.cs`

- [ ] **Step 1: Create OverlayWindow XAML**

Create `src/TextFix/Views/OverlayWindow.xaml`:

```xml
<Window x:Class="TextFix.Views.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ShowActivated="False"
        Focusable="False"
        SizeToContent="WidthAndHeight"
        ResizeMode="NoResize">

    <Window.Resources>
        <Style x:Key="PillBorder" TargetType="Border">
            <Setter Property="Background" Value="#E6 1A1A2E"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="Padding" Value="16"/>
            <Setter Property="Effect">
                <Setter.Value>
                    <DropShadowEffect BlurRadius="20" Opacity="0.4" ShadowDepth="4"/>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Border x:Name="MainBorder" Style="{StaticResource PillBorder}" MaxWidth="400">
        <StackPanel>
            <!-- Processing state: spinner + "Correcting..." -->
            <StackPanel x:Name="ProcessingPanel" Orientation="Horizontal" Visibility="Visible">
                <Ellipse x:Name="Spinner" Width="18" Height="18"
                         Stroke="#6C63FF" StrokeThickness="2"
                         StrokeDashArray="3 2" Margin="0,0,12,0">
                    <Ellipse.RenderTransform>
                        <RotateTransform CenterX="9" CenterY="9"/>
                    </Ellipse.RenderTransform>
                </Ellipse>
                <TextBlock Text="Correcting..." Foreground="#E0E0E0"
                           FontFamily="Segoe UI" FontSize="13" VerticalAlignment="Center"/>
            </StackPanel>

            <!-- Result state: diff + buttons -->
            <StackPanel x:Name="ResultPanel" Visibility="Collapsed">
                <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                    <TextBlock x:Name="StatusIcon" Text="&#x2713;" Foreground="#4ADE80"
                               FontSize="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                    <TextBlock x:Name="StatusText" Text="" Foreground="#E0E0E0"
                               FontFamily="Segoe UI" FontSize="13" VerticalAlignment="Center"/>
                </StackPanel>
                <TextBlock x:Name="OriginalText" Foreground="#AAAAAA"
                           FontFamily="Segoe UI" FontSize="12" TextWrapping="Wrap"
                           TextDecorations="Strikethrough" Margin="0,0,0,4"/>
                <TextBlock x:Name="CorrectedText" Foreground="#E0E0E0"
                           FontFamily="Segoe UI" FontSize="12" TextWrapping="Wrap"
                           Margin="0,0,0,12"/>
                <StackPanel Orientation="Horizontal">
                    <Border Background="#6C63FF" CornerRadius="4" Padding="4,2" Margin="0,0,8,0">
                        <TextBlock Text="Apply (Enter)" Foreground="White"
                                   FontFamily="Segoe UI" FontSize="11"/>
                    </Border>
                    <Border Background="#444444" CornerRadius="4" Padding="4,2">
                        <TextBlock Text="Cancel (Esc)" Foreground="#CCCCCC"
                                   FontFamily="Segoe UI" FontSize="11"/>
                    </Border>
                </StackPanel>
                <!-- Auto-apply countdown -->
                <TextBlock x:Name="CountdownText" Foreground="#666666"
                           FontFamily="Segoe UI" FontSize="10" Margin="0,6,0,0"/>
            </StackPanel>

            <!-- Error state -->
            <StackPanel x:Name="ErrorPanel" Orientation="Horizontal" Visibility="Collapsed">
                <TextBlock Text="&#x26A0;" Foreground="#FBBF24"
                           FontSize="16" Margin="0,0,12,0" VerticalAlignment="Center"/>
                <TextBlock x:Name="ErrorText" Foreground="#E0E0E0"
                           FontFamily="Segoe UI" FontSize="13" VerticalAlignment="Center"
                           TextWrapping="Wrap" MaxWidth="300"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Create OverlayWindow code-behind**

Create `src/TextFix/Views/OverlayWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TextFix.Interop;
using TextFix.Models;

namespace TextFix.Views;

public partial class OverlayWindow : Window
{
    private DispatcherTimer? _autoApplyTimer;
    private DispatcherTimer? _spinnerTimer;
    private int _countdownSeconds;
    private CorrectionResult? _currentResult;

    public event Action<bool>? UserResponded; // true = apply, false = cancel

    public OverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public void ShowProcessing()
    {
        ProcessingPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        PositionNearCursor();
        StartSpinnerAnimation();
        Show();
    }

    public void ShowResult(CorrectionResult result, int autoApplySeconds)
    {
        _currentResult = result;
        StopSpinnerAnimation();

        if (result.IsError)
        {
            ProcessingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = result.ErrorMessage;

            StartAutoClose(3);
            return;
        }

        if (!result.HasChanges)
        {
            ProcessingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = "No corrections needed.";

            StartAutoClose(2);
            return;
        }

        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        var changeCount = CountChanges(result.OriginalText, result.CorrectedText);
        StatusText.Text = $"Fixed {changeCount} error{(changeCount == 1 ? "" : "s")}";
        OriginalText.Text = result.OriginalText;
        CorrectedText.Text = result.CorrectedText;

        if (autoApplySeconds > 0)
            StartAutoApplyCountdown(autoApplySeconds);
    }

    public void ShowFocusLost()
    {
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = "Focus changed — Ctrl+V to paste";

        StartAutoClose(3);
    }

    private void PositionNearCursor()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            // Offset slightly so overlay doesn't cover the cursor
            Left = point.X + 10;
            Top = point.Y + 20;

            // Clamp to screen bounds
            var screen = SystemParameters.WorkArea;
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
            UserResponded?.Invoke(true);
            Hide();
        }
        else if (e.Key == Key.Escape)
        {
            StopAutoApply();
            UserResponded?.Invoke(false);
            Hide();
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
                Hide();
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
        // Simple word-level diff count
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
```

- [ ] **Step 3: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/TextFix/Views/OverlayWindow.xaml src/TextFix/Views/OverlayWindow.xaml.cs
git commit -m "feat: add floating overlay window with processing, result, and error states"
```

---

### Task 9: Correction Service (Pipeline Orchestrator)

**Files:**
- Create: `src/TextFix/Services/CorrectionService.cs`

- [ ] **Step 1: Implement CorrectionService**

Create `src/TextFix/Services/CorrectionService.cs`:

```csharp
using TextFix.Models;

namespace TextFix.Services;

public class CorrectionService
{
    private readonly ClipboardManager _clipboard;
    private readonly FocusTracker _focusTracker;
    private readonly AiClient _aiClient;
    private CancellationTokenSource? _cts;

    public CorrectionResult? LastResult { get; private set; }

    public CorrectionService(ClipboardManager clipboard, FocusTracker focusTracker, AiClient aiClient)
    {
        _clipboard = clipboard;
        _focusTracker = focusTracker;
        _aiClient = aiClient;
    }

    public event Action? ProcessingStarted;
    public event Action<CorrectionResult>? CorrectionCompleted;
    public event Action? FocusLost;
    public event Action<string>? ErrorOccurred;

    public async Task TriggerCorrectionAsync()
    {
        Cancel();
        _cts = new CancellationTokenSource();

        _focusTracker.CaptureSourceWindow();

        var selectedText = await _clipboard.CaptureSelectedTextAsync();
        if (selectedText is null)
        {
            ErrorOccurred?.Invoke("No text selected.");
            return;
        }

        ProcessingStarted?.Invoke();

        var result = await _aiClient.CorrectAsync(selectedText, _cts.Token);
        LastResult = result;
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
```

- [ ] **Step 2: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TextFix/Services/CorrectionService.cs
git commit -m "feat: add CorrectionService orchestrating capture, AI correction, and paste-back"
```

---

### Task 10: Settings Window

**Files:**
- Create: `src/TextFix/Views/SettingsWindow.xaml`
- Create: `src/TextFix/Views/SettingsWindow.xaml.cs`

- [ ] **Step 1: Create SettingsWindow XAML**

Create `src/TextFix/Views/SettingsWindow.xaml`:

```xml
<Window x:Class="TextFix.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TextFix Settings"
        Width="450" Height="400"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        Background="#1E1E2E"
        Foreground="#E0E0E0"
        FontFamily="Segoe UI">

    <Window.Resources>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#2D2D3F"/>
            <Setter Property="Foreground" Value="#E0E0E0"/>
            <Setter Property="BorderBrush" Value="#444"/>
            <Setter Property="Padding" Value="6,4"/>
            <Setter Property="Margin" Value="0,4,0,12"/>
        </Style>
        <Style TargetType="Label">
            <Setter Property="Foreground" Value="#AAAAAA"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Margin" Value="0,0,0,2"/>
            <Setter Property="FontSize" Value="12"/>
        </Style>
    </Window.Resources>

    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="TextFix Settings" FontSize="18" FontWeight="SemiBold"
                   Foreground="#E0E0E0" Margin="0,0,0,20"/>

        <StackPanel Grid.Row="1">
            <Label Content="API Key"/>
            <TextBox x:Name="ApiKeyBox"/>
        </StackPanel>

        <StackPanel Grid.Row="2">
            <Label Content="Hotkey (e.g., Ctrl+Shift+C)"/>
            <TextBox x:Name="HotkeyBox"/>
        </StackPanel>

        <StackPanel Grid.Row="3">
            <Label Content="Auto-apply delay (seconds, 0 = never)"/>
            <TextBox x:Name="AutoApplyBox"/>
        </StackPanel>

        <StackPanel Grid.Row="4">
            <Label Content="Model"/>
            <TextBox x:Name="ModelBox"/>
        </StackPanel>

        <StackPanel Grid.Row="6" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="SaveButton" Content="Save" Width="80" Height="30"
                    Background="#6C63FF" Foreground="White" BorderBrush="Transparent"
                    FontSize="13" Cursor="Hand" Click="OnSave" Margin="0,0,8,0"/>
            <Button x:Name="CancelButton" Content="Cancel" Width="80" Height="30"
                    Background="#444" Foreground="#CCC" BorderBrush="Transparent"
                    FontSize="13" Cursor="Hand" Click="OnCancel"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create SettingsWindow code-behind**

Create `src/TextFix/Views/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using TextFix.Models;

namespace TextFix.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public bool SettingsChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ApiKeyBox.Text = settings.ApiKey;
        HotkeyBox.Text = settings.Hotkey;
        AutoApplyBox.Text = settings.OverlayAutoApplySeconds.ToString();
        ModelBox.Text = settings.Model;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.ApiKey = ApiKeyBox.Text.Trim();
        _settings.Hotkey = HotkeyBox.Text.Trim();
        _settings.Model = ModelBox.Text.Trim();

        if (int.TryParse(AutoApplyBox.Text.Trim(), out var delay) && delay >= 0)
            _settings.OverlayAutoApplySeconds = delay;

        await _settings.SaveAsync();
        SettingsChanged = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/TextFix/Views/SettingsWindow.xaml src/TextFix/Views/SettingsWindow.xaml.cs
git commit -m "feat: add Settings window with API key, hotkey, model, and auto-apply config"
```

---

### Task 11: App Shell (System Tray + Wiring)

**Files:**
- Modify: `src/TextFix/App.xaml`
- Modify: `src/TextFix/App.xaml.cs`

- [ ] **Step 1: Update App.xaml to remove StartupUri**

Replace the contents of `src/TextFix/App.xaml` with:

```xml
<Application x:Class="TextFix.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources/>
</Application>
```

- [ ] **Step 2: Implement App.xaml.cs with full wiring**

Replace the contents of `src/TextFix/App.xaml.cs` with:

```csharp
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using TextFix.Models;
using TextFix.Services;
using TextFix.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TextFix;

public partial class App : Application
{
    private static Mutex? _mutex;
    private NotifyIcon? _trayIcon;
    private HotkeyListener? _hotkeyListener;
    private CorrectionService? _correctionService;
    private OverlayWindow? _overlay;
    private AppSettings _settings = new();

    // Hidden window needed for hotkey message pump
    private Window? _hiddenWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "TextFix_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("TextFix is already running.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Global exception handler
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        _settings = await AppSettings.LoadAsync();

        CreateHiddenWindow();
        SetupTrayIcon();
        SetupOverlay();
        RegisterHotkey();

        // Prompt for API key on first run
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            OpenSettings();
    }

    private void CreateHiddenWindow()
    {
        _hiddenWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = $"TextFix ({_settings.Hotkey})",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
    }

    private void SetupOverlay()
    {
        _overlay = new OverlayWindow();
        _overlay.UserResponded += OnUserResponded;
    }

    private void RegisterHotkey()
    {
        _hotkeyListener?.Dispose();
        _hotkeyListener = new HotkeyListener();
        _hotkeyListener.HotkeyPressed += OnHotkeyPressed;

        if (_hiddenWindow is null) return;

        if (!_hotkeyListener.Register(_hiddenWindow, _settings.Hotkey))
        {
            _trayIcon?.ShowBalloonTip(
                3000,
                "TextFix",
                $"Could not register hotkey {_settings.Hotkey}. It may be in use by another app. Click the tray icon to change it.",
                ToolTipIcon.Warning);
        }
    }

    private async void OnHotkeyPressed()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(
                CorrectionResult.Error("", "Set up your API key in Settings."),
                0);
            return;
        }

        try
        {
            var aiClient = new AiClient(_settings);
            var clipboard = new ClipboardManager();
            var focusTracker = new FocusTracker();
            _correctionService = new CorrectionService(clipboard, focusTracker, aiClient);

            _correctionService.ProcessingStarted += () =>
                Dispatcher.Invoke(() => _overlay?.ShowProcessing());

            _correctionService.CorrectionCompleted += result =>
                Dispatcher.Invoke(() => _overlay?.ShowResult(result, _settings.OverlayAutoApplySeconds));

            _correctionService.ErrorOccurred += msg =>
                Dispatcher.Invoke(() =>
                {
                    _overlay?.ShowProcessing();
                    _overlay?.ShowResult(CorrectionResult.Error("", msg), 0);
                });

            _correctionService.FocusLost += () =>
                Dispatcher.Invoke(() => _overlay?.ShowFocusLost());

            await _correctionService.TriggerCorrectionAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(CorrectionResult.Error("", $"Error: {ex.Message}"), 0);
        }
    }

    private async void OnUserResponded(bool apply)
    {
        if (_correctionService is null) return;

        if (apply && _correctionService.LastResult is not null)
        {
            await _correctionService.ApplyCorrectionAsync(_correctionService.LastResult);
        }
        else
        {
            _correctionService.CancelAndRestore();
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        window.ShowDialog();
        if (window.SettingsChanged)
        {
            RegisterHotkey();
            if (_trayIcon is not null)
                _trayIcon.Text = $"TextFix ({_settings.Hotkey})";
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyListener?.Dispose();
        _trayIcon?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TextFix");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:o}] {ex}\n\n");
        }
        catch { /* best effort */ }
    }
}
```

- [ ] **Step 3: Add WinForms reference for NotifyIcon**

Edit `src/TextFix/TextFix.csproj` to add WinForms support (needed for `NotifyIcon` and `ContextMenuStrip`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationIcon />
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Anthropic" Version="12.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Verify build**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet build src/TextFix
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TextFix/App.xaml src/TextFix/App.xaml.cs src/TextFix/TextFix.csproj
git commit -m "feat: wire up App shell with system tray, hotkey, overlay, and full correction pipeline"
```

---

### Task 12: Delete Scaffolded MainWindow

**Files:**
- Delete: `src/TextFix/MainWindow.xaml`
- Delete: `src/TextFix/MainWindow.xaml.cs`

- [ ] **Step 1: Remove the template MainWindow files**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
rm -f src/TextFix/MainWindow.xaml src/TextFix/MainWindow.xaml.cs
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/TextFix
```

Expected: Build succeeded (App.xaml has no StartupUri referencing MainWindow).

- [ ] **Step 3: Commit**

```bash
git add -A src/TextFix/MainWindow.xaml src/TextFix/MainWindow.xaml.cs
git commit -m "chore: remove scaffolded MainWindow (app runs from system tray)"
```

---

### Task 13: End-to-End Manual Test

**Files:** None (manual testing)

- [ ] **Step 1: Run the app**

```bash
cd /c/Users/swell/Documents/GitHub/TextFix
dotnet run --project src/TextFix
```

Expected: App starts, system tray icon appears, settings window opens (no API key yet).

- [ ] **Step 2: Configure API key**

Enter your Anthropic API key in the settings window and save.

- [ ] **Step 3: Test the correction flow**

1. Open Notepad (or any text editor)
2. Type: `i wouldlike t obuild ana app tha thelp me fix text`
3. Select all text (Ctrl+A)
4. Press Ctrl+Shift+C
5. Verify: overlay appears with spinner, then shows diff with corrected text
6. Press Enter to apply
7. Verify: text in Notepad is replaced with corrected version

- [ ] **Step 4: Test edge cases**

1. Press Ctrl+Shift+C with no text selected — should show "No text selected"
2. Switch to another app during processing — should show "Focus changed — Ctrl+V to paste"
3. Press Escape on the diff toast — should cancel and restore clipboard
4. Wait for auto-apply countdown — should apply automatically

- [ ] **Step 5: Test settings**

1. Right-click tray icon → Settings
2. Change hotkey to something else (e.g., Ctrl+Shift+F)
3. Save and verify the new hotkey works
4. Change it back

- [ ] **Step 6: Commit any fixes discovered during testing**

```bash
git add -A
git commit -m "fix: adjustments from end-to-end testing"
```

---

### Task 14: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md with actual project structure**

Update `CLAUDE.md` to reflect the implemented architecture, actual file paths, and build/test commands now that the project exists.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with implemented project structure"
```
