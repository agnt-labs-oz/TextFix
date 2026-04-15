using System.Windows;
using TextFix.Models;

namespace TextFix.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _keyVisible;

    public static readonly string[] KnownModels =
    [
        "claude-haiku-4-5-20251001",
        "claude-sonnet-4-5-20250514",
        "claude-sonnet-4-6",
        "claude-opus-4-6",
    ];

    private static readonly (string Label, int Seconds)[] AutoApplyOptions =
    [
        ("Off (manual Enter/Esc)", 0),
        ("3 seconds", 3),
        ("5 seconds", 5),
        ("10 seconds", 10),
    ];

    public bool SettingsChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ApiKeyBox.Password = settings.GetApiKey();
        HotkeyBox.Text = settings.Hotkey;

        foreach (var model in KnownModels)
            ModelBox.Items.Add(model);
        ModelBox.SelectedItem = settings.Model;
        if (ModelBox.SelectedItem is null)
        {
            ModelBox.Items.Add(settings.Model);
            ModelBox.SelectedItem = settings.Model;
        }

        foreach (var mode in CorrectionMode.Defaults)
            ModeBox.Items.Add(mode.Name);
        ModeBox.SelectedItem = settings.ActiveModeName;

        int selectedIndex = 0;
        for (int i = 0; i < AutoApplyOptions.Length; i++)
        {
            AutoApplyBox.Items.Add(AutoApplyOptions[i].Label);
            if (AutoApplyOptions[i].Seconds == settings.OverlayAutoApplySeconds)
                selectedIndex = i;
        }
        AutoApplyBox.SelectedIndex = selectedIndex;

        KeepOverlayOpenBox.IsChecked = settings.KeepOverlayOpen;
    }

    private void OnToggleKeyVisibility(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;
        if (_keyVisible)
        {
            ApiKeyTextBox.Text = ApiKeyBox.Password;
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            ApiKeyBox.Password = ApiKeyTextBox.Text;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyBox.Visibility = Visibility.Visible;
        }
    }

    private void OnCopyKey(object sender, RoutedEventArgs e)
    {
        var key = _keyVisible ? ApiKeyTextBox.Text : ApiKeyBox.Password;
        if (!string.IsNullOrEmpty(key))
            System.Windows.Clipboard.SetText(key);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var apiKey = _keyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        _settings.SetApiKey(apiKey);
        _settings.Hotkey = HotkeyBox.Text.Trim();
        _settings.Model = ModelBox.SelectedItem as string ?? _settings.Model;
        _settings.ActiveModeName = ModeBox.SelectedItem as string ?? _settings.ActiveModeName;

        var idx = AutoApplyBox.SelectedIndex;
        if (idx >= 0 && idx < AutoApplyOptions.Length)
            _settings.OverlayAutoApplySeconds = AutoApplyOptions[idx].Seconds;

        _settings.KeepOverlayOpen = KeepOverlayOpenBox.IsChecked == true;

        await _settings.SaveAsync();
        SettingsChanged = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
