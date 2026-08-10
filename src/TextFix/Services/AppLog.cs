using System.IO;

namespace TextFix.Services;

public sealed class AppLog
{
    public enum Level { Info, Warn, Error }

    private readonly string _dir;
    private readonly Level _minLevel;
    private readonly string? _sessionInfo;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;
    private DateTime _headerDate = DateTime.MinValue;

    /// <param name="utcNow">
    /// Seam for tests only. Three behaviours here turn on the date — the filename, the
    /// 7-day cleanup, and the build header — and none of them are observable without
    /// being able to move the clock past midnight.
    /// </param>
    public AppLog(string directory, Level minLevel, string? sessionInfo = null, Func<DateTime>? utcNow = null)
    {
        _dir = directory;
        _minLevel = minLevel;
        _sessionInfo = sessionInfo;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string LogDirectory => _dir;

    public void Info(string message) => Write(Level.Info, message, null);
    public void Warn(string message, Exception? ex = null) => Write(Level.Warn, message, ex);
    public void Error(string message, Exception? ex = null) => Write(Level.Error, message, ex);

    private void Write(Level level, string message, Exception? ex)
    {
        if (level < _minLevel) return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_dir);

                // Use UTC for filenames, line timestamps, and retention so dates always agree
                // and DST/clock-skew can't shift records into the wrong file.
                var now = _utcNow();
                var todayUtc = now.Date;
                if (_lastCleanupDate < todayUtc)
                {
                    CleanupOldLogs();
                    _lastCleanupDate = todayUtc;
                }

                var path = Path.Combine(_dir, $"textfix-{todayUtc:yyyy-MM-dd}.log");

                // Stamp the build on the first line this process contributes to each day's
                // file. The startup banner is Info and so is dropped at the default Warn
                // level, which used to leave error-only logs with no way to tell which
                // build wrote them — the ambiguity that makes a bug report unanswerable.
                //
                // Tracked per date, not once per process: TextFix launches at login and
                // runs for days, so a once-only header would land in day one's file and
                // leave every later file unattributed — the exact case that needs it most.
                if (_headerDate != todayUtc)
                {
                    _headerDate = todayUtc;
                    if (!string.IsNullOrWhiteSpace(_sessionInfo))
                        File.AppendAllText(path, $"=== {_sessionInfo} ===" + Environment.NewLine);
                }

                var line = $"[{now:o}] [T{Environment.CurrentManagedThreadId,3}] [{level.ToString().ToUpperInvariant()}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + FormatException(ex);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    /// <summary>
    /// Avoids <c>Exception.ToString()</c> because some SDK exceptions (notably
    /// HTTP-backed ones) round-trip request metadata — including authorization
    /// headers — into their full string form. We log only the type, message,
    /// inner-exception chain, and stack trace.
    /// </summary>
    private static string FormatException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var current = ex;
        var depth = 0;
        while (current is not null && depth < 5)
        {
            if (depth > 0) sb.Append(" --> ");
            sb.Append(current.GetType().FullName).Append(": ").AppendLine(current.Message);
            current = current.InnerException;
            depth++;
        }
        if (ex.StackTrace is not null) sb.AppendLine(ex.StackTrace);
        return sb.ToString().TrimEnd();
    }

    private void CleanupOldLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(_dir, "textfix-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { /* best effort */ }
        }
    }
}
