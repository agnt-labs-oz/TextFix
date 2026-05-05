using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace TextFix.Views;

public partial class AboutWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string LicenseUrl = "https://github.com/agnt-labs-oz/TextFix/blob/master/LICENSE";

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? "?"}";
    }

    private void KofiButton_Click(object sender, RoutedEventArgs e) => OpenUrl(KofiUrl);
    private void GitHubLink_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    private void LicenseLink_Click(object sender, RoutedEventArgs e) => OpenUrl(LicenseUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
