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

    /// <summary>اولین پوشه قابل‌نوشتن از میان گزینه‌ها، بعد پوشه موقت سیستم؛ اگر هیچ‌کدام نشد null.</summary>
    public static string? Pick(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (TryUse(c) is { } ok) return ok;
        return TryUse(Path.Combine(Path.GetTempPath(), "SanaatMetal"));
    }

    /// <summary>
    /// پوشه خصوصی داخل wwwroot برای هاست‌هایی که فقط wwwroot قابل نوشتن است.
    /// درخواست‌های وب به این مسیر با PrivateDataMiddleware رد می‌شوند.
    /// </summary>
    public const string PrivateSegment = "_private";
}

/// <summary>هر درخواست به /_private (لاگ و کلیدها) را با 404 رد می‌کند تا از وب قابل دانلود نباشد.</summary>
public sealed class PrivateDataMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/" + WritableDir.PrivateSegment, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(ctx);
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

    public FileLoggerProvider(string? dir, LogLevel min)
    {
        _dir = dir ?? Path.Combine(Path.GetTempPath(), "SanaatMetal");
        _min = min;
    }

    public string Directory => _dir;

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));

    internal void Write(LogLevel level, string category, string message, Exception? ex)
    {
        if (level < _min) return;
        if (level >= LogLevel.Error) Diag.Add(level.ToString(), category, message, ex);
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(level).Append("] ").Append(category).AppendLine();
        sb.Append("  ").AppendLine(message);
        if (ex is not null) sb.AppendLine(ex.ToString());
        sb.AppendLine();
        lock (_lock)
        {
            // اگر پوشه اصلی قابل نوشتن نبود، پوشه موقت سیستم امتحان می‌شود؛ لاگ نباید خودش باعث خطای سایت شود
            foreach (var dir in new[] { _dir, Path.Combine(Path.GetTempPath(), "SanaatMetal") })
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, $"errors-{DateTime.Now:yyyy-MM-dd}.log"), sb.ToString(), Encoding.UTF8);
                    return;
                }
                catch (Exception e)
                {
                    Diag.LastLogWriteError = $"{dir}: {e.Message}";
                }
            }
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
