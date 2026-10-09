using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SanaatMetal.Services;

public static partial class Fmt
{
    private static readonly PersianCalendar Pc = new();
    private static readonly TimeZoneInfo IranTz = FindIranTz();

    private static TimeZoneInfo FindIranTz()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IRST", TimeSpan.FromHours(3.5), "IRST", "IRST");
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), IranTz);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), IranTz);

    public static string FaDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(ch is >= '0' and <= '9' ? (char)('۰' + (ch - '0')) : ch);
        return sb.ToString();
    }

    /// <summary>تبدیل ارقام فارسی/عربی به انگلیسی (برای ورودی کاربر)</summary>
    public static string EnDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    public static string Number(decimal? value, bool fa = true)
    {
        if (value is null) return "";
        var s = value.Value.ToString(value.Value % 1 == 0 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
        return fa ? FaDigits(s).Replace(',', '٬') : s;
    }

    public static string Jalali(DateTime? utc, bool withTime = false, bool fa = true)
    {
        if (utc is null) return "";
        var d = ToLocal(utc.Value);
        var s = $"{Pc.GetYear(d):0000}/{Pc.GetMonth(d):00}/{Pc.GetDayOfMonth(d):00}";
        if (withTime) s += $" {d:HH:mm}";
        return fa ? FaDigits(s) : s;
    }

    /// <summary>تاریخ شمسی بدون تبدیل منطقه زمانی (برای مقادیر فقط-تاریخ)</summary>
    public static string JalaliDateOnly(DateTime? d)
    {
        if (d is null) return "";
        var v = d.Value;
        return $"{Pc.GetYear(v):0000}/{Pc.GetMonth(v):00}/{Pc.GetDayOfMonth(v):00}";
    }

    public static string Gregorian(DateTime? utc, bool withTime = false) =>
        utc is null ? "" : ToLocal(utc.Value).ToString(withTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>پارس تاریخ شمسی مانند ۱۴۰۵/۰۷/۱۷ ؛ خروجی میلادی (فقط تاریخ)</summary>
    public static DateTime? ParseJalali(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var m = JalaliRegex().Match(EnDigits(input.Trim()));
        if (!m.Success) return null;
        try
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            if (y < 1000) y += 1300;
            return Pc.ToDateTime(y, mo, d, 0, 0, 0, 0);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    [GeneratedRegex(@"^(\d{2,4})[/\-.](\d{1,2})[/\-.](\d{1,2})$")]
    private static partial Regex JalaliRegex();

    public static string? NormalizeMobile(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = EnDigits(input).Replace(" ", "").Replace("-", "");
        if (s.StartsWith("+98")) s = "0" + s[3..];
        else if (s.StartsWith("0098")) s = "0" + s[4..];
        else if (s.StartsWith("98") && s.Length == 12) s = "0" + s[2..];
        else if (s.StartsWith('9') && s.Length == 10) s = "0" + s;
        return MobileRegex().IsMatch(s) ? s : null;
    }

    [GeneratedRegex(@"^09\d{9}$")]
    private static partial Regex MobileRegex();

    public static string Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Guid.NewGuid().ToString("N")[..8];
        var s = EnDigits(text).Trim().ToLowerInvariant();
        s = SlugInvalid().Replace(s, "-");
        s = SlugDash().Replace(s, "-").Trim('-');
        return s.Length > 150 ? s[..150] : (s.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : s);
    }

    // حروف فارسی در اسلاگ مجاز است
    [GeneratedRegex(@"[^a-z0-9؀-ۿ]+")]
    private static partial Regex SlugInvalid();

    [GeneratedRegex("-{2,}")]
    private static partial Regex SlugDash();

    public static string DisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
    }

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
