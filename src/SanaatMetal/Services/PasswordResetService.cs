using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

/// <summary>
/// ساخت و ارسال لینک بازیابی / تعیین رمز عبور با ایمیل.
/// لینک فقط ۲ ساعت معتبر است و بعد از استفاده (تغییر رمز) باطل می‌شود.
/// </summary>
public class PasswordResetService(UserManager<AppUser> users, EmailService email, SettingsService settings, IHttpContextAccessor http)
{
    public async Task<(bool Ok, string? Error)> SendLinkAsync(AppUser user, bool newAccount = false)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return (false, "برای این کاربر ایمیلی ثبت نشده است.");
        var ctx = http.HttpContext ?? throw new InvalidOperationException("No HTTP context");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{Seo.BaseUrl(ctx)}/admin/account/resetpassword?uid={Uri.EscapeDataString(user.Id)}&code={code}";

        var s = await settings.GetAsync();
        var e = (string? v) => WebUtility.HtmlEncode(v ?? "");
        var title = newAccount ? "تعیین رمز عبور حساب پنل" : "بازیابی رمز عبور پنل";
        var intro = newAccount
            ? "برای شما یک حساب کاربری در پنل مدیریت ساخته شد. برای تعیین رمز عبور روی دکمه زیر بزنید."
            : "درخواست بازیابی رمز عبور حساب شما در پنل مدیریت ثبت شد. برای تعیین رمز جدید روی دکمه زیر بزنید.";
        var html = $"""
            <div dir="rtl" style="font-family:Tahoma,Arial;line-height:2;color:#1f2937;max-width:560px">
              <h3 style="color:#22318a">{e(s.CompanyNameFa)} - {title}</h3>
              <p>{e(user.FullName)} عزیز،</p>
              <p>{intro}</p>
              <p><b>نام کاربری:</b> <span dir="ltr">{e(user.UserName)}</span></p>
              <p style="margin:24px 0"><a href="{e(link)}" style="background:#22318a;color:#fff;padding:10px 22px;border-radius:8px;text-decoration:none">تعیین رمز عبور</a></p>
              <p style="font-size:12px;color:#6b7280">این لینک تا ۲ ساعت معتبر است و فقط یک بار کار می‌کند.
              اگر این درخواست از طرف شما نبوده، این ایمیل را نادیده بگیرید؛ رمز فعلی شما تغییر نمی‌کند.</p>
              <p style="font-size:12px;color:#6b7280;direction:ltr;text-align:left;word-break:break-all">{e(link)}</p>
            </div>
            """;
        return await email.SendAsync(user.Email, $"{title} - {s.CompanyNameFa}", html);
    }
}
