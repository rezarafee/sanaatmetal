using System.Collections.Concurrent;
using System.Text;

namespace SanaatMetal.Infrastructure;

/// <summary>
/// پیدا کردن پوشه‌ای که برنامه واقعاً اجازه نوشتن در آن را دارد.
/// روی هاست اشتراکی (Plesk) گاهی پوشه تنظیم‌شده وجود ندارد یا دسترسی نوشتن ندارد.
/// </summary>
public static class WritableDir
{
    public static string? TryUse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>اولین پوشه قابل‌نوشتن از میان گزینه‌ها؛ آخرین گزینه پوشه موقت سیستم است.</summary>
    public static string Pick(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (TryUse(c) is { } ok) return ok;
        return TryUse(Path.Combine(Path.GetTempPath(), "SanaatMetal")) ?? Path.GetTempPath();
    }
}

/// <summary>
/// لاگ ساده در فایل متنی: logs/errors-yyyy-MM-dd.log کنار برنامه.
/// روی Plesk از File Manager قابل باز کردن است و از وب قابل دسترسی نیست (فقط wwwroot عمومی است).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _min;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string dir, LogLevel min)
    {
        _dir = dir;
        _min = min;
    }

    public string Directory => _dir;

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));

    internal void Write(LogLevel level, string category, string message, Exception? ex)
    {
        if (level < _min) return;
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(level).Append("] ").Append(category).AppendLine();
        sb.Append("  ").AppendLine(message);
        if (ex is not null) sb.AppendLine(ex.ToString());
        sb.AppendLine();
        try
        {
            lock (_lock)
            {
                var file = Path.Combine(_dir, $"errors-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // لاگ نباید خودش باعث خطای سایت شود
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._min && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
