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
