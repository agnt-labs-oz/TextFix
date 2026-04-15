using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using TextFix.Models;
using TextFix.Services;
using TextFix.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TextFix;

public partial class App : Application
{
    private static Mutex? _mutex;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _historyMenu;
    private HotkeyListener? _hotkeyListener;
    private CorrectionService? _correctionService;
    private AiClient? _aiClient;
    private ClipboardManager? _clipboardManager;
    private FocusTracker? _focusTracker;
    private OverlayWindow? _overlay;
    private AppSettings _settings = new();
    private bool _isBusy;

    // Hidden window needed for hotkey message pump
    private Window? _hiddenWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "TextFix_SingleInstance", out bool isNew);
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

        CreateHiddenWindow();
        SetupTrayIcon();
        SetupOverlay();
        SetupServices();
        RegisterHotkey();

        // Prompt for API key on first run
        if (string.IsNullOrWhiteSpace(_settings.GetApiKey()))
            OpenSettings();
    }

    private void CreateHiddenWindow()
    {
        _hiddenWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        // Create the HWND without showing the window — needed for hotkey message pump
        new WindowInteropHelper(_hiddenWindow).EnsureHandle();
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

        // Mode submenu
        var modeMenu = new ToolStripMenuItem("Mode");
        foreach (var mode in CorrectionMode.Defaults)
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

        // History submenu
        _historyMenu = new ToolStripMenuItem("History") { Enabled = false };
        _trayIcon.ContextMenuStrip.Items.Add(_historyMenu);

        _trayIcon.ContextMenuStrip.Items.Add("Copy Last Correction", null, (_, _) => CopyLastCorrection());
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
    }

    private async void OnModeSelected(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item) return;
        var modeName = item.Tag as string;
        if (modeName is null) return;

        _settings.ActiveModeName = modeName;
        await _settings.SaveAsync();

        // Update checkmarks
        if (_trayIcon?.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
        {
            foreach (ToolStripMenuItem mi in modeMenu.DropDownItems)
                mi.Checked = (mi.Tag as string) == modeName;
        }

        // Update tooltip
        if (_trayIcon is not null)
            _trayIcon.Text = $"TextFix — {modeName} ({_settings.Hotkey})";
    }

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
    }

    private async void OnRetryRequested()
    {
        if (_isBusy) return;
        _isBusy = true;

        try
        {
            await _correctionService!.TriggerCorrectionAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(CorrectionResult.Error("", $"Error: {ex.Message}"), 0);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void SetupServices()
    {
        _clipboardManager = new ClipboardManager();
        _focusTracker = new FocusTracker();

        if (!string.IsNullOrWhiteSpace(_settings.GetApiKey()))
            _aiClient = new AiClient(_settings);

        _correctionService = new CorrectionService(_clipboardManager, _focusTracker, _aiClient!, _settings);

        _correctionService.ProcessingStarted += () =>
            Dispatcher.Invoke(() => _overlay?.ShowProcessing());

        _correctionService.CorrectionCompleted += result =>
            Dispatcher.Invoke(() =>
            {
                _overlay?.ShowResult(result, _settings.OverlayAutoApplySeconds, _settings.KeepOverlayOpen);
                RefreshHistoryMenu();
            });

        _correctionService.ErrorOccurred += msg =>
            Dispatcher.Invoke(() =>
            {
                _overlay?.ShowProcessing();
                _overlay?.ShowResult(CorrectionResult.Error("", msg), 0);
            });

        _correctionService.FocusLost += () =>
            Dispatcher.Invoke(() => _overlay?.ShowFocusLost());
    }

    private void RebuildServices()
    {
        _aiClient = !string.IsNullOrWhiteSpace(_settings.GetApiKey())
            ? new AiClient(_settings)
            : null;
        _correctionService?.UpdateAiClient(_aiClient!);
    }

    private void RegisterHotkey()
    {
        if (_hiddenWindow is null) return;

        // Unregister old hotkey if listener already exists
        _hotkeyListener?.Unregister();

        if (_hotkeyListener is null)
        {
            _hotkeyListener = new HotkeyListener();
            _hotkeyListener.HotkeyPressed += OnHotkeyPressed;
        }

        if (!_hotkeyListener.Register(_hiddenWindow, _settings.Hotkey))
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
        if (_isBusy) return;
        _isBusy = true;

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.GetApiKey()))
            {
                _overlay?.ShowProcessing();
                _overlay?.ShowResult(
                    CorrectionResult.Error("", "Set up your API key in Settings."),
                    0);
                return;
            }

            await _correctionService!.TriggerCorrectionAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(CorrectionResult.Error("", $"Error: {ex.Message}"), 0);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void OnUserResponded(bool apply)
    {
        if (_correctionService is null) return;

        if (apply && _correctionService.LastResult is not null)
        {
            await _correctionService.ApplyCorrectionAsync(_correctionService.LastResult);

            if (_settings.KeepOverlayOpen)
                _overlay?.ShowApplied();
            else
                _overlay?.Hide();
        }
        else
        {
            _correctionService.CancelAndRestore();
            _overlay?.Hide();
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

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        window.ShowDialog();
        if (window.SettingsChanged)
        {
            RebuildServices();
            RegisterHotkey();
            SyncTrayState();
        }
    }

    private void SyncTrayState()
    {
        if (_trayIcon is null) return;

        _trayIcon.Text = $"TextFix — {_settings.ActiveModeName} ({_settings.Hotkey})";

        // Sync mode checkmarks
        if (_trayIcon.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
        {
            foreach (ToolStripMenuItem mi in modeMenu.DropDownItems)
                mi.Checked = (mi.Tag as string) == _settings.ActiveModeName;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyListener?.Dispose();
        _trayIcon?.Dispose();
        _hiddenWindow?.Close();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TextFix");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:o}] {ex}\n\n");
        }
        catch { /* best effort */ }
    }
}
