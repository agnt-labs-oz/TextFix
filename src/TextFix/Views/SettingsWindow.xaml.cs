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

        ApiKeyBox.Password = settings.GetApiKey();
        HotkeyBox.Text = settings.Hotkey;
        AutoApplyBox.Text = settings.OverlayAutoApplySeconds.ToString();
        ModelBox.Text = settings.Model;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.SetApiKey(ApiKeyBox.Password.Trim());
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
