using Microsoft.Win32;

namespace TextFix.Services;

/// <summary>
/// Manages the HKCU\...\Run entry that auto-launches TextFix at Windows sign-in.
/// HKCU (not HKLM) so no admin rights are needed and the setting follows the user across machines.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TextFix";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Apply(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException(@"Could not open HKCU\" + RunKey);

        if (enable)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine executable path for autostart.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
