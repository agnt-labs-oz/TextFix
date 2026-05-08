using System.Diagnostics;
using System.Reflection;
using System.Windows;
using TextFix.Models;
using TextFix.Services;

namespace TextFix.Views;

public partial class AboutWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string LicenseUrl = "https://github.com/agnt-labs-oz/TextFix/blob/master/LICENSE";

    private readonly StatsTracker _statsTracker;

    public AboutWindow(StatsTracker statsTracker)
    {
        _statsTracker = statsTracker;
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? "?"}";
        Loaded += async (_, _) => await LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        StatsAggregate agg;
        try { agg = await _statsTracker.AggregateAsync(); }
        catch { agg = new StatsAggregate(); }

        LifetimeText.Text = agg.LifetimeCorrections.ToString("N0");
        TimeSavedText.Text = FormatMinutes(agg.TimeSavedMinutes);
        MostUsedModeText.Text = agg.MostUsedMode is null
            ? "–"
            : $"{agg.MostUsedMode} ({Pct(agg)}%)";
        MonthCostText.Text = $"${agg.MonthCostUsd:0.00}";
        PerModeList.ItemsSource = agg.PerMode
            .OrderByDescending(kv => kv.Value)
            .ToList();
    }

    private static int Pct(StatsAggregate agg)
    {
        if (agg.LifetimeCorrections == 0 || agg.MostUsedMode is null) return 0;
        return (int)Math.Round(100.0 * agg.PerMode[agg.MostUsedMode] / agg.LifetimeCorrections);
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1.0) return "< 1m";
        var ts = TimeSpan.FromMinutes(minutes);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
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
