using System.Windows;
using System.Windows.Controls;
using TextFix.Models;
using TextFix.Services;
using TextFix.Services.Providers;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;

namespace TextFix.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CorrectionHistory? _history;
    private readonly StatsTracker? _statsTracker;
    private bool _keyVisible;

    public bool SettingsChanged { get; private set; }
    public bool HistoryCleared { get; private set; }

    public SettingsWindow(
        AppSettings settings, CorrectionHistory? history = null, StatsTracker? statsTracker = null)
    {
        InitializeComponent();
        _settings = settings;
        _history = history;
        _statsTracker = statsTracker;

        HotkeyBox.Text = settings.Hotkey;

        foreach (var preset in ProviderPresets.All)
            ProviderBox.Items.Add(new ComboBoxItem { Content = preset.DisplayName, Tag = preset.Id });
        SelectProvider(settings.ActiveProviderId);

        RefreshModeBox();

        AutoApplyBox.Text = settings.OverlayAutoApplySeconds.ToString();
        ManualOnlyBox.IsChecked = settings.ManualApplyOnly;
        UpdateAutoApplyEnabled();

        // Reflect the live registry state, not just the saved setting — these can diverge
        // if a cleanup tool, group policy, or the user edited HKCU directly. The checkbox
        // should match what Windows will actually do at next sign-in.
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();

        HistoryMaxBox.Text = settings.HistoryMaxItems.ToString();
        // When opened standalone (no live history), clearing makes no sense — disable.
        ClearHistoryButton.IsEnabled = _history is not null;

        PopulateCustomModesList();
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_history is null) return;

        var confirm = WpfMessageBox.Show(
            App.HistoryWipeWarning,
            "TextFix — Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        _history.Clear();
        try
        {
            await _history.SaveAsync();
            // Lifetime stats live in their own file. Wiping only the history left the
            // About window still reporting every correction the user just erased.
            if (_statsTracker is not null)
                await _statsTracker.ClearAsync();
        }
        catch (Exception ex)
        {
            // Say what survived. "History cleared" over a failed wipe is the worse bug.
            WpfMessageBox.Show($"History could not be fully erased: {ex.Message}",
                "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
            HistoryCleared = true;
            HistoryStatusText.Text = "History partly cleared — see the warning.";
            HistoryStatusText.Visibility = Visibility.Visible;
            return;
        }
        HistoryCleared = true;
        HistoryStatusText.Text = "History cleared. Time-saved total kept.";
        HistoryStatusText.Visibility = Visibility.Visible;
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

    private string CurrentProviderId =>
        (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? ProviderPresets.AnthropicId;

    private void OnSetupOllama(object sender, RoutedEventArgs e)
    {
        // Resolve from the live field, not the saved config: if the user has just
        // edited the Base URL, the helper must manage THAT server. Falls back to the
        // preset exactly as the provider would.
        var preset = ProviderPresets.Get(ProviderPresets.OllamaId);
        var baseUrl = BaseUrlBox.Text.Trim();
        var effectiveUrl = string.IsNullOrWhiteSpace(baseUrl) ? preset.BaseUrl : baseUrl;

        var dialog = new OllamaSetupDialog(effectiveUrl) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.ReadyModel is not null)
        {
            // Only fill an empty model box — a model the user chose deliberately must
            // not be overwritten by whatever the helper happened to end up with.
            if (string.IsNullOrWhiteSpace(ModelBox.Text))
                ModelBox.Text = dialog.ReadyModel;
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
            ConnectionStatusText.Text = $"Ollama is ready with {dialog.ReadyModel}.";
        }
    }

    private void SelectProvider(string id)
    {
        for (var i = 0; i < ProviderBox.Items.Count; i++)
        {
            if (ProviderBox.Items[i] is ComboBoxItem item && (string)item.Tag == id)
            {
                ProviderBox.SelectedIndex = i;
                return;
            }
        }
        ProviderBox.SelectedIndex = 0;
    }

    /// <summary>Persists whatever is in the fields to the config for <paramref name="id"/>.</summary>
    /// <remarks>
    /// Values equal to the preset's are stored as "" rather than as literals. LoadFieldsFrom
    /// resolves "" to the preset for display, so writing the resolved value straight back
    /// would pin it on the first Settings visit and a later preset change — a new default
    /// model, a moved endpoint — would never reach existing users.
    /// </remarks>
    private void StoreFieldsInto(string id)
    {
        var preset = ProviderPresets.Get(id);
        var config = _settings.GetProviderConfig(id);

        var baseUrl = BaseUrlBox.Text.Trim();
        config.BaseUrl = baseUrl == preset.BaseUrl ? "" : baseUrl;

        var model = (ModelBox.Text ?? "").Trim();
        config.Model = model == preset.DefaultModel ? "" : model;

        var key = _keyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        config.SetApiKey(key);
    }

    private void LoadFieldsFrom(string id)
    {
        var preset = ProviderPresets.Get(id);
        var config = _settings.GetProviderConfig(id);

        BaseUrlBox.Text = string.IsNullOrWhiteSpace(config.BaseUrl) ? preset.BaseUrl : config.BaseUrl;
        ApiKeyBox.Password = config.GetApiKey();
        ApiKeyTextBox.Text = "";
        _keyVisible = false;
        ApiKeyBox.Visibility = Visibility.Visible;
        ApiKeyTextBox.Visibility = Visibility.Collapsed;

        ModelBox.Items.Clear();
        if (!preset.IsOpenAiCompatible)
        {
            foreach (var m in AnthropicProvider.KnownModels)
                ModelBox.Items.Add(m);
        }
        ModelBox.Text = string.IsNullOrWhiteSpace(config.Model) ? preset.DefaultModel : config.Model;

        // Anthropic goes through its SDK: no base URL to edit, and its ListModelsAsync
        // is a static list, so a connection test that cannot fail would be a lie.
        BaseUrlPanel.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        TestConnectionPanel.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        RefreshModelsButton.Visibility = preset.IsOpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = preset.Key == KeyRequirement.None ? Visibility.Collapsed : Visibility.Visible;
        // The setup helper is Ollama-specific: it downloads Ollama's installer and
        // talks to Ollama's native /api endpoints, neither of which a generic
        // OpenAI-compatible endpoint has.
        SetupOllamaButton.Visibility = preset.Id == ProviderPresets.OllamaId
            ? Visibility.Visible : Visibility.Collapsed;

        ConnectionStatusText.Text = "";
    }

    private string? _loadedProviderId;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded && _loadedProviderId is null)
        {
            _loadedProviderId = CurrentProviderId;
            LoadFieldsFrom(_loadedProviderId);
            return;
        }

        // Keep the outgoing provider's edits before repainting for the incoming one.
        if (_loadedProviderId is not null)
            StoreFieldsInto(_loadedProviderId);

        _loadedProviderId = CurrentProviderId;
        LoadFieldsFrom(_loadedProviderId);
    }

    /// <summary>
    /// Disables both network buttons for the duration of a lookup. Listing models can take
    /// many seconds against a cold local endpoint, and these are async void handlers with no
    /// natural re-entrancy guard — without this a user can queue several and whichever
    /// returns last wins the status line, which may not be the one they last clicked.
    /// </summary>
    private void SetNetworkButtonsEnabled(bool enabled)
    {
        RefreshModelsButton.IsEnabled = enabled;
        TestConnectionButton.IsEnabled = enabled;
        // The provider dropdown has to freeze too. Switching mid-lookup runs
        // LoadFieldsFrom for the incoming provider, and the continuation then writes the
        // OUTGOING provider's model list and "Connected — N models" into its fields.
        ProviderBox.IsEnabled = enabled;
    }

    private async void OnRefreshModels(object sender, RoutedEventArgs e)
    {
        StoreFieldsInto(CurrentProviderId);
        SetNetworkButtonsEnabled(false);
        try
        {
            var models = await TryListModelsAsync();
            if (models is null) return;

            var current = ModelBox.Text;
            ModelBox.Items.Clear();
            foreach (var m in models) ModelBox.Items.Add(m);
            ModelBox.Text = models.Contains(current) ? current : models.FirstOrDefault() ?? "";
        }
        finally
        {
            SetNetworkButtonsEnabled(true);
        }
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        StoreFieldsInto(CurrentProviderId);
        SetNetworkButtonsEnabled(false);
        try
        {
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            ConnectionStatusText.Text = "Testing…";

            var models = await TryListModelsAsync();
            if (models is null) return;

            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
            ConnectionStatusText.Text = $"Connected — {models.Count} model{(models.Count == 1 ? "" : "s")}";
        }
        finally
        {
            SetNetworkButtonsEnabled(true);
        }
    }

    /// <summary>
    /// Lists models for the current provider, painting the failure into the status
    /// line and returning null. Never throws.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TryListModelsAsync()
    {
        var preset = ProviderPresets.Get(CurrentProviderId);
        var config = _settings.GetProviderConfig(preset.Id);

        // Resolve through the preset, exactly as the provider does. A stored "" means
        // "use the preset's" — not "blank" — so testing the raw field here would wrongly
        // reject Ollama, whose configured URL is normally left at the preset default.
        var effectiveUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? preset.BaseUrl : config.BaseUrl;

        // Say what to do rather than letting an empty URL fall through to SendAsync, where
        // it surfaces as a UriFormatException the user cannot act on. Custom is the provider
        // that ships with no default Base URL, so it is the one that lands here.
        if (string.IsNullOrWhiteSpace(effectiveUrl))
        {
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Goldenrod;
            ConnectionStatusText.Text = "Enter a Base URL first.";
            return null;
        }

        try
        {
            var provider = new OpenAiCompatibleProvider(
                preset, effectiveUrl, config.Model, config.GetApiKey());
            var models = await provider.ListModelsAsync();
            if (models.Count == 0)
            {
                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Goldenrod;
                ConnectionStatusText.Text = preset.Id == ProviderPresets.OllamaId
                    ? "Reachable, but no models pulled. Run: ollama pull llama3.2:3b"
                    : "Reachable, but the endpoint listed no models.";
                return null;
            }
            return models;
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            var url = effectiveUrl;
            ConnectionStatusText.Text = preset.Id == ProviderPresets.OllamaId
                ? $"Cannot reach {url} — is Ollama running? Install it from ollama.com"
                : $"Cannot reach {url} — {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Validates the endpoint fields for the selected provider, showing a message and
    /// returning false if they cannot work. Only OpenAI-compatible providers have an
    /// editable Base URL; Anthropic's is fixed by its SDK.
    /// </summary>
    private bool ValidateProviderFields()
    {
        var preset = ProviderPresets.Get(CurrentProviderId);

        // Presets carry an example only where one exists: Custom has no Base URL, and
        // neither Ollama nor Custom has a default model, because both depend entirely on
        // what the user has actually pulled or deployed. Never render a bare "Example: ".
        var urlHint = string.IsNullOrEmpty(preset.BaseUrl)
            ? "" : $" Example: {preset.BaseUrl}";
        var modelHint = string.IsNullOrEmpty(preset.DefaultModel)
            ? "" : $" Example: {preset.DefaultModel}";

        if (preset.IsOpenAiCompatible)
        {
            var baseUrl = BaseUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                WpfMessageBox.Show(
                    $"{preset.DisplayName} needs a Base URL.{urlHint}",
                    "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                WpfMessageBox.Show(
                    $"Base URL must be a full http:// or https:// address.{urlHint}",
                    "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            WpfMessageBox.Show(
                $"Choose a model for {preset.DisplayName}.{modelHint}",
                "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
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

        if (!ValidateProviderFields()) return;

        StoreFieldsInto(CurrentProviderId);
        _settings.ActiveProviderId = CurrentProviderId;
        _settings.Hotkey = hotkeyText;
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

        var startWithWindows = StartWithWindowsBox.IsChecked == true;
        _settings.StartWithWindows = startWithWindows;
        try
        {
            StartupRegistration.Apply(startWithWindows);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Could not update Windows startup setting: {ex.Message}\n\nOther settings will still be saved.",
                "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var historyText = HistoryMaxBox.Text.Trim();
        if (!int.TryParse(historyText, out var historyMax) || historyMax < 1)
        {
            WpfMessageBox.Show($"History limit must be between 1 and {CorrectionHistory.MaxItemsCap}.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.HistoryMaxItems = Math.Min(historyMax, CorrectionHistory.MaxItemsCap);

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
