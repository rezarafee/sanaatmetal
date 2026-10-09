using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SanaatMetal.Services;

namespace SanaatMetal.Infrastructure;

/// <summary>زبان از کوکی sm.lang یا پارامتر ?lang= ؛ پنل ادمین همیشه فارسی</summary>
public class SiteCultureProvider : RequestCultureProvider
{
    public const string CookieName = "sm.lang";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext ctx)
    {
        string lang = "fa";
        if (!ctx.Request.Path.StartsWithSegments("/admin"))
        {
            var q = ctx.Request.Query["lang"].ToString();
            if (q is "fa" or "en")
            {
                lang = q;
                ctx.Response.Cookies.Append(CookieName, q, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, Secure = ctx.Request.IsHttps,
                });
            }
            else if (ctx.Request.Cookies.TryGetValue(CookieName, out var c) && c is "fa" or "en") lang = c!;
        }
        return Task.FromResult<ProviderCultureResult?>(
            new ProviderCultureResult("en-US", lang == "en" ? "en-US" : "fa-IR"));
    }
}

/// <summary>اعداد را با ارقام فارسی و جداکننده هزارگان هم می‌پذیرد</summary>
public class FlexibleDecimalBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var value = ctx.ValueProvider.GetValue(ctx.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        ctx.ModelState.SetModelValue(ctx.ModelName, value);
        var raw = Fmt.EnDigits(value.FirstValue)?.Replace(",", "").Replace("٬", "").Replace("،", "").Replace(" ", "").Replace("٫", ".");
        if (string.IsNullOrEmpty(raw))
        {
            if (Nullable.GetUnderlyingType(ctx.ModelType) != null) ctx.Result = ModelBindingResult.Success(null);
            else ctx.Result = ModelBindingResult.Success(0m);
            return Task.CompletedTask;
        }
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            ctx.Result = ModelBindingResult.Success(d);
        else
            ctx.ModelState.TryAddModelError(ctx.ModelName, "عدد وارد شده معتبر نیست.");
        return Task.CompletedTask;
    }
}

public class FlexibleDecimalBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?)
            ? new FlexibleDecimalBinder() : null;
}

public class PersianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"رمز عبور باید حداقل {length} کاراکتر باشد." };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "رمز عبور باید شامل عدد باشد." };
    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "رمز عبور باید شامل حرف کوچک انگلیسی باشد." };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "رمز عبور باید شامل حرف بزرگ انگلیسی باشد." };
    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "رمز عبور باید شامل یک نماد (مثل @ یا #) باشد." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = $"نام کاربری «{userName}» قبلاً ثبت شده است." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = $"ایمیل «{email}» قبلاً ثبت شده است." };
    public override IdentityError InvalidUserName(string? userName) => new() { Code = nameof(InvalidUserName), Description = "نام کاربری فقط می‌تواند شامل حروف و اعداد انگلیسی باشد." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "ایمیل معتبر نیست." };
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "رمز عبور فعلی اشتباه است." };
}
