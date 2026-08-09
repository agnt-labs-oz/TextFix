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
    public void SessionHeader_IsWrittenOnceBeforeTheFirstSurvivingLine()
    {
        // The startup banner is Info, so at the default Warn level an error-only log
        // carried no build identity at all — which is what made a "TextFix is broken"
        // report impossible to place against a version.
        var log = new AppLog(_logDir, AppLog.Level.Warn, "TextFix 9.9.9 started");

        log.Info("dropped");   // below the level — must not trigger the header either
        log.Warn("first");
        log.Warn("second");

        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log"));
        var lines = contents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("=== TextFix 9.9.9 started", lines[0]);
        Assert.Single(lines, l => l.StartsWith("==="));
        Assert.Contains("first", lines[1]);
    }

    [Fact]
    public void SessionHeader_IsRepeated_InEachDaysFile()
    {
        // TextFix launches at login and runs for days. A once-per-process header would
        // land in day one's file and leave every later file unattributed — which is the
        // case that needs it most, since a long-running instance is exactly where you
        // cannot remember which build is loaded.
        var now = new DateTime(2026, 8, 10, 23, 59, 0, DateTimeKind.Utc);
        var log = new AppLog(_logDir, AppLog.Level.Warn, "TextFix 9.9.9 started", () => now);

        log.Warn("before midnight");
        now = now.AddMinutes(2); // 2026-08-11
        log.Warn("after midnight");

        var day1 = File.ReadAllText(Path.Combine(_logDir, "textfix-2026-08-10.log"));
        var day2 = File.ReadAllText(Path.Combine(_logDir, "textfix-2026-08-11.log"));
        Assert.StartsWith("=== TextFix 9.9.9 started", day1);
        Assert.StartsWith("=== TextFix 9.9.9 started", day2);
        Assert.Contains("after midnight", day2);
        Assert.DoesNotContain("after midnight", day1);
    }

    [Fact]
    public void SessionHeader_IsOmitted_WhenNotSupplied()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);
        log.Warn("only");

        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log"));
        Assert.DoesNotContain("===", contents);
    }

    [Fact]
    public void Warn_WithException_RecordsTypeAndMessage()
    {
        var log = new AppLog(_logDir, AppLog.Level.Warn);

        log.Warn("provider failed", new InvalidOperationException("boom"));

        var contents = File.ReadAllText(Path.Combine(_logDir, $"textfix-{DateTime.UtcNow:yyyy-MM-dd}.log"));
        Assert.Contains("provider failed", contents);
        Assert.Contains("System.InvalidOperationException", contents);
        Assert.Contains("boom", contents);
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
