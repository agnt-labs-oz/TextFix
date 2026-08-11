using System.IO;
using System.Net;
using TextFix.Services;
using TextFix.Tests.Services.Providers;

namespace TextFix.Tests.Services;

public class OllamaSetupTests
{
    // Captured verbatim from a live Ollama 0.32.6 (2026-08-11) — not hand-written.
    // Hand-approximated fixtures are how the <text>-wrapper bug shipped.
    private const string TagsBody = """
    {"models":[{"name":"gemma4:26b","model":"gemma4:26b","size":17987581215},{"name":"llama3.2:3b","model":"llama3.2:3b","size":2019393189}]}
    """;

    private const string PullSuccessStream = """
    {"status":"pulling manifest"}
    {"status":"pulling dde5aa3fc5ff","digest":"sha256:dde5aa3fc5ffc17176b5e8bdc82f587b24b2678c6c66101bf7da77af9f7ccdff","total":2019377376,"completed":1000000}
    {"status":"pulling dde5aa3fc5ff","digest":"sha256:dde5aa3fc5ffc17176b5e8bdc82f587b24b2678c6c66101bf7da77af9f7ccdff","total":2019377376,"completed":2019377376}
    {"status":"verifying sha256 digest"}
    {"status":"writing manifest"}
    {"status":"success"}
    """;

    private const string PullErrorStream = """
    {"status":"pulling manifest"}
    {"error":"pull model manifest: file does not exist"}
    """;

    private static OllamaSetup Make(StubHttpMessageHandler handler) =>
        new("http://localhost:11434/v1", handler);

    /// <summary>
    /// Reports on the calling thread. <see cref="Progress{T}"/> posts to a sync
    /// context asynchronously, which forces tests into arbitrary Task.Delay drains —
    /// a latent flake this avoids entirely.
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434")]
    [InlineData("http://192.168.1.20:9999/v1", "http://192.168.1.20:9999")]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    public void ApiRootFrom_StripsTheCompatibilityLayerSuffix(string baseUrl, string expectedRoot)
    {
        // The provider's BaseUrl points at /v1; model management only exists at the
        // root. A user who moved Ollama to another port must have THAT server managed.
        Assert.Equal(expectedRoot, OllamaSetup.ApiRootFrom(baseUrl));
    }

    [Theory]
    // /api/tags lists newest-first — on the dev machine that put a 26B model measured
    // unusable on CPU ahead of the 3B that works. A recommended model must win.
    [InlineData(new[] { "gemma4:26b", "llama3.2:3b" }, "llama3.2:3b")]
    [InlineData(new[] { "gemma4:26b", "qwen2.5:7b" }, "qwen2.5:7b")]
    [InlineData(new[] { "llama3.2:3b", "qwen2.5:7b" }, "llama3.2:3b")] // preference order, not list order
    [InlineData(new[] { "mistral:7b" }, "mistral:7b")]                 // no recommended model: take what exists
    public void ChooseReadyModel_PrefersARecommendedModel(string[] available, string expected)
    {
        Assert.Equal(expected, OllamaSetup.ChooseReadyModel(available));
    }

    [Fact]
    public void ChooseReadyModel_NullWhenNothingInstalled()
    {
        Assert.Null(OllamaSetup.ChooseReadyModel([]));
    }

    [Fact]
    public void ChooseReadyModel_PrefersARecommendedModel_OverTagsOrder()
    {
        // /api/tags lists newest-first. On this project's own dev machine that put a
        // 26B model — measured unusable on its CPU — ahead of the 3B that works.
        // Auto-filling by list order would hand the user a provider that times out.
        Assert.Equal("llama3.2:3b",
            OllamaSetup.ChooseReadyModel(["gemma4:26b", "llama3.2:3b"]));
    }

    [Fact]
    public void ChooseReadyModel_FallsBackToTheFirstModel_WhenNothingRecommendedIsPresent()
    {
        // A model the user pulled themselves is still a working setup — never refuse it.
        Assert.Equal("gemma4:26b", OllamaSetup.ChooseReadyModel(["gemma4:26b", "phi4:14b"]));
    }

    [Fact]
    public void ChooseReadyModel_ReturnsNull_WhenNoModelsExist()
    {
        Assert.Null(OllamaSetup.ChooseReadyModel([]));
    }

    [Fact]
    public async Task IsServerUpAsync_TrueWhenVersionAnswers()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"version":"0.32.6"}""");

        Assert.True(await Make(handler).IsServerUpAsync());
        Assert.EndsWith("/api/version", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task IsServerUpAsync_FalseWhenConnectionRefused()
    {
        var handler = new StubHttpMessageHandler(
            new System.Net.Http.HttpRequestException("refused"));

        Assert.False(await Make(handler).IsServerUpAsync());
    }

    [Fact]
    public async Task ListLocalModelsAsync_ParsesTheCapturedTagsShape()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TagsBody);

        var models = await Make(handler).ListLocalModelsAsync();

        Assert.Equal(["gemma4:26b", "llama3.2:3b"], models);
    }

    [Fact]
    public async Task PullModelAsync_ReportsProgress_AndSendsTheModelField()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, PullSuccessStream);
        var reports = new List<OllamaSetup.PullProgress>();

        await Make(handler).PullModelAsync(
            "llama3.2:3b", new SyncProgress<OllamaSetup.PullProgress>(reports.Add), CancellationToken.None);

        Assert.Contains("\"model\":\"llama3.2:3b\"", handler.LastRequestBody);
        Assert.Contains(reports, r => r is { Status: "success" });
        Assert.Contains(reports, r => r.Completed == 1000000 && r.Total == 2019377376);
    }

    [Fact]
    public async Task PullModelAsync_Throws_WhenTheStreamCarriesAnErrorLine()
    {
        // The trap: a failed pull is HTTP 200 with an {"error":...} line inside the
        // stream. The status code says nothing.
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, PullErrorStream);

        var ex = await Assert.ThrowsAsync<OllamaPullException>(() =>
            Make(handler).PullModelAsync("nope", null, CancellationToken.None));

        Assert.Contains("file does not exist", ex.Message);
    }

    [Fact]
    public async Task PullModelAsync_Throws_WhenTheStreamEndsWithoutSuccess()
    {
        // A dropped connection mid-pull ends the stream cleanly but without the
        // terminal success line — that must not be reported as a completed pull.
        const string truncated = """
        {"status":"pulling manifest"}
        {"status":"pulling dde5aa3fc5ff","total":2019377376,"completed":5000}
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, truncated);

        await Assert.ThrowsAsync<OllamaPullException>(() =>
            Make(handler).PullModelAsync("llama3.2:3b", null, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadInstallerAsync_WritesAllBytes_AndReportsProgress()
    {
        const string body = "MZ-this-stands-in-for-installer-bytes";
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, body);
        var dest = Path.Combine(Path.GetTempPath(), $"OllamaSetup-{Guid.NewGuid():N}.exe");
        var reports = new List<(long Done, long Total)>();
        try
        {
            await Make(handler).DownloadInstallerAsync(
                dest, new SyncProgress<(long, long)>(reports.Add), CancellationToken.None);

            Assert.Equal(body, File.ReadAllText(dest));
            Assert.NotEmpty(reports);
            Assert.Equal(body.Length, reports[^1].Done);
        }
        finally
        {
            File.Delete(dest);
        }
    }
}
