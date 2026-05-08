using System.IO;

namespace TextFix.Services;

public sealed class AppLog
{
    public enum Level { Info, Warn, Error }

    private readonly string _dir;
    private readonly Level _minLevel;
    private readonly object _gate = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public AppLog(string directory, Level minLevel)
    {
        _dir = directory;
        _minLevel = minLevel;
    }

    public string LogDirectory => _dir;

    public void Info(string message) => Write(Level.Info, message, null);
    public void Warn(string message) => Write(Level.Warn, message, null);
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
                var todayUtc = DateTime.UtcNow.Date;
                if (_lastCleanupDate < todayUtc)
                {
                    CleanupOldLogs();
                    _lastCleanupDate = todayUtc;
                }

                var path = Path.Combine(_dir, $"textfix-{todayUtc:yyyy-MM-dd}.log");
                var line = $"[{DateTime.UtcNow:o}] [T{Environment.CurrentManagedThreadId,3}] [{level.ToString().ToUpperInvariant()}] {message}";
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
