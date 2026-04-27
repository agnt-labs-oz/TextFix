using System.Windows;
using System.Windows.Controls;
using TextFix.Models;
using TextFix.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;

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
        "claude-sonnet-4-7",
        "claude-opus-4-6",
        "claude-opus-4-7",
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

        RefreshModeBox();

        AutoApplyBox.Text = settings.OverlayAutoApplySeconds.ToString();
        ManualOnlyBox.IsChecked = settings.ManualApplyOnly;
        UpdateAutoApplyEnabled();

        PopulateCustomModesList();
    }

    private void OnDigitsOnly(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch)) { e.Handled = true; return; }
        }
    }

    private void OnManualOnlyChanged(object sender, RoutedEventArgs e) => UpdateAutoApplyEnabled();

    private void UpdateAutoApplyEnabled()
    {
        var manual = ManualOnlyBox.IsChecked == true;
        AutoApplyBox.IsEnabled = !manual;
        AutoApplyBox.Opacity = manual ? 0.5 : 1.0;
    }

    private void RefreshModeBox()
    {
        var current = ModeBox.SelectedItem as string ?? _settings.ActiveModeName;
        ModeBox.Items.Clear();
        foreach (var mode in _settings.AllModes())
            ModeBox.Items.Add(mode.Name);
        ModeBox.SelectedItem = current;
        if (ModeBox.SelectedItem is null && ModeBox.Items.Count > 0)
            ModeBox.SelectedIndex = 0;
    }

    private void PopulateCustomModesList()
    {
        CustomModesList.Children.Clear();
        foreach (var mode in _settings.CustomModes)
        {
            var modeName = mode.Name; // capture for closure

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = modeName,
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(nameBlock, 0);

            var editBtn = new WpfButton
            {
                Content = "Edit",
                Width = 40,
                Height = 22,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = System.Windows.Media.Brushes.DimGray,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Margin = new Thickness(2, 0, 2, 0),
                Tag = modeName
            };
            editBtn.Click += OnEditCustomMode;
            Grid.SetColumn(editBtn, 1);

            var deleteBtn = new WpfButton
            {
                Content = "Del",
                Width = 36,
                Height = 22,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = System.Windows.Media.Brushes.IndianRed,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Margin = new Thickness(2, 0, 0, 0),
                Tag = modeName
            };
            deleteBtn.Click += OnDeleteCustomMode;
            Grid.SetColumn(deleteBtn, 2);

            row.Children.Add(nameBlock);
            row.Children.Add(editBtn);
            row.Children.Add(deleteBtn);

            CustomModesList.Children.Add(row);
        }
    }

    private void OnAddCustomMode(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomModeDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var name = dlg.ModeName;
        // Prevent duplicate names
        if (_settings.AllModes().Any(m => m.Name == name))
        {
            WpfMessageBox.Show($"A mode named \"{name}\" already exists.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.CustomModes.Add(new CorrectionMode { Name = name, SystemPrompt = dlg.ModePrompt });
        PopulateCustomModesList();
        RefreshModeBox();
    }

    private void OnEditCustomMode(object sender, RoutedEventArgs e)
    {
        var name = (sender as WpfButton)?.Tag as string;
        var mode = _settings.CustomModes.FirstOrDefault(m => m.Name == name);
        if (mode is null) return;

        var dlg = new CustomModeDialog(mode.Name, mode.SystemPrompt) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var newName = dlg.ModeName;
        // Check for name conflict (allow keeping same name)
        if (newName != name && _settings.AllModes().Any(m => m.Name == newName))
        {
            WpfMessageBox.Show($"A mode named \"{newName}\" already exists.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var idx = _settings.CustomModes.IndexOf(mode);
        _settings.CustomModes[idx] = new CorrectionMode { Name = newName, SystemPrompt = dlg.ModePrompt };

        // Update active mode name if it was renamed
        if (_settings.ActiveModeName == name)
            _settings.ActiveModeName = newName;

        PopulateCustomModesList();
        RefreshModeBox();
    }

    private void OnDeleteCustomMode(object sender, RoutedEventArgs e)
    {
        var name = (sender as WpfButton)?.Tag as string;
        var mode = _settings.CustomModes.FirstOrDefault(m => m.Name == name);
        if (mode is null) return;

        _settings.CustomModes.Remove(mode);

        if (_settings.ActiveModeName == name)
            _settings.ActiveModeName = CorrectionMode.Defaults[0].Name;

        PopulateCustomModesList();
        RefreshModeBox();
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
        var hotkeyText = HotkeyBox.Text.Trim();
        var (_, vk) = HotkeyListener.ParseHotkey(hotkeyText);
        if (vk == 0)
        {
            System.Windows.MessageBox.Show("Invalid hotkey format. Example: Ctrl+Shift+Z", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var apiKey = _keyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        _settings.SetApiKey(apiKey);
        _settings.Hotkey = hotkeyText;
        _settings.Model = ModelBox.SelectedItem as string ?? _settings.Model;
        _settings.ActiveModeName = ModeBox.SelectedItem as string ?? _settings.ActiveModeName;

        var autoApplyText = AutoApplyBox.Text.Trim();
        if (!int.TryParse(autoApplyText, out var seconds) || seconds < 0)
        {
            WpfMessageBox.Show("Auto-apply delay must be a non-negative integer (seconds).", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.OverlayAutoApplySeconds = Math.Min(seconds, 300);

        _settings.ManualApplyOnly = ManualOnlyBox.IsChecked == true;

        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save settings: {ex.Message}", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        SettingsChanged = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
