using System.IO;
using TextFix.Models;
using TextFix.Services.Providers;

namespace TextFix.Tests.Models;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TextFixTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Defaults_HasExpectedValues()
    {
        var settings = new AppSettings();

        Assert.Equal("", settings.GetApiKey());
        Assert.Equal("Ctrl+Shift+Z", settings.Hotkey);
        Assert.Equal("claude-haiku-4-5-20251001", settings.Model);
        Assert.Equal(3, settings.OverlayAutoApplySeconds);
        Assert.False(settings.StartWithWindows);
        Assert.Equal("Fix errors", settings.ActiveModeName);
        Assert.Equal(10, settings.HistoryMaxItems);
    }

    [Fact]
    public async Task Load_DefaultsHistoryMaxItems_WhenPropertyAbsentFromOldFile()
    {
        // Settings files written before HistoryMaxItems existed don't include the key —
        // System.Text.Json deserialises that as 0 (int default), which would clamp the
        // history ring buffer to a single entry. Verify the load path repairs it.
        var path = Path.Combine(_tempDir, "old.json");
        await File.WriteAllTextAsync(path, """{"Hotkey":"Ctrl+Shift+Z","Model":"claude-haiku-4-5-20251001"}""");

        var settings = await AppSettings.LoadAsync(path);

        Assert.Equal(10, settings.HistoryMaxItems);
    }

    [Fact]
    public async Task Load_PreservesValidHistoryMaxItems()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var saved = new AppSettings { HistoryMaxItems = 25 };
        await saved.SaveAsync(path);

        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(25, loaded.HistoryMaxItems);
    }

    [Fact]
    public async Task Save_CreatesJsonFile()
    {
        var settings = new AppSettings();
        settings.SetApiKey("test-key-123");
        var path = Path.Combine(_tempDir, "settings.json");

        await settings.SaveAsync(path);

        Assert.True(File.Exists(path));
        // Verify the file does NOT contain the plaintext key
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("test-key-123", json);
    }

    [Fact]
    public async Task Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");

        var settings = await AppSettings.LoadAsync(path);

        Assert.Equal("", settings.GetApiKey());
        Assert.Equal("Ctrl+Shift+Z", settings.Hotkey);
    }

    [Fact]
    public async Task Load_ReturnsDefaults_WhenFileIsCorrupted()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(path, "not valid json {{{");

        var settings = await AppSettings.LoadAsync(path);

        Assert.Equal("", settings.GetApiKey());
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var original = new AppSettings();
        original.SetApiKey("sk-ant-test");
        original.Hotkey = "Ctrl+Alt+F";
        original.Model = "claude-haiku-4-5-20251001";
        original.OverlayAutoApplySeconds = 5;
        original.StartWithWindows = true;

        var path = Path.Combine(_tempDir, "settings.json");

        await original.SaveAsync(path);
        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(original.GetApiKey(), loaded.GetApiKey());
        Assert.Equal(original.Hotkey, loaded.Hotkey);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.OverlayAutoApplySeconds, loaded.OverlayAutoApplySeconds);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
    }

    [Fact]
    public async Task Load_MigratesPlaintextApiKey()
    {
        // Simulate a legacy settings file with plaintext ApiKey
        var path = Path.Combine(_tempDir, "legacy.json");
        await File.WriteAllTextAsync(path, """{"ApiKey":"sk-legacy-key","Hotkey":"Ctrl+Shift+Z"}""");

        var settings = await AppSettings.LoadAsync(path);

        // Key should be readable
        Assert.Equal("sk-legacy-key", settings.GetApiKey());
        // Plaintext should have been cleared and encrypted key set
        Assert.Equal("", settings.ApiKey);
        Assert.NotEmpty(settings.EncryptedApiKey);
    }

    [Fact]
    public void Defaults_ActiveModeIsFixErrors()
    {
        var settings = new AppSettings();
        Assert.Equal("Fix errors", settings.ActiveModeName);
    }

    [Fact]
    public void GetActiveMode_ReturnsMatchingMode()
    {
        var settings = new AppSettings { ActiveModeName = "Professional" };
        var mode = settings.GetActiveMode();
        Assert.Equal("Professional", mode.Name);
        Assert.Contains("professional", mode.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetActiveMode_FallsBackToFixErrors_WhenNameInvalid()
    {
        var settings = new AppSettings { ActiveModeName = "NonexistentMode" };
        var mode = settings.GetActiveMode();
        Assert.Equal("Fix errors", mode.Name);
    }

    [Fact]
    public async Task RoundTrip_PreservesActiveModeName()
    {
        var original = new AppSettings { ActiveModeName = "Concise" };
        var path = Path.Combine(_tempDir, "mode_test.json");

        await original.SaveAsync(path);
        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal("Concise", loaded.ActiveModeName);
    }

    [Fact]
    public void CustomModes_DefaultsToEmpty()
    {
        var settings = new AppSettings();
        Assert.Empty(settings.CustomModes);
    }

    [Fact]
    public void GetActiveMode_FindsCustomMode()
    {
        var settings = new AppSettings
        {
            ActiveModeName = "My Custom",
            CustomModes =
            [
                new CorrectionMode { Name = "My Custom", SystemPrompt = "Do custom stuff" },
            ],
        };
        var mode = settings.GetActiveMode();
        Assert.Equal("My Custom", mode.Name);
        Assert.Equal("Do custom stuff", mode.SystemPrompt);
    }

    [Fact]
    public void GetActiveMode_DefaultsWin_OverCustom_WhenNameMatchesBoth()
    {
        var settings = new AppSettings
        {
            ActiveModeName = "Fix errors",
            CustomModes =
            [
                new CorrectionMode { Name = "Fix errors", SystemPrompt = "custom override" },
            ],
        };
        var mode = settings.GetActiveMode();
        Assert.NotEqual("custom override", mode.SystemPrompt);
    }

    [Fact]
    public async Task RoundTrip_PreservesCustomModes()
    {
        var original = new AppSettings
        {
            CustomModes =
            [
                new CorrectionMode { Name = "Sarcastic", SystemPrompt = "Make it sarcastic" },
                new CorrectionMode { Name = "Pirate", SystemPrompt = "Talk like a pirate" },
            ],
        };
        var path = Path.Combine(_tempDir, "custom_modes.json");

        await original.SaveAsync(path);
        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(2, loaded.CustomModes.Count);
        Assert.Equal("Sarcastic", loaded.CustomModes[0].Name);
        Assert.Equal("Talk like a pirate", loaded.CustomModes[1].SystemPrompt);
    }

    [Fact]
    public void AllModes_ReturnsBothDefaultsAndCustom()
    {
        var settings = new AppSettings
        {
            CustomModes =
            [
                new CorrectionMode { Name = "Custom1", SystemPrompt = "test" },
            ],
        };
        var all = settings.AllModes();
        Assert.Equal(CorrectionMode.Defaults.Count + 1, all.Count);
        Assert.Equal("Custom1", all[^1].Name);
    }

    [Fact]
    public void LogLevel_DefaultsToWarn()
    {
        var settings = new AppSettings();
        Assert.Equal("Warn", settings.LogLevel);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyKeyAndModelToAnthropicProvider()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new AppSettings { Model = "claude-opus-4-6" };
            legacy.SetApiKey("sk-ant-legacy");
            await legacy.SaveAsync(path);

            var loaded = await AppSettings.LoadAsync(path);

            var anthropic = loaded.GetProviderConfig("anthropic");
            Assert.Equal("sk-ant-legacy", anthropic.GetApiKey());
            Assert.Equal("claude-opus-4-6", anthropic.Model);
            Assert.Equal("anthropic", loaded.ActiveProviderId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_MigrationIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new AppSettings { Model = "claude-opus-4-6" };
            legacy.SetApiKey("sk-ant-legacy");
            await legacy.SaveAsync(path);

            var first = await AppSettings.LoadAsync(path);
            await first.SaveAsync(path);
            var second = await AppSettings.LoadAsync(path);

            // A second load must not append a duplicate anthropic entry.
            Assert.Single(second.Providers, p => p.Id == "anthropic");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ProviderConfig_ApiKeyRoundTripsThroughDpapi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            settings.GetProviderConfig("openai").SetApiKey("sk-openai-secret");
            await settings.SaveAsync(path);

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("sk-openai-secret", raw);

            var loaded = await AppSettings.LoadAsync(path);
            Assert.Equal("sk-openai-secret", loaded.GetProviderConfig("openai").GetApiKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void GetProviderConfig_CreatesAndReusesEntry()
    {
        var settings = new AppSettings();

        var first = settings.GetProviderConfig("ollama");
        first.Model = "llama3.2:3b";
        var second = settings.GetProviderConfig("ollama");

        Assert.Same(first, second);
        Assert.Equal("llama3.2:3b", second.Model);
    }

    [Fact]
    public void ActiveProvider_FollowsActiveProviderId()
    {
        var settings = new AppSettings { ActiveProviderId = "ollama" };
        settings.GetProviderConfig("ollama").Model = "qwen2.5:3b";

        Assert.Equal("qwen2.5:3b", settings.ActiveProvider.Model);
    }

    [Fact]
    public void ActiveProvider_UnknownId_ResolvesThroughThePresetLikeTheFactoryDoes()
    {
        // ProviderFactory looks the config up by ProviderPresets.Get(id).Id, which falls
        // back to Anthropic. Resolving the raw id here instead would hand back — and
        // persist — a stray config while corrections ran from the Anthropic one.
        var settings = new AppSettings { ActiveProviderId = "groq" };
        settings.GetProviderConfig(ProviderPresets.AnthropicId).Model = "claude-sonnet-4-6";

        Assert.Equal(ProviderPresets.AnthropicId, settings.ActiveProvider.Id);
        Assert.Equal("claude-sonnet-4-6", settings.ActiveProvider.Model);
        Assert.DoesNotContain(settings.Providers, p => p.Id == "groq");
    }
}
