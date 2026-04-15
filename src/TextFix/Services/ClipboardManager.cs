using System.Runtime.InteropServices;
using System.Windows;
using TextFix.Interop;
using Clipboard = System.Windows.Clipboard;

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
