using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace SanaatMetal.Models;

/// <summary>مشتریان ما (پالایشگاه‌ها و شرکت‌ها) برای اسلایدر لوگو در صفحه اصلی</summary>
public class Client : Entity
{
    [Required, MaxLength(200)] public string NameFa { get; set; } = "";
    [MaxLength(200)] public string? NameEn { get; set; }
    [MaxLength(400)] public string? LogoPath { get; set; }
    [MaxLength(300)] public string? Website { get; set; }
    [MaxLength(150)] public string? Industry { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>پیام‌های فرم تماس صفحه اصلی؛ همیشه ذخیره می‌شود حتی اگر ارسال ایمیل شکست بخورد</summary>
public class ContactMessage : Entity
{
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [Required, MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Company { get; set; }
    [MaxLength(200)] public string? Subject { get; set; }
    [Required, MaxLength(4000)] public string Body { get; set; } = "";
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(5)] public string Culture { get; set; } = "fa";
    public bool EmailSent { get; set; }
    [MaxLength(1000)] public string? EmailError { get; set; }
    public bool IsRead { get; set; }
    public bool IsArchived { get; set; }
    [MaxLength(2000)] public string? StaffNote { get; set; }
}

/// <summary>تنظیمات سایت (یک ردیف)</summary>
public class SiteSettings
{
    public record Office(int No, string? TitleFa, string? TitleEn, string? Phone, string? AddressFa, string? AddressEn);

    /// <summary>دفترهایی که حداقل تلفن یا نشانی دارند</summary>
    public IEnumerable<Office> Offices()
    {
        if (!string.IsNullOrWhiteSpace(Phone) || !string.IsNullOrWhiteSpace(AddressFa))
            yield return new Office(1, Office1TitleFa, Office1TitleEn, Phone, AddressFa, AddressEn);
        if (!string.IsNullOrWhiteSpace(Phone2) || !string.IsNullOrWhiteSpace(Address2Fa))
            yield return new Office(2, Office2TitleFa, Office2TitleEn, Phone2, Address2Fa, Address2En);
    }

    public int Id { get; set; }
    [MaxLength(200)] public string CompanyNameFa { get; set; } = "صنعت متال";
    [MaxLength(200)] public string? CompanyNameEn { get; set; } = "Sanaat Metal";
    [MaxLength(300)] public string? SloganFa { get; set; }
    [MaxLength(300)] public string? SloganEn { get; set; }
    [MaxLength(300)] public string? HeroTitleFa { get; set; }
    [MaxLength(300)] public string? HeroTitleEn { get; set; }
    [MaxLength(600)] public string? HeroSubtitleFa { get; set; }
    [MaxLength(600)] public string? HeroSubtitleEn { get; set; }
    public string? AboutFa { get; set; }
    public string? AboutEn { get; set; }
    [MaxLength(200)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Mobile { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(500)] public string? AddressFa { get; set; }
    [MaxLength(500)] public string? AddressEn { get; set; }
    // دفتر اول از Phone و AddressFa/En استفاده می‌کند؛ دفتر دوم فیلدهای جداگانه دارد
    [MaxLength(100)] public string? Office1TitleFa { get; set; }
    [MaxLength(100)] public string? Office1TitleEn { get; set; }
    [MaxLength(100)] public string? Office2TitleFa { get; set; }
    [MaxLength(100)] public string? Office2TitleEn { get; set; }
    [MaxLength(200)] public string? Phone2 { get; set; }
    [MaxLength(500)] public string? Address2Fa { get; set; }
    [MaxLength(500)] public string? Address2En { get; set; }
    /// <summary>اندازه پایه فونت پنل مدیریت (پیکسل)</summary>
    [Range(12, 18)] public int AdminFontSize { get; set; } = 14;
    [MaxLength(300)] public string? WorkingHoursFa { get; set; }
    [MaxLength(300)] public string? WorkingHoursEn { get; set; }
    [MaxLength(300)] public string? Instagram { get; set; }
    [MaxLength(300)] public string? Telegram { get; set; }
    [MaxLength(300)] public string? WhatsApp { get; set; }
    [MaxLength(300)] public string? LinkedIn { get; set; }
    [MaxLength(30)] public string CurrencyFa { get; set; } = "ریال";
    [MaxLength(30)] public string CurrencyEn { get; set; } = "IRR";
    public decimal DefaultTaxPercent { get; set; } = 10;
    [MaxLength(1000)] public string? InvoiceFooter { get; set; }
    [MaxLength(30)] public string? EconomicCode { get; set; }
    [MaxLength(30)] public string? RegistrationNumber { get; set; }
    [MaxLength(400)] public string? LogoPath { get; set; }

    // ---------- ظاهر سایت (از صفحه «ظاهر سایت» در پنل ویرایش می‌شود) ----------
    [MaxLength(9)] public string ThemePrimary { get; set; } = "#22318a";
    [MaxLength(9)] public string ThemeAccent { get; set; } = "#f5b301";
    [MaxLength(9)] public string ThemeBackground { get; set; } = "#f3f4f8";
    /// <summary>رنگ‌های اختیاری؛ خالی یعنی رنگ پیش‌فرض قالب</summary>
    [MaxLength(9)] public string? ThemeNavBg { get; set; }
    [MaxLength(9)] public string? ThemeNavText { get; set; }
    [MaxLength(9)] public string? ThemeTopbarBg { get; set; }
    [MaxLength(9)] public string? ThemeFooterBg { get; set; }
    [MaxLength(9)] public string? ThemeHeroBg { get; set; }
    [Range(0, 32)] public int ThemeRadius { get; set; } = 18;
    /// <summary>تم پیش‌فرض برای بازدیدکننده‌ای که خودش انتخاب نکرده: auto / light / dark</summary>
    [MaxLength(10)] public string ThemeMode { get; set; } = "auto";
    /// <summary>فونت تیترها: Lalezar یا Vazirmatn</summary>
    [MaxLength(30)] public string ThemeTitleFont { get; set; } = "Lalezar";

    [MaxLength(400)] public string? FooterLogoPath { get; set; }
    [MaxLength(400)] public string? FaviconPath { get; set; }
    [MaxLength(400)] public string? OgImagePath { get; set; }
    [MaxLength(400)] public string? HeroImagePath { get; set; }
    [MaxLength(400)] public string? BackgroundImagePath { get; set; }
    /// <summary>cover (تمام‌صفحه ثابت) یا repeat (کاشی)</summary>
    [MaxLength(10)] public string BackgroundMode { get; set; } = "cover";
    /// <summary>درصد پوشش رنگ پس‌زمینه روی عکس پس‌زمینه (تا متن خوانا بماند)</summary>
    [Range(0, 95)] public int BackgroundVeil { get; set; } = 85;
    /// <summary>ارتفاع لوگوی بالای سایت (پیکسل)</summary>
    [Range(28, 96)] public int LogoHeight { get; set; } = 44;
    /// <summary>circle / rounded / wide (لوگوی افقی بدون برش)</summary>
    [MaxLength(10)] public string LogoShape { get; set; } = "circle";
    public bool ShowBrandText { get; set; } = true;
    /// <summary>طرح روی بنر اصلی: blueprint (نقشه فنی) / grid / none</summary>
    [MaxLength(12)] public string HeroDecor { get; set; } = "grid";

    // تصاویر محصولات و انیمیشن‌ها
    /// <summary>نسبت ابعاد عکس محصول: 1:1، 4:3، 3:4، 16:9</summary>
    [MaxLength(5)] public string ProductImageRatio { get; set; } = "4:3";
    /// <summary>contain (کامل با حاشیه شفاف) / white (کامل با حاشیه سفید) / cover (برش برای پر کردن کادر)</summary>
    [MaxLength(10)] public string ProductImageFit { get; set; } = "contain";
    /// <summary>انیمیشن ظاهر شدن هنگام اسکرول: fade-up / zoom / flip / blur / slide / none</summary>
    [MaxLength(10)] public string RevealAnimation { get; set; } = "fade-up";
    /// <summary>افکت روی عکس‌ها با ماوس: zoom / tilt / shine / lift / none</summary>
    [MaxLength(10)] public string HoverEffect { get; set; } = "zoom";

    // بخش‌های صفحه اصلی
    public bool ShowHeroSearch { get; set; } = true;
    public bool ShowStats { get; set; } = true;
    public bool ShowCategories { get; set; } = true;
    public bool ShowFeatured { get; set; } = true;
    public bool ShowWhyUs { get; set; } = true;
    public bool ShowClients { get; set; } = true;
    /// <summary>منوی «گواهی‌ها و دیتاشیت‌ها»</summary>
    public bool ShowDownloads { get; set; } = true;
    /// <summary>سال تجربه در آمار صفحه اصلی؛ صفر یعنی نمایش داده نشود</summary>
    [Range(0, 200)] public int YearsOfExperience { get; set; } = 15;

    /// <summary>متن‌های دلخواه سایت که جایگزین متن پیش‌فرض می‌شوند: JSON {"Key":{"fa":"..","en":".."}}</summary>
    public string? TextOverrides { get; set; }

    private Dictionary<string, (string? Fa, string? En)>? _texts;
    /// <summary>متن‌های جایگزین (یک بار از JSON خوانده می‌شود)</summary>
    public IReadOnlyDictionary<string, (string? Fa, string? En)> Texts() => _texts ??= ParseTexts(TextOverrides);

    public static Dictionary<string, (string? Fa, string? En)> ParseTexts(string? json)
    {
        var d = new Dictionary<string, (string? Fa, string? En)>();
        if (string.IsNullOrWhiteSpace(json)) return d;
        try
        {
            var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(json);
            foreach (var (k, v) in raw ?? [])
                d[k] = (v.GetValueOrDefault("fa"), v.GetValueOrDefault("en"));
        }
        catch (System.Text.Json.JsonException) { }
        return d;
    }

    public static string? SerializeTexts(IEnumerable<KeyValuePair<string, (string? Fa, string? En)>> texts)
    {
        var o = texts.Where(t => !string.IsNullOrWhiteSpace(t.Value.Fa) || !string.IsNullOrWhiteSpace(t.Value.En))
            .ToDictionary(t => t.Key, t => new Dictionary<string, string?> { ["fa"] = t.Value.Fa?.Trim(), ["en"] = t.Value.En?.Trim() });
        return o.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // SMTP
    [MaxLength(200)] public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    [MaxLength(200)] public string? SmtpUser { get; set; }
    /// <summary>با DataProtection رمز می‌شود</summary>
    [MaxLength(1000)] public string? SmtpPasswordProtected { get; set; }
    [MaxLength(200)] public string? SmtpFrom { get; set; }
    [MaxLength(200)] public string? NotifyEmail { get; set; }
}

public class AppUser : IdentityUser
{
    [MaxLength(150)] public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class RolePermission
{
    public int Id { get; set; }
    [Required, MaxLength(256)] public string RoleName { get; set; } = "";
    [Required, MaxLength(100)] public string Permission { get; set; } = "";
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [MaxLength(150)] public string? UserName { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? EntityName { get; set; }
    [MaxLength(50)] public string? EntityId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
}

/// <summary>
/// فایل بارگذاری‌شده که در دیتابیس نگه داشته می‌شود؛ برای هاست‌هایی که برنامه اجازه نوشتن در پوشه سایت را ندارد.
/// با همان آدرس /uploads/... نمایش داده می‌شود.
/// </summary>
public class StoredFile
{
    public int Id { get; set; }
    [Required, MaxLength(300)] public string Path { get; set; } = "";
    [Required, MaxLength(100)] public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
