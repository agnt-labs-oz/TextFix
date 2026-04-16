using System.IO;
using System.Windows.Input;
using System.Windows.Interop;
using TextFix.Interop;

namespace TextFix.Services;

public class HotkeyListener : IDisposable
{
    private const int HotkeyId = 9000;
    private HwndSource? _source;
    private IntPtr _handle;

    public event Action? HotkeyPressed;

    /// <summary>
    /// Creates a Win32 message-only window (HWND_MESSAGE parent) and registers
    /// the hotkey on it. Message-only windows exist purely for message processing
    /// and are not affected by WPF window activation or visibility changes.
    /// </summary>
    public bool Register(string hotkeyString)
    {
        if (_source is null)
        {
            var parameters = new HwndSourceParameters("TextFix_Hotkey")
            {
                Width = 0,
                Height = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _handle = _source.Handle;
            Log($"Created message-only window: {_handle}");
        }

        var (modifiers, vk) = ParseHotkey(hotkeyString);
        if (vk == 0)
        {
            Log($"Register failed: could not parse '{hotkeyString}'");
            return false;
        }

        // Unregister first in case it's already registered
        NativeMethods.UnregisterHotKey(_handle, HotkeyId);

        var result = NativeMethods.RegisterHotKey(
            _handle,
            HotkeyId,
            modifiers | NativeMethods.MOD_NOREPEAT,
            vk);
        Log($"Register('{hotkeyString}') mod=0x{modifiers:X} vk=0x{vk:X} → {result}");
        return result;
    }

    public void Unregister()
    {
        if (_handle != IntPtr.Zero)
        {
            Log("Unregister called");
            NativeMethods.UnregisterHotKey(_handle, HotkeyId);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Log("WM_HOTKEY received");
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
        _source = null;
        _handle = IntPtr.Zero;
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextFix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "debug.log"),
                $"[{DateTime.UtcNow:o}] [Hotkey] {message}\n");
        }
        catch { }
    }
}
