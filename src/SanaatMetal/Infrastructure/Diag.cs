using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;

namespace SanaatMetal.Infrastructure;

/// <summary>
/// وضعیت اجرای برنامه و آخرین خطاها برای صفحه عیب‌یابی /diag.
/// خطاها در حافظه هم نگه داشته می‌شوند تا اگر هاست اجازه نوشتن فایل ندهد، باز هم دیده شوند.
/// </summary>
public static class Diag
{
    public static string LogDir { get; set; } = "";
    public static string KeysDir { get; set; } = "";
    public static string? KeysConfigured { get; set; }
    public static DateTime StartedAt { get; } = DateTime.Now;
    public static string? LastLogWriteError { get; set; }

    public static string Build
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var built = File.Exists(asm.Location) ? File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm") : "?";
            return $"diag-2 / built {built}";
        }
    }

    public record Entry(DateTime At, string Level, string Source, string Message, string? Error);

    private static readonly ConcurrentQueue<Entry> Recent = new();

    public static void Add(string level, string source, string message, Exception? ex)
    {
        Recent.Enqueue(new Entry(DateTime.Now, level, source, message, ex?.ToString()));
        while (Recent.Count > 25 && Recent.TryDequeue(out _)) { }
    }

    public static IEnumerable<Entry> Entries => Recent.Reverse();

    private static string Check(Func<string> f)
    {
        try { return "OK  " + f(); }
        catch (Exception ex) { return "FAIL  " + ex.GetType().Name + ": " + ex.Message + "\n        " + (ex.InnerException?.Message ?? ""); }
    }

    private static async Task<string> CheckAsync(Func<Task<string>> f)
    {
        try { return "OK  " + await f(); }
        catch (Exception ex) { return "FAIL  " + ex.GetType().Name + ": " + ex.Message + "\n        " + (ex.InnerException?.Message ?? ""); }
    }

    /// <summary>
    /// صفحه متنی /diag: نسخه برنامه، مسیرها، تست کلیدها، فرم‌ها و دیتابیس، و آخرین خطاها.
    /// رمز و رشته اتصال نمایش داده نمی‌شود. با "Diagnostics:DiagPage": false خاموش می‌شود.
    /// </summary>
    public static void MapDiag(this WebApplication app)
    {
        app.MapGet("/diag", async (HttpContext ctx, IServiceProvider sp, IConfiguration config, IWebHostEnvironment env) =>
        {
            if (config["Diagnostics:DiagPage"] is "false" or "False") return Results.NotFound();

            var sb = new StringBuilder();
            sb.AppendLine("SanaatMetal diagnostics");
            sb.AppendLine("=======================");
            sb.AppendLine($"Build:          {Build}");
            sb.AppendLine($"Started:        {StartedAt:yyyy-MM-dd HH:mm:ss}   Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Environment:    {env.EnvironmentName}");
            sb.AppendLine($".NET runtime:   {RuntimeInformation.FrameworkDescription}  ({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture})");
            sb.AppendLine($"Process user:   {Environment.UserDomainName}\\{Environment.UserName}");
            sb.AppendLine($"ContentRoot:    {env.ContentRootPath}");
            sb.AppendLine($"Culture:        {CultureInfo.CurrentCulture.Name} / UI {CultureInfo.CurrentUICulture.Name}");
            sb.AppendLine($"fa-IR calendar: {Check(() => new CultureInfo("fa-IR").DateTimeFormat.Calendar.GetType().Name)}");
            sb.AppendLine();
            sb.AppendLine($"Log folder:     {LogDir}");
            sb.AppendLine($"  writable:     {(WritableDir.TryUse(LogDir) is null ? "NO" : "yes")}");
            if (LastLogWriteError is not null) sb.AppendLine($"  last write error: {LastLogWriteError}");
            sb.AppendLine($"Keys folder:    {KeysDir}{(string.IsNullOrEmpty(KeysConfigured) ? "" : $"   (configured: {KeysConfigured})")}");
            sb.AppendLine($"  writable:     {(WritableDir.TryUse(KeysDir) is null ? "NO" : "yes")}");
            sb.AppendLine($"uploads folder: {(WritableDir.TryUse(Path.Combine(env.WebRootPath ?? "", "uploads")) is null ? "NOT writable" : "writable")}");
            sb.AppendLine();
            sb.AppendLine("Checks");
            sb.AppendLine("------");
            sb.AppendLine($"Data protection (keys):  {Check(() => { var p = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("diag"); return p.Unprotect(p.Protect("x")) == "x" ? "protect/unprotect works" : "mismatch"; })}");
            sb.AppendLine($"Antiforgery (forms):     {Check(() => { var t = sp.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx); return "token created (" + (t.RequestToken?.Length ?? 0) + " chars)"; })}");
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                sb.AppendLine($"Database connect:        {await CheckAsync(async () => await db.Database.CanConnectAsync() ? "connected" : "cannot connect")}");
                sb.AppendLine($"Pending migrations:      {await CheckAsync(async () => { var p = (await db.Database.GetPendingMigrationsAsync()).ToList(); return p.Count == 0 ? "none" : string.Join(", ", p); })}");
                sb.AppendLine($"Site settings row:       {await CheckAsync(async () => (await db.SiteSettings.AnyAsync()) ? "present" : "MISSING")}");
            }
            sb.AppendLine();
            sb.AppendLine("Recent errors (newest first)");
            sb.AppendLine("----------------------------");
            var any = false;
            foreach (var e in Entries)
            {
                any = true;
                sb.AppendLine($"[{e.At:yyyy-MM-dd HH:mm:ss}] {e.Level} {e.Source}");
                sb.AppendLine("  " + e.Message);
                if (e.Error is not null) sb.AppendLine(string.Join("\n", e.Error.Split('\n').Take(25)));
                sb.AppendLine();
            }
            if (!any) sb.AppendLine("(none since start; open the page that fails, then reload this page)");

            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
        });
    }
}

/// <summary>نگهداری کلیدهای امنیتی در حافظه وقتی هیچ پوشه قابل‌نوشتنی روی هاست نیست.</summary>
public sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> _items = new();
    public IReadOnlyCollection<XElement> GetAllElements() { lock (_items) return _items.Select(e => new XElement(e)).ToList(); }
    public void StoreElement(XElement element, string friendlyName) { lock (_items) _items.Add(new XElement(element)); }
}
