using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TextFix.Interop;
using Clipboard = System.Windows.Clipboard;

namespace TextFix.Services;

public class ClipboardManager
{
    private string? _savedClipboardText;
    private IntPtr _sourceWindow;
    private const int ClipboardRetryCount = 3;
    private const int ClipboardRetryDelayMs = 50;
    private const int PostKeystrokeDelayMs = 100;

    public void SetSourceWindow(IntPtr hwnd) => _sourceWindow = hwnd;

    public async Task<string?> CaptureSelectedTextAsync()
    {
        _savedClipboardText = GetClipboardText();
        Log($"Saved clipboard: '{Truncate(_savedClipboardText)}'");

        // Wait for modifier keys to be physically released by the user
        await WaitForModifierKeysReleased();
        Log("Modifier keys released");

        // Check which window has focus
        var fgBefore = NativeMethods.GetForegroundWindow();
        Log($"Foreground window before Ctrl+C: {fgBefore}");

        // Ensure the source window still has focus (hotkey processing may have shifted it)
        if (_sourceWindow != IntPtr.Zero && fgBefore != _sourceWindow)
        {
            NativeMethods.SetForegroundWindow(_sourceWindow);
            await Task.Delay(50);
            Log($"Restored focus to source window: {_sourceWindow}");
        }

        // Clear clipboard so we can detect if Ctrl+C worked
        try { Clipboard.Clear(); } catch { }
        await Task.Delay(30);

        // Try Ctrl+C up to 3 times with increasing delay
        string? newText = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            SimulateCtrlC();
            Log($"Sent Ctrl+C (attempt {attempt})");
            await Task.Delay(PostKeystrokeDelayMs * attempt);

            newText = GetClipboardText();
            Log($"Clipboard after Ctrl+C attempt {attempt}: '{Truncate(newText)}'");

            if (!string.IsNullOrEmpty(newText))
                break;

            // Clear and retry
            if (attempt < 3)
            {
                try { Clipboard.Clear(); } catch { }
                await Task.Delay(50);
            }
        }

        if (string.IsNullOrEmpty(newText))
        {
            Log("No text captured — clipboard empty after all attempts");
            return null;
        }

        return newText;
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextFix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    private static string? Truncate(string? s) =>
        s is null ? "(null)" : s.Length > 50 ? s[..50] + "..." : s;

    public async Task PasteTextAsync(string text)
    {
        SetClipboardText(text);
        await WaitForModifierKeysReleased();
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

    private static async Task WaitForModifierKeysReleased()
    {
        // Poll until Ctrl, Shift, and Alt are all physically released
        // GetAsyncKeyState returns negative (high bit set) if key is currently down
        const int maxWaitMs = 1000;
        int waited = 0;
        while (waited < maxWaitMs)
        {
            bool ctrlDown = NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) < 0;
            bool shiftDown = NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) < 0;
            bool altDown = NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) < 0;

            if (!ctrlDown && !shiftDown && !altDown)
                break;

            await Task.Delay(20);
            waited += 20;
        }
        // Small extra delay for input queue to settle
        await Task.Delay(30);
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

        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        Log($"SendInput: requested {inputs.Length}, sent {sent}, lastError={Marshal.GetLastWin32Error()}");
    }

    private static NativeMethods.INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        // MapVirtualKey translates VK to hardware scan code for better compatibility
        uint scan = NativeMethods.MapVirtualKey(vk, 0);
        return new NativeMethods.INPUT
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)scan,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };
    }
}
