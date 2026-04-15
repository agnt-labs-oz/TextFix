using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
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
    private HotkeyListener? _hotkeyListener;
    private CorrectionService? _correctionService;
    private OverlayWindow? _overlay;
    private AppSettings _settings = new();

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
        RegisterHotkey();

        // Prompt for API key on first run
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
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
        _hiddenWindow.Show();
        _hiddenWindow.Hide();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = $"TextFix ({_settings.Hotkey})",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
    }

    private void SetupOverlay()
    {
        _overlay = new OverlayWindow();
        _overlay.UserResponded += OnUserResponded;
    }

    private void RegisterHotkey()
    {
        _hotkeyListener?.Dispose();
        _hotkeyListener = new HotkeyListener();
        _hotkeyListener.HotkeyPressed += OnHotkeyPressed;

        if (_hiddenWindow is null) return;

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
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(
                CorrectionResult.Error("", "Set up your API key in Settings."),
                0);
            return;
        }

        try
        {
            var aiClient = new AiClient(_settings);
            var clipboard = new ClipboardManager();
            var focusTracker = new FocusTracker();
            _correctionService = new CorrectionService(clipboard, focusTracker, aiClient);

            _correctionService.ProcessingStarted += () =>
                Dispatcher.Invoke(() => _overlay?.ShowProcessing());

            _correctionService.CorrectionCompleted += result =>
                Dispatcher.Invoke(() => _overlay?.ShowResult(result, _settings.OverlayAutoApplySeconds));

            _correctionService.ErrorOccurred += msg =>
                Dispatcher.Invoke(() =>
                {
                    _overlay?.ShowProcessing();
                    _overlay?.ShowResult(CorrectionResult.Error("", msg), 0);
                });

            _correctionService.FocusLost += () =>
                Dispatcher.Invoke(() => _overlay?.ShowFocusLost());

            await _correctionService.TriggerCorrectionAsync();
        }
        catch (Exception ex)
        {
            LogError(ex);
            _overlay?.ShowProcessing();
            _overlay?.ShowResult(CorrectionResult.Error("", $"Error: {ex.Message}"), 0);
        }
    }

    private async void OnUserResponded(bool apply)
    {
        if (_correctionService is null) return;

        if (apply && _correctionService.LastResult is not null)
        {
            await _correctionService.ApplyCorrectionAsync(_correctionService.LastResult);
        }
        else
        {
            _correctionService.CancelAndRestore();
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        window.ShowDialog();
        if (window.SettingsChanged)
        {
            RegisterHotkey();
            if (_trayIcon is not null)
                _trayIcon.Text = $"TextFix ({_settings.Hotkey})";
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyListener?.Dispose();
        _trayIcon?.Dispose();
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
