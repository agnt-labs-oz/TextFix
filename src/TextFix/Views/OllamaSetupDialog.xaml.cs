using System.IO;
using System.Windows;
using TextFix.Services;

namespace TextFix.Views;

/// <summary>
/// Walks a user from nothing installed to a working local model: detect → download →
/// verify → install → detect again → pull a model → ready. The state machine lives
/// here; all I/O lives in <see cref="OllamaSetup"/>.
/// </summary>
/// <remarks>
/// The one invariant that must survive any edit: <b>the downloaded installer is never
/// launched except through a passed Authenticode check</b> — chain validity plus the
/// pinned publisher CN. The user chose direct-from-ollama.com over a package manager;
/// the verification is what makes that choice responsible.
/// </remarks>
public partial class OllamaSetupDialog : Window
{
    private enum Step { Detecting, OfferStart, OfferDownload, Working, OfferPull, Done, Failed }

    private readonly OllamaSetup _setup;
    private Step _step = Step.Detecting;
    private CancellationTokenSource? _cts;

    /// <summary>Model available when the dialog closed, or null if setup didn't finish.</summary>
    public string? ReadyModel { get; private set; }

    public OllamaSetupDialog(string effectiveBaseUrl)
    {
        InitializeComponent();
        _setup = new OllamaSetup(effectiveBaseUrl);
        Loaded += async (_, _) => await DetectAsync();
    }

    private async Task DetectAsync()
    {
        _step = Step.Detecting;
        ActionButton.IsEnabled = false;
        StatusText.Text = "Checking for Ollama…";

        if (await _setup.IsServerUpAsync())
        {
            await CheckModelsAsync();
            return;
        }

        if (_setup.IsInstalledOnDisk())
        {
            _step = Step.OfferStart;
            StatusText.Text = "Ollama is installed but not running.";
            DetailText.Text = "";
            SetAction("Start Ollama");
            return;
        }

        _step = Step.OfferDownload;
        StatusText.Text = $"Ollama isn't installed. The installer is {OllamaSetup.ApproxDownloadSize} — "
            + "it bundles the runtimes for every GPU type.";
        DetailText.Text = $"Downloaded over HTTPS from {OllamaSetup.InstallerUrl}, and the installer's "
            + $"digital signature is verified as \"{OllamaSetup.RequiredSignerCn}\" before it runs.";
        SetAction("Download and install");
    }

    private async void OnAction(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_step)
            {
                case Step.OfferStart: await StartInstalledAsync(); break;
                case Step.OfferDownload: await DownloadVerifyInstallAsync(); break;
                case Step.OfferPull: await PullAsync(); break;
                case Step.Done: DialogResult = true; break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
            await DetectAsync();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async Task StartInstalledAsync()
    {
        BeginWork("Starting Ollama…");
        _setup.StartInstalledApp();
        if (await _setup.WaitForServerAsync(TimeSpan.FromSeconds(30), _cts!.Token))
        {
            await CheckModelsAsync();
            return;
        }
        Fail("Ollama was started but its server didn't come up. Check the Ollama tray icon, then try again.");
    }

    private async Task DownloadVerifyInstallAsync()
    {
        BeginWork("Downloading Ollama…");
        Progress.Visibility = Visibility.Visible;

        var installerPath = Path.Combine(
            Path.GetTempPath(), $"OllamaSetup-{Guid.NewGuid():N}.exe");
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    Progress.IsIndeterminate = false;
                    Progress.Value = 100.0 * p.Done / p.Total;
                    DetailText.Text = $"{p.Done / 1_048_576:N0} MB of {p.Total / 1_048_576:N0} MB";
                }
                else
                {
                    Progress.IsIndeterminate = true;
                    DetailText.Text = $"{p.Done / 1_048_576:N0} MB";
                }
            });
            await _setup.DownloadInstallerAsync(installerPath, progress, _cts!.Token);

            Progress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Verifying the installer's signature…";
            // Launch is unreachable except through this check. Verification runs on a
            // background thread — WinVerifyTrust hashes the whole 1.5 GB file.
            var verify = await Task.Run(() =>
                AuthenticodeVerifier.Verify(installerPath, OllamaSetup.RequiredSignerCn));
            if (!verify.IsValid)
            {
                Fail($"The downloaded installer failed verification and was deleted. {verify.Detail}");
                return;
            }

            StatusText.Text = "Complete the Ollama installer, then come back here.";
            DetailText.Text = "Waiting for the Ollama server to appear…";
            var installer = _setup.LaunchInstaller(installerPath);
            // Tie cleanup to the installer process actually exiting, not to the server
            // appearing — the file cannot be deleted while the installer runs from it.
            _ = installer?.WaitForExitAsync().ContinueWith(_ => TryDelete(installerPath));

            if (await _setup.WaitForServerAsync(TimeSpan.FromMinutes(5), _cts.Token))
            {
                await CheckModelsAsync();
                return;
            }
            Fail("The installer ran but the Ollama server never appeared. If you cancelled the install, just close this dialog.");
        }
        catch
        {
            TryDelete(installerPath);
            throw;
        }
    }

    private async Task CheckModelsAsync()
    {
        StatusText.Text = "Ollama is running. Checking for models…";
        var models = await _setup.ListLocalModelsAsync();
        if (models.Count > 0)
        {
            ReadyModel = models[0];
            Finish($"Ollama is ready — {models.Count} model(s) available.");
            return;
        }

        _step = Step.OfferPull;
        StatusText.Text = "Ollama is running, but no model is downloaded yet.";
        DetailText.Text = "";
        ModelPanel.Visibility = Visibility.Visible;
        SetAction("Download model");
    }

    private async Task PullAsync()
    {
        var model = SmallModelRadio.IsChecked == true ? "llama3.2:3b" : "qwen2.5:7b";
        BeginWork($"Downloading {model}…");
        ModelPanel.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Visible;

        var progress = new Progress<OllamaSetup.PullProgress>(p =>
        {
            if (p.Total > 0)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = 100.0 * p.Completed / p.Total;
                DetailText.Text = $"{p.Status} — {p.Completed / 1_048_576:N0} MB of {p.Total / 1_048_576:N0} MB";
            }
            else
            {
                Progress.IsIndeterminate = true;
                DetailText.Text = p.Status;
            }
        });

        try
        {
            await _setup.PullModelAsync(model, progress, _cts!.Token);
        }
        catch (OllamaPullException ex)
        {
            ModelPanel.Visibility = Visibility.Visible;
            Progress.Visibility = Visibility.Collapsed;
            _step = Step.OfferPull;
            StatusText.Text = $"The download failed: {ex.Message}";
            SetAction("Try again");
            return;
        }

        ReadyModel = model;
        Finish($"Ollama is ready with {model}.");
    }

    private void BeginWork(string status)
    {
        _step = Step.Working;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        ActionButton.IsEnabled = false;
        StatusText.Text = status;
        DetailText.Text = "";
    }

    private void SetAction(string label)
    {
        ActionButton.Content = label;
        ActionButton.IsEnabled = true;
    }

    private void Finish(string message)
    {
        _step = Step.Done;
        Progress.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        DetailText.Text = "Hit Test connection in Settings to confirm.";
        SetAction("Done");
        CloseButton.Content = "Close";
    }

    private void Fail(string message)
    {
        _step = Step.Failed;
        Progress.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        ActionButton.IsEnabled = false;
        CloseButton.Content = "Close";
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = _step == Step.Done;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _cts?.Cancel();

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort — %TEMP% is cleaned anyway */ }
    }
}
