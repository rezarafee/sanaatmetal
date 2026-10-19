using System.Security.Claims;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

public class SettingsService(AppDbContext db, IMemoryCache cache, IDataProtectionProvider dp)
{
    private const string Key = "site-settings";
    private readonly IDataProtector _protector = dp.CreateProtector("SanaatMetal.Smtp");

    public async Task<SiteSettings> GetAsync() =>
        (await cache.GetOrCreateAsync(Key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SiteSettings();
        }))!;

    public void Invalidate() => cache.Remove(Key);

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); } catch { return null; }
    }
}

public enum UploadKind { Image, Document }

public class FileStorageService(IWebHostEnvironment env, ILogger<FileStorageService> log, IServiceScopeFactory scopes)
{
    // اگر یک بار نوشتن روی دیسک ممکن نشد، تا ری‌استارت برنامه فایل‌ها مستقیم در دیتابیس ذخیره می‌شوند
    private static volatile bool _diskUnavailable;
    public static bool DiskUnavailable => _diskUnavailable;

    public static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".pdf"] = "application/pdf", [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".zip"] = [[0x50, 0x4B, 0x03, 0x04]],
    };

    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] DocExt = [".pdf", ".jpg", ".jpeg", ".png", ".docx", ".xlsx", ".zip"];
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxDocBytes = 25 * 1024 * 1024;

    public string AllowedFor(UploadKind kind) => string.Join(",", kind == UploadKind.Image ? ImageExt : DocExt);

    /// <summary>ذخیره امن فایل: بررسی پسوند، حجم و امضای فایل، نام تصادفی</summary>
    public async Task<(string? Path, string? Error)> SaveAsync(IFormFile? file, string folder, UploadKind kind)
    {
        if (file is null || file.Length == 0) return (null, "فایلی انتخاب نشده است.");
        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = kind == UploadKind.Image ? ImageExt : DocExt;
        if (!allowed.Contains(ext)) return (null, $"نوع فایل مجاز نیست. پسوندهای مجاز: {string.Join("، ", allowed)}");
        var max = kind == UploadKind.Image ? MaxImageBytes : MaxDocBytes;
        if (file.Length > max) return (null, $"حجم فایل بیشتر از {max / 1024 / 1024} مگابایت است.");

        await using (var s = file.OpenReadStream())
        {
            var header = new byte[8];
            var read = await s.ReadAsync(header);
            if (!Signatures[ext].Any(sig => read >= sig.Length && header.Take(sig.Length).SequenceEqual(sig)))
                return (null, "محتوای فایل با پسوند آن مطابقت ندارد.");
        }

        var safeFolder = string.Concat(folder.Where(char.IsLetterOrDigit));
        var rel = $"uploads/{safeFolder}/{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:MM}";
        var name = $"{Guid.NewGuid():N}{ext}";
        var webPath = $"/{rel}/{name}";
        if (!_diskUnavailable)
        {
            var dir = System.IO.Path.Combine(env.WebRootPath, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(dir);
                await using (var fs = new FileStream(System.IO.Path.Combine(dir, name), FileMode.CreateNew))
                    await file.CopyToAsync(fs);
                return (webPath, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // روی هاست اشتراکی (Plesk) برنامه گاهی اجازه نوشتن در پوشه سایت را ندارد؛ فایل در دیتابیس ذخیره می‌شود
                _diskUnavailable = true;
                log.LogWarning(ex, "Upload folder not writable ({Dir}); storing uploads in the database from now on", dir);
            }
        }

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            // context جدا تا تغییرات ذخیره‌نشده صفحه فعلی همراه فایل ذخیره نشوند
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.StoredFiles.Add(new StoredFile { Path = webPath, ContentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream"), Data = ms.ToArray(), Size = ms.Length });
            await db.SaveChangesAsync();
            return (webPath, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Saving upload to the database failed: {Path}", webPath);
            return (null, "ذخیره فایل ممکن نشد. دوباره تلاش کنید؛ اگر تکرار شد کد خطا را از صفحه /diag بفرستید.");
        }
    }

    public void Delete(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath) || !webPath.StartsWith("/uploads/")) return;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(env.WebRootPath, webPath.TrimStart('/')));
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(env.WebRootPath, "uploads"));
        // اگر فایل قدیمی حذف نشد، فقط در لاگ ثبت می‌شود و عملیات اصلی (مثلاً جایگزینی عکس) ادامه پیدا می‌کند
        try { if (full.StartsWith(root) && File.Exists(full)) File.Delete(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Deleting upload failed: {Path}", full); }
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().StoredFiles.Where(f => f.Path == webPath).ExecuteDelete();
        }
        catch (Exception ex) { log.LogWarning(ex, "Deleting stored upload failed: {Path}", webPath); }
    }
}

public class EmailService(SettingsService settings, ILogger<EmailService> log)
{
    public async Task<(bool Ok, string? Error)> SendAsync(string to, string subject, string htmlBody, string? replyTo = null)
    {
        var s = await settings.GetAsync();
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.SmtpFrom))
            return (false, "تنظیمات SMTP وارد نشده است.");
        try
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(s.SmtpFrom));
            msg.To.Add(MailboxAddress.Parse(to));
            if (!string.IsNullOrWhiteSpace(replyTo) && MailboxAddress.TryParse(replyTo, out var rt)) msg.ReplyTo.Add(rt);
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 15000 };
            var opt = s.SmtpUseSsl ? (s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.Auto;
            await client.ConnectAsync(s.SmtpHost, s.SmtpPort, opt);
            var pass = settings.Unprotect(s.SmtpPasswordProtected);
            if (!string.IsNullOrWhiteSpace(s.SmtpUser)) await client.AuthenticateAsync(s.SmtpUser, pass ?? "");
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            return (true, null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Email send failed");
            return (false, ex.Message.Length > 900 ? ex.Message[..900] : ex.Message);
        }
    }
}

public class AuditService(AppDbContext db, IHttpContextAccessor http)
{
    public void Log(string action, string? entity = null, object? entityId = null, string? details = null)
    {
        var ctx = http.HttpContext;
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entity,
            EntityId = entityId?.ToString(),
            Details = details is { Length: > 2000 } ? details[..2000] : details,
            UserName = ctx?.User.FindFirstValue("FullName") ?? ctx?.User.Identity?.Name,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}

public static class UserExtensions
{
    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue("FullName") is { Length: > 0 } n ? n : user.Identity?.Name ?? "";
}
