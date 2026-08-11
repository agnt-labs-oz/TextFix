using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextFix.Services;

/// <summary>
/// Everything the Ollama setup dialog needs: detect the server, download the installer,
/// launch it, wait for the server, list and pull models. No UI in here — the dialog owns
/// the state machine, this class owns the I/O, and every network call takes a
/// CancellationToken because each one sits behind a live Cancel button.
/// </summary>
/// <remarks>
/// Talks to Ollama's NATIVE API (<c>/api/…</c> at the server root), not the OpenAI
/// compatibility layer the provider uses (<c>/v1/…</c>) — model management only exists
/// on the native side. <see cref="ApiRootFrom"/> derives the root from the same
/// effective BaseUrl the provider resolves, so a user who moved Ollama to another port
/// gets their server managed, not the default one.
///
/// Wire shapes below were captured from a live Ollama 0.32.6 on 2026-08-11, not taken
/// from documentation — see docs/superpowers/plans/2026-08-11-ollama-setup-helper.md.
/// </remarks>
public class OllamaSetup
{
    /// <summary>
    /// Fixed download URL — never user-supplied. Observed to redirect (all HTTPS) to the
    /// current GitHub release asset; ~1.46 GB as of v0.32.7.
    /// </summary>
    public const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";

    /// <summary>
    /// The CN the installer's Authenticode signature must carry, read from the signed
    /// binaries Ollama actually ships. If Ollama ever changes signing identity, the
    /// helper fails closed and this constant needs re-verifying against a fresh install.
    /// </summary>
    public const string RequiredSignerCn = "Ollama Inc.";

    /// <summary>Shown before download starts; the real size is only known at response time.</summary>
    public const string ApproxDownloadSize = "about 1.5 GB";

    /// <summary>
    /// The models the setup dialog offers, in preference order — sized for text
    /// correction, measured usable on CPU-only hardware (llama3.2:3b) or worth the
    /// download on a GPU box (qwen2.5:7b).
    /// </summary>
    public static readonly string[] RecommendedModels = ["llama3.2:3b", "qwen2.5:7b"];

    /// <summary>
    /// Which already-present model the dialog should treat as ready.
    /// </summary>
    /// <remarks>
    /// Not simply the first: <c>/api/tags</c> lists newest-first, and on this project's
    /// own dev machine that was a 26B model measured UNUSABLE on its CPU-only hardware —
    /// auto-filling that into an empty model box would hand the user a provider that
    /// times out on a paragraph. A recommended model wins whenever one is present.
    /// </remarks>
    public static string? ChooseReadyModel(IReadOnlyList<string> available) =>
        available.Count == 0
            ? null
            : RecommendedModels.FirstOrDefault(available.Contains) ?? available[0];

    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly string _apiRoot;

    public OllamaSetup(string effectiveBaseUrl, HttpMessageHandler? handler = null)
    {
        _apiRoot = ApiRootFrom(effectiveBaseUrl);
        _http = handler is null ? SharedClient : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// The provider's BaseUrl points at the OpenAI compatibility layer
    /// (<c>http://localhost:11434/v1</c>); the native API lives at the root.
    /// </summary>
    public static string ApiRootFrom(string effectiveBaseUrl)
    {
        var trimmed = (effectiveBaseUrl ?? "").TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^3].TrimEnd('/')
            : trimmed;
    }

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Ollama");

    /// <summary>Installed on disk but the server may or may not be running.</summary>
    public virtual bool IsInstalledOnDisk() =>
        File.Exists(Path.Combine(DefaultInstallDir, "ollama app.exe"));

    /// <summary>Starts the installed tray app, which brings the server up with it.</summary>
    public virtual void StartInstalledApp() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(DefaultInstallDir, "ollama app.exe"))
        { UseShellExecute = true });

    public virtual async Task<bool> IsServerUpAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.GetAsync($"{_apiRoot}/api/version", timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Polls until the server answers or the budget runs out.</summary>
    public async Task<bool> WaitForServerAsync(TimeSpan budget, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsServerUpAsync(ct)) return true;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return false;
    }

    /// <summary>Names of locally available models, e.g. "llama3.2:3b".</summary>
    public virtual async Task<IReadOnlyList<string>> ListLocalModelsAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        using var response = await _http.GetAsync($"{_apiRoot}/api/tags", timeoutCts.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TagsResponse>(timeoutCts.Token);
        return payload?.Models?
            .Select(m => m.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList() ?? [];
    }

    /// <summary>
    /// Streams the installer to <paramref name="destinationPath"/>, reporting
    /// (bytesSoFar, totalOrMinusOne). The caller MUST verify the file's signature
    /// before launching it — this method only fetches bytes.
    /// </summary>
    public virtual async Task DownloadInstallerAsync(
        string destinationPath, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        // ResponseHeadersRead is load-bearing: the default buffers the entire body,
        // which for a ~1.5 GB installer is a memory balloon that no stubbed test would
        // ever catch.
        using var response = await _http.GetAsync(
            InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = File.Create(destinationPath);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }
    }

    /// <summary>
    /// Launches the verified installer interactively and returns the process, so the
    /// caller can tie cleanup of the downloaded file to the installer's exit.
    /// </summary>
    public virtual System.Diagnostics.Process? LaunchInstaller(string installerPath) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
        });

    /// <summary>
    /// Pulls a model, reporting per-layer progress. Throws <see cref="OllamaPullException"/>
    /// when the stream reports failure.
    /// </summary>
    /// <remarks>
    /// The failure mode that matters: a failed pull arrives as an <c>{"error":"…"}</c>
    /// line INSIDE an HTTP 200 stream (captured live: a bogus model name returns
    /// status 200, one "pulling manifest" line, then the error line). Checking the
    /// status code tells you nothing; every line must be inspected.
    /// </remarks>
    public virtual async Task PullModelAsync(
        string model, IProgress<PullProgress>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiRoot}/api/pull")
        {
            Content = JsonContent.Create(new { model }),
        };

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var sawSuccess = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            PullLine? parsed;
            try { parsed = JsonSerializer.Deserialize<PullLine>(line); }
            catch (JsonException) { continue; } // an unparseable line is not worth dying over
            if (parsed is null) continue;

            if (!string.IsNullOrEmpty(parsed.Error))
                throw new OllamaPullException(parsed.Error);

            if (parsed.Status == "success") sawSuccess = true;
            progress?.Report(new PullProgress(
                parsed.Status ?? "", parsed.Completed ?? 0, parsed.Total ?? 0));
        }

        if (!sawSuccess)
            throw new OllamaPullException(
                "The download ended without confirming success — try again.");
    }

    public sealed record PullProgress(string Status, long Completed, long Total);

    private sealed record TagsResponse(
        [property: JsonPropertyName("models")] List<TagModel>? Models);

    private sealed record TagModel(
        [property: JsonPropertyName("name")] string? Name);

    private sealed record PullLine(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("total")] long? Total,
        [property: JsonPropertyName("completed")] long? Completed,
        [property: JsonPropertyName("error")] string? Error);
}

/// <summary>A model pull that the Ollama server itself reported as failed.</summary>
public sealed class OllamaPullException(string message) : Exception(message);
