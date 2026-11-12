using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

/// <summary>مشتریان ما (لوگوی پالایشگاه‌ها و شرکت‌ها در اسلایدر صفحه اصلی)</summary>
[HasPermission(Permissions.Clients)]
public class ClientsController(AppDbContext db, FileStorageService files, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index() =>
        View(await db.Clients.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.NameFa).ToListAsync());

    public async Task<IActionResult> Edit(int? id)
    {
        var c = id is null ? new Client { SortOrder = await db.Clients.CountAsync() + 1 } : await db.Clients.FindAsync(id);
        return c is null ? NotFound() : View(c);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int? id, [Bind("NameFa,NameEn,Website,Industry,Description,SortOrder,IsActive")] Client input, IFormFile? logo)
    {
        var c = id is null or 0 ? new Client() : await db.Clients.FindAsync(id);
        if (c is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(input.Website) && !(Uri.TryCreate(input.Website, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))
            ModelState.AddModelError("Website", "آدرس وب‌سایت باید با http یا https شروع شود.");
        if (logo is { Length: > 0 })
        {
            var (path, err) = await files.SaveAsync(logo, "clients", UploadKind.Image, ImageProfile.ClientLogo);
            if (err is not null) ModelState.AddModelError("", err);
            else { files.Delete(c.LogoPath); c.LogoPath = path; }
        }
        if (!ModelState.IsValid) { input.Id = c.Id; input.LogoPath = c.LogoPath; return View(input); }
        c.NameFa = input.NameFa.Trim(); c.NameEn = input.NameEn?.Trim(); c.Website = input.Website?.Trim(); c.Industry = input.Industry;
        c.Description = input.Description; c.SortOrder = input.SortOrder; c.IsActive = input.IsActive; c.UpdatedAt = DateTime.UtcNow;
        if (c.Id == 0) db.Clients.Add(c);
        await db.SaveChangesAsync();
        audit.Log(id is null ? "افزودن مشتری (اسلایدر)" : "ویرایش مشتری (اسلایدر)", "Client", c.Id, c.NameFa);
        await db.SaveChangesAsync();
        Success("اطلاعات مشتری ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await db.Clients.FindAsync(id);
        if (c is null) return NotFound();
        db.Clients.Remove(c);
        audit.Log("حذف مشتری (اسلایدر)", "Client", id, c.NameFa);
        await db.SaveChangesAsync();
        files.Delete(c.LogoPath);
        Success("مشتری حذف شد.");
        return RedirectToAction(nameof(Index));
    }
}

[HasPermission(Permissions.MessagesView)]
public class MessagesController(AppDbContext db, EmailService email, SettingsService settings, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, string? filter, int page = 1)
    {
        var query = db.ContactMessages.AsNoTracking().AsQueryable();
        query = filter switch
        {
            "unread" => query.Where(m => !m.IsRead && !m.IsArchived),
            "failed" => query.Where(m => !m.EmailSent && !m.IsArchived),
            "archived" => query.Where(m => m.IsArchived),
            _ => query.Where(m => !m.IsArchived),
        };
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.Name.Contains(q) || m.Email.Contains(q) || (m.Subject != null && m.Subject.Contains(q)) || m.Body.Contains(q) || (m.Company != null && m.Company.Contains(q)));
        const int size = 25;
        var total = await query.CountAsync();
        ViewBag.Q = q; ViewBag.Filter = filter; ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        return View(await query.OrderByDescending(m => m.Id).Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var m = await db.ContactMessages.FindAsync(id);
        if (m is null) return NotFound();
        if (!m.IsRead) { m.IsRead = true; await db.SaveChangesAsync(); }
        return View(m);
    }

    [HttpPost, HasPermission(Permissions.MessagesManage)]
    public async Task<IActionResult> Action(int id, string op, string? staffNote)
    {
        var m = await db.ContactMessages.FindAsync(id);
        if (m is null) return NotFound();
        switch (op)
        {
            case "archive": m.IsArchived = !m.IsArchived; break;
            case "unread": m.IsRead = false; break;
            case "note": m.StaffNote = staffNote; break;
            case "resend":
                var s = await settings.GetAsync();
                var to = s.NotifyEmail ?? s.Email;
                if (string.IsNullOrWhiteSpace(to)) { Error("ایمیل دریافت‌کننده در تنظیمات وارد نشده است."); break; }
                var e = (string? v) => WebUtility.HtmlEncode(v ?? "-");
                var (ok, err) = await email.SendAsync(to, "درخواست از سایت: " + (m.Subject ?? m.Name),
                    $"<div dir='rtl' style='font-family:Tahoma;line-height:2'><b>نام:</b> {e(m.Name)}<br><b>ایمیل:</b> {e(m.Email)}<br><b>تلفن:</b> {e(m.Phone)}<br><b>شرکت:</b> {e(m.Company)}<p style='white-space:pre-wrap'>{e(m.Body)}</p></div>", m.Email);
                m.EmailSent = ok; m.EmailError = err;
                if (ok) Success("ایمیل با موفقیت ارسال شد."); else Error("ارسال ناموفق: " + err);
                break;
            case "delete":
                db.ContactMessages.Remove(m);
                audit.Log("حذف پیام", "ContactMessage", id, m.Email);
                await db.SaveChangesAsync();
                Success("پیام حذف شد.");
                return RedirectToAction(nameof(Index));
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }
}

[HasPermission(Permissions.Settings)]
public class SettingsController(AppDbContext db, SettingsService settings, FileStorageService files, EmailService email, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index() => View(await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SiteSettings());

    [HttpPost]
    public async Task<IActionResult> Index(SiteSettings input, string? smtpPassword)
    {
        var s = await db.SiteSettings.FirstOrDefaultAsync();
        if (s is null) { s = new SiteSettings(); db.SiteSettings.Add(s); }
        var keepPassword = s.SmtpPasswordProtected;
        // ظاهر سایت و متن‌ها در صفحه‌های جداگانه ویرایش می‌شوند و این فرم نباید آن‌ها را پاک کند
        foreach (var prop in AppearanceProps) prop.SetValue(input, prop.GetValue(s));
        var isSuper = User.IsInRole(Roles.SuperAdmin);
        if (!isSuper)
        {
            // سرور ایمیل فقط توسط سوپر ادمین تغییر می‌کند؛ وگرنه کاربر دیگری می‌توانست ایمیل‌های بازیابی رمز را از سرور خودش عبور دهد
            (input.SmtpHost, input.SmtpPort, input.SmtpUseSsl, input.SmtpUser, input.SmtpFrom, input.NotifyEmail) =
                (s.SmtpHost, s.SmtpPort, s.SmtpUseSsl, s.SmtpUser, s.SmtpFrom, s.NotifyEmail);
            smtpPassword = null;
        }
        input.SmtpPasswordProtected = keepPassword;
        input.Id = s.Id;
        db.Entry(s).CurrentValues.SetValues(input);
        s.AdminFontSize = Math.Clamp(s.AdminFontSize == 0 ? 14 : s.AdminFontSize, 12, 18);
        s.SmtpPasswordProtected = string.IsNullOrEmpty(smtpPassword) ? keepPassword : settings.Protect(smtpPassword);
        audit.Log("ویرایش تنظیمات سایت", "Settings", s.Id);
        await db.SaveChangesAsync();
        settings.Invalidate();
        Success("تنظیمات ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    // ---------------- ظاهر سایت ----------------

    /// <summary>فیلدهایی که فقط در صفحه «ظاهر سایت» و «متن‌های سایت» ویرایش می‌شوند</summary>
    private static readonly string[] AppearanceNames =
    [
        nameof(SiteSettings.LogoPath), nameof(SiteSettings.ThemePrimary), nameof(SiteSettings.ThemeAccent), nameof(SiteSettings.ThemeBackground),
        nameof(SiteSettings.ThemeNavBg), nameof(SiteSettings.ThemeNavText), nameof(SiteSettings.ThemeTopbarBg), nameof(SiteSettings.ThemeFooterBg),
        nameof(SiteSettings.ThemeHeroBg), nameof(SiteSettings.ThemeRadius), nameof(SiteSettings.ThemeMode), nameof(SiteSettings.ThemeTitleFont),
        nameof(SiteSettings.FooterLogoPath), nameof(SiteSettings.FaviconPath), nameof(SiteSettings.OgImagePath), nameof(SiteSettings.HeroImagePath),
        nameof(SiteSettings.BackgroundImagePath), nameof(SiteSettings.BackgroundMode), nameof(SiteSettings.BackgroundVeil), nameof(SiteSettings.LogoHeight),
        nameof(SiteSettings.LogoShape), nameof(SiteSettings.ShowBrandText), nameof(SiteSettings.HeroDecor), nameof(SiteSettings.ProductImageRatio),
        nameof(SiteSettings.ProductImageFit), nameof(SiteSettings.RevealAnimation), nameof(SiteSettings.HoverEffect), nameof(SiteSettings.ShowHeroSearch),
        nameof(SiteSettings.ShowStats), nameof(SiteSettings.ShowCategories), nameof(SiteSettings.ShowFeatured), nameof(SiteSettings.ShowWhyUs),
        nameof(SiteSettings.ShowClients), nameof(SiteSettings.ShowDownloads), nameof(SiteSettings.YearsOfExperience), nameof(SiteSettings.TextOverrides),
    ];
    private static readonly System.Reflection.PropertyInfo[] AppearanceProps =
        AppearanceNames.Select(n => typeof(SiteSettings).GetProperty(n)!).ToArray();

    /// <summary>عکس‌های قابل آپلود در «ظاهر سایت»: نام فیلد فرم، ویژگی تنظیمات، اندازه استاندارد</summary>
    private static readonly (string Field, string Prop, ImageProfile Profile)[] AppearanceImages =
    [
        ("logo", nameof(SiteSettings.LogoPath), ImageProfile.Logo),
        ("footerLogo", nameof(SiteSettings.FooterLogoPath), ImageProfile.Logo),
        ("favicon", nameof(SiteSettings.FaviconPath), ImageProfile.Favicon),
        ("ogImage", nameof(SiteSettings.OgImagePath), ImageProfile.OpenGraph),
        ("heroImage", nameof(SiteSettings.HeroImagePath), ImageProfile.Hero),
        ("bgImage", nameof(SiteSettings.BackgroundImagePath), ImageProfile.Background),
    ];

    public async Task<IActionResult> Appearance() => View(await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SiteSettings());

    [HttpPost, RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Appearance(SiteSettings input, string[]? remove)
    {
        var s = await db.SiteSettings.FirstOrDefaultAsync();
        if (s is null) { s = new SiteSettings(); db.SiteSettings.Add(s); }
        // فقط فیلدهای ظاهری از فرم گرفته می‌شوند؛ مسیر عکس‌ها و متن‌ها از فرم پذیرفته نمی‌شود
        foreach (var prop in AppearanceProps)
        {
            if (prop.Name.EndsWith("Path") || prop.Name == nameof(SiteSettings.TextOverrides)) continue;
            prop.SetValue(s, prop.GetValue(input));
        }
        ThemeCss.Normalize(s);

        var errors = new List<string>();
        foreach (var (field, propName, profile) in AppearanceImages)
        {
            var prop = typeof(SiteSettings).GetProperty(propName)!;
            var old = prop.GetValue(s) as string;
            if (remove?.Contains(field) == true && old is not null) { files.Delete(old); prop.SetValue(s, null); old = null; }
            var file = Request.Form.Files.GetFile(field);
            if (file is not { Length: > 0 }) continue;
            var (path, err) = await files.SaveAsync(file, "site", UploadKind.Image, profile);
            if (err is not null) { errors.Add(err); continue; }
            files.Delete(old);
            prop.SetValue(s, path);
        }
        audit.Log("ویرایش ظاهر سایت", "Settings", s.Id);
        await db.SaveChangesAsync();
        settings.Invalidate();
        if (errors.Count > 0) Error("بعضی عکس‌ها ذخیره نشدند: " + string.Join(" | ", errors));
        else Success("ظاهر سایت ذخیره شد. برای دیدن تغییرات، صفحه سایت را تازه کنید.");
        return RedirectToAction(nameof(Appearance));
    }

    // ---------------- متن‌های سایت ----------------

    public async Task<IActionResult> Texts() => View(await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SiteSettings());

    [HttpPost]
    public async Task<IActionResult> Texts(Dictionary<string, string?>? fa, Dictionary<string, string?>? en)
    {
        var s = await db.SiteSettings.FirstOrDefaultAsync();
        if (s is null) { s = new SiteSettings(); db.SiteSettings.Add(s); }
        var editable = Lang.EditableGroups.SelectMany(g => g.Keys).ToHashSet();
        var result = new Dictionary<string, (string? Fa, string? En)>();
        foreach (var key in editable)
        {
            var d = Lang.Defaults.GetValueOrDefault(key);
            string? Clean(Dictionary<string, string?>? src, string def)
            {
                var v = src?.GetValueOrDefault(key)?.Trim();
                if (string.IsNullOrEmpty(v) || v == def) return null;
                return v.Length > 600 ? v[..600] : v;
            }
            var (f, e) = (Clean(fa, d.Fa), Clean(en, d.En));
            if (f is not null || e is not null) result[key] = (f, e);
        }
        s.TextOverrides = SiteSettings.SerializeTexts(result);
        audit.Log("ویرایش متن‌های سایت", "Settings", s.Id, $"{result.Count} متن سفارشی");
        await db.SaveChangesAsync();
        settings.Invalidate();
        Success("متن‌های سایت ذخیره شد.");
        return RedirectToAction(nameof(Texts));
    }

    [HttpPost]
    public async Task<IActionResult> TestEmail(string to)
    {
        var (ok, err) = await email.SendAsync(to, "ایمیل آزمایشی " + (await settings.GetAsync()).CompanyNameFa, "<div dir='rtl' style='font-family:Tahoma'>تنظیمات ایمیل سایت به درستی کار می‌کند ✅</div>");
        if (ok) Success("ایمیل آزمایشی ارسال شد."); else Error("ارسال ناموفق: " + err);
        return RedirectToAction(nameof(Index));
    }
}

[HasPermission(Permissions.Audit)]
public class AuditController(AppDbContext db) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Action.Contains(q) || (a.UserName != null && a.UserName.Contains(q)) || (a.Details != null && a.Details.Contains(q)));
        const int size = 50;
        var total = await query.CountAsync();
        ViewBag.Q = q; ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        return View(await query.OrderByDescending(a => a.Id).Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync());
    }
}
