using System.IO;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class AppLogTests : IDisposable
{
    private readonly string _logDir;

    public AppLogTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), $"TextFixLogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Write_CreatesFileNamedByDate()
    {
        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("hello");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Info_AtWarnLevel_DoesNotWrite()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);
        log.Info("ignored");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log");
        Assert.False(File.Exists(expected));
    }

    [Fact]
    public void Warn_AtWarnLevel_Writes()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);
        log.Warn("kept");
        var expected = Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log");
        var contents = File.ReadAllText(expected);
        Assert.Contains("[WARN]", contents);
        Assert.Contains("kept", contents);
    }

    [Fact]
    public void Error_WritesStackTraceWhenExceptionProvided()
    {
        var log = new AppLog(_logDir, AppLog.Level.Info);
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { log.Error("context", ex); }

        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log"));
        Assert.Contains("[ERROR]", contents);
        Assert.Contains("context", contents);
        Assert.Contains("InvalidOperationException", contents);
    }

    [Fact]
    public void Write_LineIncludesThreadId()
    {
        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("hello");
        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log"));
        Assert.Matches(@"\[T\s*\d+\]", contents);
    }

    [Fact]
    public void Cleanup_DeletesFilesOlderThan7Days()
    {
        var oldDate = DateTime.UtcNow.AddDays(-10);
        var oldFile = Path.Combine(_logDir, $"textfix-{oldDate:yyyy-MM-dd}.log");
        File.WriteAllText(oldFile, "ancient");
        File.SetLastWriteTimeUtc(oldFile, oldDate);

        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("today");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void Cleanup_KeepsFilesWithinLast7Days()
    {
        var recentDate = DateTime.UtcNow.AddDays(-3);
        var recentFile = Path.Combine(_logDir, $"textfix-{recentDate:yyyy-MM-dd}.log");
        File.WriteAllText(recentFile, "recent");
        File.SetLastWriteTimeUtc(recentFile, recentDate);

        var log = new AppLog(_logDir, AppLog.Level.Info);
        log.Info("today");

        Assert.True(File.Exists(recentFile));
    }
}
