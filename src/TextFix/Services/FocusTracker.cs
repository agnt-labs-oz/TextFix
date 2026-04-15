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
