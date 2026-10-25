using System.Text;
using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;

namespace Clenzy.Core.Services;

/// <summary>Removes user-identifying path prefixes so shared logs and reports contain less personal information.</summary>
public sealed class LogSanitizer
{
    private readonly string? _profile;

    public LogSanitizer(string? userProfile)
    {
        _profile = string.IsNullOrWhiteSpace(userProfile) ? null : PathGuard.TrimTrailingSeparator(userProfile);
    }

    public string Redact(string text)
    {
        if (_profile is null || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace(_profile, "%USERPROFILE%", PathGuard.Comparison);
    }
}

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
    public void Audit(string message) { }
}

/// <summary>
/// Rotating plain-text logger. Two streams are kept: <c>clenzy-*.log</c> for diagnostics and
/// <c>audit-*.log</c> for deletions and registry changes. Files roll by size and old files are removed.
/// </summary>
public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFilesPerStream = 8;

    private readonly string _directory;
    private readonly LogSanitizer _sanitizer;
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;
    private StreamWriter? _app;
    private StreamWriter? _audit;
    private string? _appPath;
    private string? _auditPath;
    private bool _disposed;

    public FileAppLogger(AppDataLayout layout, LogSanitizer sanitizer, Func<DateTime>? now = null)
    {
        _directory = layout.LogsDirectory;
        _sanitizer = sanitizer;
        _now = now ?? (() => DateTime.Now);
        try
        {
            Directory.CreateDirectory(_directory);
            Prune("clenzy-*.log");
            Prune("audit-*.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never prevent the application from starting.
        }
    }

    public string LogsDirectory => _directory;

    public void Info(string message) => Write(isAudit: false, "INFO", message);

    public void Warning(string message) => Write(isAudit: false, "WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} ({exception.GetType().Name}: {exception.Message})";
        Write(isAudit: false, "ERROR", text);
    }

    public void Audit(string message) => Write(isAudit: true, "AUDIT", message);

    private void Write(bool isAudit, string level, string message)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var writer = GetWriter(isAudit);
                writer.WriteLine($"{_now():yyyy-MM-dd HH:mm:ss.fff} [{level}] {_sanitizer.Redact(message)}");
                writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // Never let logging failures break a cleaning run.
            }
        }
    }

    private StreamWriter GetWriter(bool isAudit)
    {
        var current = isAudit ? _audit : _app;
        var currentPath = isAudit ? _auditPath : _appPath;
        var prefix = isAudit ? "audit" : "clenzy";

        if (current is not null && currentPath is not null && new FileInfo(currentPath).Length < MaxFileBytes &&
            currentPath.Contains(_now().ToString("yyyyMMdd"), StringComparison.Ordinal))
        {
            return current;
        }

        current?.Dispose();
        var stamp = _now().ToString("yyyyMMdd");
        string path;
        var index = 0;
        do
        {
            path = Path.Combine(_directory, index == 0 ? $"{prefix}-{stamp}.log" : $"{prefix}-{stamp}.{index}.log");
            index++;
        }
        while (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes);

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, new UTF8Encoding(false));
        if (isAudit)
        {
            _audit = writer;
            _auditPath = path;
        }
        else
        {
            _app = writer;
            _appPath = path;
        }

        Prune($"{prefix}-*.log");
        return writer;
    }

    private void Prune(string pattern)
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var old in files.Skip(MaxFilesPerStream))
            {
                try
                {
                    old.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip files that are still open.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore.
        }
    }

    /// <summary>Deletes every Clenzy log file (used by "Clear application history").</summary>
    public void ClearLogs()
    {
        lock (_gate)
        {
            _app?.Dispose();
            _audit?.Dispose();
            _app = null;
            _audit = null;
            _appPath = null;
            _auditPath = null;
            foreach (var file in Directory.EnumerateFiles(_directory, "*.log"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore files that are in use.
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _app?.Dispose();
            _audit?.Dispose();
        }
    }
}
