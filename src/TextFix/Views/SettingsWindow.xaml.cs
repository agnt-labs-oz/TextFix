using System.Windows;
using TextFix.Models;

namespace TextFix.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public static readonly string[] KnownModels =
    [
        "claude-haiku-4-5-20251001",
        "claude-sonnet-4-5-20250514",
        "claude-sonnet-4-6",
        "claude-opus-4-6",
    ];

    public bool SettingsChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ApiKeyBox.Password = settings.GetApiKey();
        HotkeyBox.Text = settings.Hotkey;
        AutoApplyBox.Text = settings.OverlayAutoApplySeconds.ToString();

        // Populate model dropdown
        foreach (var model in KnownModels)
            ModelBox.Items.Add(model);
        ModelBox.SelectedItem = settings.Model;
        if (ModelBox.SelectedItem is null)
        {
            ModelBox.Items.Add(settings.Model);
            ModelBox.SelectedItem = settings.Model;
        }

        // Populate mode dropdown
        foreach (var mode in CorrectionMode.Defaults)
            ModeBox.Items.Add(mode.Name);
        ModeBox.SelectedItem = settings.ActiveModeName;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.SetApiKey(ApiKeyBox.Password.Trim());
        _settings.Hotkey = HotkeyBox.Text.Trim();
        _settings.Model = ModelBox.SelectedItem as string ?? _settings.Model;
        _settings.ActiveModeName = ModeBox.SelectedItem as string ?? _settings.ActiveModeName;

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
