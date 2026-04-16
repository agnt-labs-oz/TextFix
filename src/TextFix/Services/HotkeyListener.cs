using System.IO;
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
        if (vk == 0)
        {
            Log($"Register failed: could not parse '{hotkeyString}'");
            return false;
        }

        var result = NativeMethods.RegisterHotKey(
            _windowHandle,
            HotkeyId,
            modifiers | NativeMethods.MOD_NOREPEAT,
            vk);
        Log($"Register('{hotkeyString}') mod=0x{modifiers:X} vk=0x{vk:X} → {result}");
        return result;
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
            Log("WM_HOTKEY received, firing HotkeyPressed");
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TextFix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "debug.log"),
                $"[{DateTime.UtcNow:o}] [Hotkey] {message}\n");
        }
        catch { }
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
