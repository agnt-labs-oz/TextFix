using System.IO;
using TextFix.Models;

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
        original.KeepOverlayOpen = true;
        original.StartWithWindows = true;

        var path = Path.Combine(_tempDir, "settings.json");

        await original.SaveAsync(path);
        var loaded = await AppSettings.LoadAsync(path);

        Assert.Equal(original.GetApiKey(), loaded.GetApiKey());
        Assert.Equal(original.Hotkey, loaded.Hotkey);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.OverlayAutoApplySeconds, loaded.OverlayAutoApplySeconds);
        Assert.Equal(original.KeepOverlayOpen, loaded.KeepOverlayOpen);
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
}
