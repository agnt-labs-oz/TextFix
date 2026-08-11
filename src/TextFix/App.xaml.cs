using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using TextFix.Interop;
using TextFix.Models;
using TextFix.Services;
using TextFix.Services.Providers;
using TextFix.Views;
using Velopack;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TextFix;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack hooks (install/update/uninstall/firstrun) may exit the process before the WPF app starts.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private const string KofiUrl = "https://ko-fi.com/3smallwins";
    private const string GitHubRepoUrl = "https://github.com/agnt-labs-oz/TextFix";
    private const string GitHubNewIssueUrl =
        "https://github.com/agnt-labs-oz/TextFix/issues/new?template=bug_report.yml";
    private const string GitHubNewIdeaUrl =
        "https://github.com/agnt-labs-oz/TextFix/discussions/new?category=ideas";

    private static Mutex? _mutex;
    private static AppLog? _log;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _historyMenu;
    private ToolStripMenuItem? _providerMenu;
    private HotkeyListener? _hotkeyListener;
    private CorrectionService? _correctionService;
    private ProviderFactory? _providerFactory;
    private IAiProvider? _aiClient;
    private ClipboardManager? _clipboardManager;
    private FocusTracker? _focusTracker;
    private OverlayWindow? _overlay;
    private UpdateService? _updateService;
    private StatsTracker? _statsTracker;
    private AppSettings _settings = new();
    private int _isBusy;
    private System.Windows.Threading.DispatcherTimer? _keepAliveTimer;

    private Window? _hiddenWindow; // kept for WPF dispatcher pump

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check — include the user SID so a same-session low-integrity
        // process can't squat on the well-known name and lock us out.
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "anon";
        _mutex = new Mutex(true, $@"Local\TextFix_SingleInstance_{sid}", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("TextFix is already running.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Global exception handler
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        _settings = await AppSettings.LoadAsync();

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextFix", "logs");
        var level = Enum.TryParse<AppLog.Level>(_settings.LogLevel, ignoreCase: true, out var lvl)
            ? lvl : AppLog.Level.Warn;
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        // Passed as the session header too: this Info line is dropped at the default Warn
        // level, so on a normal install the header is the only thing identifying the build.
        _log = new AppLog(logDir, level, $"TextFix {version} started {DateTime.UtcNow:u}");
        _log.Info($"TextFix starting (version {version})");

        // Re-apply the autostart preference on every launch so the registry entry tracks the
        // current exe path (matters after a Velopack update if Environment.ProcessPath shifts)
        // and recovers if HKCU\...\Run\TextFix was removed externally.
        try { StartupRegistration.Apply(_settings.StartWithWindows); }
        catch (Exception ex) { _log.Warn($"Could not sync Windows startup entry: {ex.Message}"); }

        CreateHiddenWindow();
        SetupTrayIcon();
        SetupOverlay();
        RefreshProviderMenu();
        await SetupServicesAsync();
        RegisterHotkey();

        _updateService = new UpdateService();
        _ = CheckForUpdatesSilentAsync();

        // Prompt for setup on first run; otherwise show a brief tray balloon so the user
        // knows the app launched (without that, a normal start has zero visible feedback —
        // just an icon appearing in the system tray that's easy to miss).
        //
        // Gate on the active provider, not on the legacy top-level key. Settings now writes
        // credentials per provider and no longer touches AppSettings.ApiKey at all, so a
        // legacy-key check would trap an Ollama user in this dialog on every launch: they
        // have no API key, they need none, and nothing they can do in Settings would ever
        // satisfy it. _aiClient is null exactly when the chosen provider is missing
        // something it cannot run without — a required key, a base URL, or a model — which
        // is the real "not set up yet" condition. ProviderSetupMessage() names which.
        if (_aiClient is null)
        {
            OpenSettings();
        }
        else
        {
            _trayIcon?.ShowBalloonTip(
                2500,
                "TextFix is running",
                $"Select any text and press {_settings.Hotkey} to correct it.",
                ToolTipIcon.Info);
        }
    }

    private async Task CheckForUpdatesSilentAsync()
    {
        if (_updateService is null) return;
        var result = await _updateService.CheckAndDownloadAsync();
        if (result.State == UpdateState.Ready && result.Info is not null)
        {
            _updateService.ApplyOnExit(result.Info);
            Dispatcher.Invoke(() => _trayIcon?.ShowBalloonTip(
                4000,
                "TextFix",
                $"Update {result.Version} downloaded — will install when you exit TextFix.",
                ToolTipIcon.Info));
        }
    }

    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        if (_updateService is null) return;
        _trayIcon?.ShowBalloonTip(2000, "TextFix", "Checking for updates…", ToolTipIcon.Info);
        var result = await _updateService.CheckAndDownloadAsync();
        switch (result.State)
        {
            case UpdateState.NotInstalled:
                _trayIcon?.ShowBalloonTip(3000, "TextFix",
                    "Updates only work for installed builds. Run the Setup.exe from GitHub Releases.",
                    ToolTipIcon.Info);
                break;
            case UpdateState.UpToDate:
                _trayIcon?.ShowBalloonTip(3000, "TextFix",
                    $"You're on the latest version ({result.Version}).", ToolTipIcon.Info);
                break;
            case UpdateState.Ready when result.Info is not null:
                _updateService.ApplyOnExit(result.Info);
                _trayIcon?.ShowBalloonTip(4000, "TextFix",
                    $"Update {result.Version} downloaded — will install when you exit TextFix.",
                    ToolTipIcon.Info);
                break;
            case UpdateState.Error:
                _trayIcon?.ShowBalloonTip(3000, "TextFix",
                    $"Update check failed: {result.Error}", ToolTipIcon.Warning);
                break;
        }
    }

    private void CreateHiddenWindow()
    {
        _hiddenWindow = new Window
        {
            Width = 0, Height = 0,
            Left = -9999, Top = -9999,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        _hiddenWindow.Show();

        // WPF's dispatcher can stop pumping Win32 messages when no visible window
        // is active (after overlay hides). A periodic timer tick forces the
        // dispatcher to keep running, ensuring WM_HOTKEY messages get delivered.
        _keepAliveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _keepAliveTimer.Tick += (_, _) => { }; // no-op, just keeps the pump alive
        _keepAliveTimer.Start();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = $"TextFix — {_settings.ActiveModeName} ({_settings.Hotkey})",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _trayIcon.MouseClick += OnTrayClick;

        // Mode submenu
        var modeMenu = new ToolStripMenuItem("Mode");
        foreach (var mode in _settings.AllModes())
        {
            var item = new ToolStripMenuItem(mode.Name)
            {
                Checked = mode.Name == _settings.ActiveModeName,
                Tag = mode.Name,
            };
            item.Click += OnModeSelected;
            modeMenu.DropDownItems.Add(item);
        }
        _trayIcon.ContextMenuStrip.Items.Add(modeMenu);

        // Provider submenu
        _providerMenu = new ToolStripMenuItem("Provider");
        foreach (var preset in ProviderPresets.All)
        {
            var item = new ToolStripMenuItem(preset.DisplayName)
            {
                Tag = preset.Id,
                Checked = preset.Id == _settings.ActiveProviderId,
            };
            item.Click += (s, _) =>
            {
                if (s is ToolStripMenuItem mi && mi.Tag is string id) SwitchProvider(id);
            };
            _providerMenu.DropDownItems.Add(item);
        }
        _trayIcon.ContextMenuStrip.Items.Add(_providerMenu);

        // History submenu
        _historyMenu = new ToolStripMenuItem("History") { Enabled = false };
        _trayIcon.ContextMenuStrip.Items.Add(_historyMenu);

        _trayIcon.ContextMenuStrip.Items.Add("Copy Last Correction", null, (_, _) => CopyLastCorrection());
        _trayIcon.ContextMenuStrip.Items.Add("Clear history…", null, (_, _) => ClearHistoryWithConfirm());
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("Check for updates…", null, OnCheckForUpdatesClicked);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Suggest a feature…", null, (_, _) => OpenUrl(GitHubNewIdeaUrl));
        _trayIcon.ContextMenuStrip.Items.Add("Report an issue…", null, (_, _) => OpenUrl(GitHubNewIssueUrl));
        _trayIcon.ContextMenuStrip.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("About TextFix…", null, (_, _) => OpenAbout());
        _trayIcon.ContextMenuStrip.Items.Add("Support TextFix ☕", null, (_, _) => OpenUrl(KofiUrl));
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
    }

    private async void OnModeSelected(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item) return;
        var modeName = item.Tag as string;
        if (modeName is null) return;

        _settings.ActiveModeName = modeName;
        try { await _settings.SaveAsync(); }
        catch (Exception ex) { LogError(ex); }

        // Update checkmarks
        if (_trayIcon?.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
        {
            foreach (ToolStripMenuItem mi in modeMenu.DropDownItems)
                mi.Checked = (mi.Tag as string) == modeName;
        }

        // Update tooltip and overlay mode
        if (_trayIcon is not null)
            _trayIcon.Text = $"TextFix — {modeName} ({_settings.Hotkey})";
        _overlay?.SetActiveMode(modeName);
    }

    /// <summary>
    /// Switches the active provider and persists it. Takes effect on the next
    /// correction — it deliberately does not re-run the result currently on screen.
    /// </summary>
    private async void SwitchProvider(string providerId)
    {
        _settings.ActiveProviderId = providerId;
        RebuildServices();
        RefreshProviderMenu();
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private void RefreshProviderMenu()
    {
        if (_providerMenu is not null)
        {
            foreach (ToolStripMenuItem mi in _providerMenu.DropDownItems)
                mi.Checked = (string?)mi.Tag == _settings.ActiveProviderId;
        }

        _overlay?.SetProviders(BuildProviderLabels(), _settings.ActiveProviderId);
    }

    /// <summary>
    /// Explains why <see cref="ProviderFactory.Create"/> returned null, in the same order
    /// the factory checks. Naming the actual missing field matters because the tray and
    /// overlay switchers let a provider be selected without ever visiting Settings, so the
    /// user has no idea which field is blank.
    /// </summary>
    private string ProviderSetupMessage()
    {
        var preset = ProviderPresets.Get(_settings.ActiveProviderId);
        var config = _settings.GetProviderConfig(preset.Id);

        if (preset.Key == KeyRequirement.Required && string.IsNullOrWhiteSpace(config.GetApiKey()))
            return $"{preset.DisplayName} needs an API key. Add one in Settings.";

        var url = string.IsNullOrWhiteSpace(config.BaseUrl) ? preset.BaseUrl : config.BaseUrl;
        if (preset.IsOpenAiCompatible && string.IsNullOrWhiteSpace(url))
            return $"{preset.DisplayName} needs a Base URL. Add one in Settings.";

        return $"{preset.DisplayName} needs a model. Pick one in Settings.";
    }

    /// <summary>Provider name with its configured model, e.g. "Ollama (local) · llama3.2:3b".</summary>
    private string ProviderLabel(ProviderPreset preset)
    {
        var config = _settings.GetProviderConfig(preset.Id);
        var model = string.IsNullOrWhiteSpace(config.Model) ? preset.DefaultModel : config.Model;
        return string.IsNullOrWhiteSpace(model) ? preset.DisplayName : $"{preset.DisplayName} · {model}";
    }

    /// <summary>Provider names with their configured model, e.g. "Ollama · llama3.2:3b".</summary>
    private List<(string Id, string Label)> BuildProviderLabels() =>
        ProviderPresets.All.Select(p => (p.Id, ProviderLabel(p))).ToList();

    private void RefreshHistoryMenu()
    {
        if (_historyMenu is null || _correctionService is null) return;

        _historyMenu.DropDownItems.Clear();
        var items = _correctionService.History.Items;

        if (items.Count == 0)
        {
            _historyMenu.Enabled = false;
            return;
        }

        _historyMenu.Enabled = true;
        foreach (var result in items)
        {
            var label = result.CorrectedText.Length > 50
                ? result.CorrectedText[..50] + "..."
                : result.CorrectedText;
            var menuItem = new ToolStripMenuItem(label);
            var text = result.CorrectedText; // capture for closure
            menuItem.Click += (_, _) =>
            {
                System.Windows.Clipboard.SetText(text);
                _trayIcon?.ShowBalloonTip(1500, "TextFix", "Copied to clipboard.", ToolTipIcon.Info);
            };
            _historyMenu.DropDownItems.Add(menuItem);
        }
    }

    private void SetupOverlay()
    {
        _overlay = new OverlayWindow();
        _overlay.UserResponded += OnUserResponded;
        _overlay.RetryRequested += OnRetryRequested;
        _overlay.ModeChanged += OnOverlayModeChanged;
        _overlay.ProviderChanged += SwitchProvider;
        _overlay.OverlayHidden += OnOverlayHidden;
        _overlay.CopyRequested += OnCopyRequested;
        _overlay.ReapplyRequested += OnReapplyRequested;
        _overlay.BoundsChanged += OnOverlayBoundsChanged;
        _overlay.LoadSavedBounds(
            _settings.OverlayWidth, _settings.OverlayHeight,
            _settings.OverlayLeft, _settings.OverlayTop);
        _overlay.SetActiveMode(_settings.ActiveModeName);
    }

    private System.Windows.Threading.DispatcherTimer? _boundsSaveTimer;

    private void OnOverlayBoundsChanged(double width, double height, double left, double top)
    {
        _settings.OverlayWidth = width;
        _settings.OverlayHeight = height;
        _settings.OverlayLeft = left;
        _settings.OverlayTop = top;

        // Debounce — drag/resize fires many events; only persist after activity settles.
        if (_boundsSaveTimer is null)
        {
            _boundsSaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600),
            };
            _boundsSaveTimer.Tick += async (_, _) =>
            {
                _boundsSaveTimer!.Stop();
                try { await _settings.SaveAsync(); }
                catch (Exception ex) { LogError(ex); }
            };
        }
        _boundsSaveTimer.Stop();
        _boundsSaveTimer.Start();
    }

    private void OnOverlayHidden()
    {
        LogDebug("OverlayHidden");
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_overlay?.IsVisible == true)
        {
            _overlay.FadeOutAndHide();
        }
        else
        {
            _overlay?.ShowIdle(
                _correctionService?.History ?? new CorrectionHistory(),
                _correctionService?.LastResult);
        }
    }

    private void OnCopyRequested()
    {
        CopyLastCorrection();
    }

    private async void OnOverlayModeChanged(string modeName)
    {
        _settings.ActiveModeName = modeName;
        try { await _settings.SaveAsync(); }
        catch (Exception ex) { LogError(ex); }
        SyncTrayState();
    }

    private async void OnRetryRequested()
    {
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return;

        try
        {
            if (_aiClient is null)
            {
                _overlay?.ShowProcessing(_settings.ActiveModeName);
                _overlay?.ShowResult(CorrectionResult.Error("", ProviderSetupMessage()), 0);
                return;
            }

            await _correctionService!.TriggerCorrectionAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing(_settings.ActiveModeName);
            _overlay?.ShowResult(CorrectionResult.Error("", "Something went wrong — try again, or check your API key in Settings."), 0);
        }
        finally
        {
            Interlocked.Exchange(ref _isBusy, 0);
        }
    }

    private async void OnReapplyRequested(string text)
    {
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return;
        try
        {
            if (_aiClient is null)
            {
                _overlay?.ShowProcessing(_settings.ActiveModeName);
                _overlay?.ShowResult(CorrectionResult.Error(text, ProviderSetupMessage()), 0);
                return;
            }
            await _correctionService!.ReapplyAsync(text);
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing(_settings.ActiveModeName);
            _overlay?.ShowResult(CorrectionResult.Error(text, "Something went wrong — try again, or check your API key in Settings."), 0);
        }
        finally
        {
            Interlocked.Exchange(ref _isBusy, 0);
        }
    }

    private async Task SetupServicesAsync()
    {
        _clipboardManager = new ClipboardManager();
        _focusTracker = new FocusTracker();

        _providerFactory = new ProviderFactory(_settings, _log);
        _aiClient = _providerFactory.Create();

        var history = await CorrectionHistory.LoadAsync(maxItems: _settings.HistoryMaxItems);
        _statsTracker = new StatsTracker(StatsTracker.DefaultPath);
        _correctionService = new CorrectionService(_clipboardManager, _focusTracker, _aiClient!, _settings, history);

        _correctionService.ProcessingStarted += () =>
            Dispatcher.Invoke(() =>
            {
                var preset = ProviderPresets.Get(_settings.ActiveProviderId);
                _overlay?.ShowProcessing(
                    _settings.ActiveModeName, ProviderLabel(preset), preset.TimeoutSeconds);
            });

        _correctionService.CorrectionCompleted += result =>
            Dispatcher.Invoke(async () =>
            {
                var autoApply = _settings.ManualApplyOnly || result.LooksConversational
                    ? 0
                    : _settings.OverlayAutoApplySeconds;
                _overlay?.ShowResult(result, autoApply, _settings.ManualApplyOnly || result.LooksConversational);
                RefreshHistoryMenu();
                await _correctionService.History.SaveAsync();
                if (_statsTracker is not null)
                    await _statsTracker.RecordAsync(result);
            });

        _correctionService.ErrorOccurred += msg =>
            Dispatcher.Invoke(() =>
            {
                _overlay?.ShowProcessing(_settings.ActiveModeName);
                _overlay?.ShowResult(CorrectionResult.Error("", msg), 0);
            });

        _correctionService.FocusLost += () =>
            Dispatcher.Invoke(() => _overlay?.ShowFocusLost());
    }

    /// <summary>
    /// Rebuilds the active provider from current settings. Every path that can change a
    /// provider's credentials must go through here, so the factory cache cannot keep
    /// serving a provider holding a revoked key.
    /// </summary>
    /// <remarks>
    /// INVARIANT: when <c>Create()</c> returns null (a key-requiring provider with no key),
    /// <see cref="CorrectionService"/> keeps its previous provider — <c>UpdateProvider</c>
    /// takes a non-nullable argument, so there is nothing to hand it. That stale instance is
    /// unreachable only because every caller gates on <c>_aiClient is null</c> first. Any new
    /// code path that invokes _correctionService MUST do the same, or it will silently run
    /// against the provider the user just switched away from.
    /// </remarks>
    private void RebuildServices()
    {
        _providerFactory?.Invalidate();
        _aiClient = _providerFactory?.Create();
        if (_aiClient is not null)
            _correctionService?.UpdateProvider(_aiClient);
    }

    private void RegisterHotkey()
    {
        if (_hotkeyListener is null)
        {
            _hotkeyListener = new HotkeyListener();
            _hotkeyListener.HotkeyPressed += OnHotkeyPressed;
        }

        if (!_hotkeyListener.Register(_settings.Hotkey))
        {
            _trayIcon?.ShowBalloonTip(
                3000,
                "TextFix",
                $"Could not register hotkey {_settings.Hotkey}. It may be in use by another app. Click the tray icon to change it.",
                ToolTipIcon.Warning);
        }
    }

    private async void OnHotkeyPressed()
    {
        LogDebug($"Hotkey pressed. _isBusy={_isBusy}");
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0)
        {
            LogDebug("Hotkey ignored — busy");
            return;
        }

        try
        {
            if (_aiClient is null)
            {
                var message = ProviderSetupMessage();
                LogDebug($"Provider not usable: {message}");
                _overlay?.ShowProcessing(_settings.ActiveModeName);
                _overlay?.ShowResult(CorrectionResult.Error("", message), 0);
                return;
            }

            LogDebug("Starting TriggerCorrectionAsync");
            await _correctionService!.TriggerCorrectionAsync();
            LogDebug("TriggerCorrectionAsync completed");
        }
        catch (Exception ex)
        {
            LogError(ex);
            LogDebug($"Hotkey handler exception: {ex.Message}");
            _overlay?.ShowProcessing(_settings.ActiveModeName);
            _overlay?.ShowResult(CorrectionResult.Error("", "Something went wrong — try again, or check your API key in Settings."), 0);
        }
        finally
        {
            Interlocked.Exchange(ref _isBusy, 0);
            LogDebug("Hotkey handler done, _isBusy=false");
        }
    }

    private async void OnUserResponded(bool apply)
    {
        LogDebug($"UserResponded: apply={apply}");
        if (_correctionService is null) return;

        // Wrap the whole body — async void + unhandled exception = silent process termination.
        try
        {
            if (apply && _correctionService.LastResult is not null)
            {
                // If user edited the text in manual mode, apply their edit rather than the original AI output.
                // TrimEnd comparison so a stray trailing newline from AcceptsReturn doesn't count as an edit.
                var edited = _overlay?.GetEditedText();
                var original = _correctionService.LastResult.CorrectedText;
                var resultToApply = edited is not null && edited.TrimEnd() != original.TrimEnd()
                    ? _correctionService.LastResult with { CorrectedText = edited }
                    : _correctionService.LastResult;

                LogDebug("Applying correction");
                await _correctionService.ApplyCorrectionAsync(resultToApply);
                LogDebug("ApplyCorrectionAsync done");

                // Always show applied state — unified dialog with diff, mode selector, redo
                _overlay?.SetHistory(_correctionService.History);
                _overlay?.ShowApplied();
            }
            else
            {
                LogDebug("Cancelling correction");
                await _correctionService.CancelAndRestoreAsync();
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private void CopyLastCorrection()
    {
        var text = _correctionService?.LastResult?.CorrectedText;
        if (text is not null)
        {
            System.Windows.Clipboard.SetText(text);
            _trayIcon?.ShowBalloonTip(2000, "TextFix", "Last correction copied to clipboard.", ToolTipIcon.Info);
        }
        else
        {
            _trayIcon?.ShowBalloonTip(2000, "TextFix", "No correction available yet.", ToolTipIcon.Info);
        }
    }

    private async void ClearHistoryWithConfirm()
    {
        if (_correctionService is null) return;

        var result = System.Windows.MessageBox.Show(
            HistoryWipeWarning,
            "TextFix — Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        _correctionService.History.Clear();
        try
        {
            await _correctionService.History.SaveAsync();
            if (_statsTracker is not null)
                await _statsTracker.ClearAsync();
        }
        catch (Exception ex)
        {
            // Never report a wipe that did not happen.
            LogError(ex);
            System.Windows.MessageBox.Show(
                $"History could not be fully erased: {ex.Message}",
                "TextFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshHistoryMenu();
    }

    /// <summary>
    /// Shared by the tray and Settings wipe prompts, so the two cannot drift into
    /// promising different things about the same action.
    /// </summary>
    internal const string HistoryWipeWarning =
        "Erase all stored correction history?\n\n"
        + "This also clears the today/total counters, the session cost, and the "
        + "correction counts in About TextFix. The cumulative \"time saved\" total is "
        + "kept. It cannot be undone.";

    private async void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _correctionService?.History, _statsTracker);
        window.ShowDialog();
        if (window.SettingsChanged)
        {
            RebuildServices();
            RegisterHotkey();
            RebuildModeMenus();
            SyncTrayState();
            // Settings writes ActiveProviderId unconditionally on Save, so merely touring
            // the provider dropdown changes where text is sent. Without this the tray
            // checkmark and the overlay's "Via" label keep asserting the old provider —
            // and since the overlay's SelectedIndex still points there, re-selecting it
            // raises no event and the display can never self-correct.
            RefreshProviderMenu();

            // Apply the new history cap to the running service and persist a trimmed file
            // so the limit takes effect immediately rather than on next launch.
            if (_correctionService is not null)
            {
                _correctionService.History.SetMaxItems(_settings.HistoryMaxItems);
                await _correctionService.History.SaveAsync();
            }
        }
        if (window.HistoryCleared)
            RefreshHistoryMenu();
    }

    private void RebuildModeMenus()
    {
        _overlay?.RefreshModes(_settings.AllModes(), _settings.ActiveModeName);

        if (_trayIcon?.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
        {
            modeMenu.DropDownItems.Clear();
            foreach (var mode in _settings.AllModes())
            {
                var item = new ToolStripMenuItem(mode.Name)
                {
                    Checked = mode.Name == _settings.ActiveModeName,
                    Tag = mode.Name,
                };
                item.Click += OnModeSelected;
                modeMenu.DropDownItems.Add(item);
            }
        }
    }

    private void SyncTrayState()
    {
        if (_trayIcon is null) return;

        _trayIcon.Text = $"TextFix — {_settings.ActiveModeName} ({_settings.Hotkey})";

        // Sync mode checkmarks in tray
        if (_trayIcon.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
        {
            foreach (ToolStripMenuItem mi in modeMenu.DropDownItems)
                mi.Checked = (mi.Tag as string) == _settings.ActiveModeName;
        }

        // Sync overlay mode selector
        _overlay?.SetActiveMode(_settings.ActiveModeName);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("TextFix exiting");
        _keepAliveTimer?.Stop();
        _hotkeyListener?.Dispose();
        _trayIcon?.Dispose();
        _hiddenWindow?.Close();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log?.Warn($"OpenUrl failed: {ex.Message}"); }
    }

    private void OpenLogFolder()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TextFix", "logs");
            Directory.CreateDirectory(logDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", logDir) { UseShellExecute = true });
        }
        catch (Exception ex) { _log?.Warn($"OpenLogFolder failed: {ex.Message}"); }
    }

    private void OpenAbout()
    {
        if (_statsTracker is null) return;
        var window = new Views.AboutWindow(_statsTracker);
        window.ShowDialog();
    }

    private static void LogError(Exception ex) => _log?.Error("Unhandled", ex);

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogDebug(string message) => _log?.Info(message);
}
