using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class LoginVm
{
    [Required(ErrorMessage = "نام کاربری را وارد کنید"), MaxLength(256)] public string UserName { get; set; } = "";
    [Required(ErrorMessage = "رمز عبور را وارد کنید"), DataType(DataType.Password), MaxLength(128)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ChangePasswordVm
{
    [Required(ErrorMessage = "رمز فعلی را وارد کنید")] public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "رمز جدید را وارد کنید"), MinLength(10, ErrorMessage = "حداقل ۱۰ کاراکتر")] public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست")] public string ConfirmPassword { get; set; } = "";
}

public class ForgotPasswordVm
{
    [Required(ErrorMessage = "نام کاربری یا ایمیل را وارد کنید"), MaxLength(256)] public string UserNameOrEmail { get; set; } = "";
}

public class ResetPasswordVm
{
    [Required] public string UserId { get; set; } = "";
    [Required] public string Code { get; set; } = "";
    [Required(ErrorMessage = "رمز جدید را وارد کنید"), DataType(DataType.Password), MinLength(10, ErrorMessage = "حداقل ۱۰ کاراکتر"), MaxLength(128)]
    public string NewPassword { get; set; } = "";
    [DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست")]
    public string ConfirmPassword { get; set; } = "";
}

public class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditService audit, AppDbContext db,
    PasswordResetService reset, IPasswordHasher<AppUser> hasher, ILogger<AccountController> log) : AdminControllerBase
{
    // برای کاربری که وجود ندارد هم یک هش رمز محاسبه می‌شود تا از زمان پاسخ نتوان فهمید نام کاربری وجود دارد یا نه
    private static string? _dummyHash;

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await users.FindByNameAsync(vm.UserName.Trim()) ?? await users.FindByEmailAsync(vm.UserName.Trim());
        if (user is null || !user.IsActive)
        {
            var dummy = new AppUser();
            _dummyHash ??= hasher.HashPassword(dummy, Guid.NewGuid().ToString());
            hasher.VerifyHashedPassword(dummy, _dummyHash, vm.Password);
            ModelState.AddModelError("", "نام کاربری یا رمز عبور اشتباه است.");
            return View(vm);
        }
        var result = await signIn.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "به دلیل تلاش‌های ناموفق، حساب به مدت ۱۵ دقیقه قفل شد.");
            audit.Log("ورود قفل شد", "User", user.Id, user.UserName);
            await db.SaveChangesAsync();
            return View(vm);
        }
        if (!result.Succeeded)
        {
            db.AuditLogs.Add(new AuditLog { Action = "ورود ناموفق (رمز اشتباه)", UserName = user.FullName, EntityName = "User", EntityId = user.Id, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
            await db.SaveChangesAsync();
            ModelState.AddModelError("", "نام کاربری یا رمز عبور اشتباه است.");
            return View(vm);
        }
        user.LastLoginAt = DateTime.UtcNow;
        await users.UpdateAsync(user);
        db.AuditLogs.Add(new AuditLog { Action = "ورود به پنل", UserName = user.FullName, EntityName = "User", EntityId = user.Id, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
        await db.SaveChangesAsync();
        return Url.IsLocalUrl(vm.ReturnUrl) && vm.ReturnUrl!.StartsWith("/admin") ? LocalRedirect(vm.ReturnUrl) : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult Denied() => View();

    // ---------- بازیابی رمز عبور با ایمیل ----------
    // فقط حساب «سوپر ادمین» می‌تواند خودش با ایمیل رمز را بازیابی کند؛ رمز سایر کاربران را فقط سوپر ادمین از بخش کاربران ریست می‌کند.

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordVm());

    [AllowAnonymous, HttpPost, EnableRateLimiting("reset")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var id = vm.UserNameOrEmail.Trim();
        var user = await users.FindByNameAsync(id) ?? await users.FindByEmailAsync(id);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (user is not null && user.IsActive && await users.IsInRoleAsync(user, Roles.SuperAdmin))
        {
            var (ok, err) = await reset.SendLinkAsync(user);
            if (!ok) log.LogError("Password reset email to super admin {User} failed: {Error}", user.UserName, err);
            db.AuditLogs.Add(new AuditLog { Action = ok ? "ارسال لینک بازیابی رمز" : "ارسال لینک بازیابی رمز ناموفق", UserName = user.FullName, EntityName = "User", EntityId = user.Id, Details = err, IpAddress = ip });
            await db.SaveChangesAsync();
        }
        else if (user is not null)
        {
            db.AuditLogs.Add(new AuditLog { Action = "درخواست بازیابی رمز (رد شد: فقط سوپر ادمین)", UserName = user.FullName, EntityName = "User", EntityId = user.Id, IpAddress = ip });
            await db.SaveChangesAsync();
        }
        // پاسخ برای همه یکسان است تا نتوان فهمید چه نام کاربری‌ای وجود دارد
        ViewBag.Sent = true;
        return View(vm);
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ResetPassword(string? uid, string? code)
    {
        var vm = new ResetPasswordVm { UserId = uid ?? "", Code = code ?? "" };
        var user = await ValidResetUserAsync(vm);
        if (user is null) ViewBag.Invalid = true; else ViewBag.UserName = user.UserName;
        return View(vm);
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        var user = await ValidResetUserAsync(vm);
        if (user is null) { ViewBag.Invalid = true; return View(vm); }
        ViewBag.UserName = user.UserName;
        if (!ModelState.IsValid) return View(vm);
        var res = await users.ResetPasswordAsync(user, DecodeToken(vm.Code)!, vm.NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Code == "InvalidToken" ? "لینک نامعتبر یا منقضی شده است." : e.Description);
            return View(vm);
        }
        // رمز جدید: قفل حساب برداشته می‌شود و همه نشست‌های قبلی (به خاطر تغییر security stamp) باطل می‌شوند
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        db.AuditLogs.Add(new AuditLog { Action = "تعیین رمز با لینک ایمیل", UserName = user.FullName, EntityName = "User", EntityId = user.Id, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
        await db.SaveChangesAsync();
        await signIn.SignOutAsync();
        TempData["LoginInfo"] = "رمز عبور جدید ذخیره شد؛ اکنون وارد شوید.";
        return RedirectToAction(nameof(Login));
    }

    private static string? DecodeToken(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 2000) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); } catch (FormatException) { return null; }
    }

    private async Task<AppUser?> ValidResetUserAsync(ResetPasswordVm vm)
    {
        var token = DecodeToken(vm.Code);
        if (token is null || string.IsNullOrEmpty(vm.UserId) || vm.UserId.Length > 100) return null;
        var user = await users.FindByIdAsync(vm.UserId);
        if (user is null || !user.IsActive) return null;
        var ok = await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider, UserManager<AppUser>.ResetPasswordTokenPurpose, token);
        return ok ? user : null;
    }

    public async Task<IActionResult> Profile()
    {
        ViewData["User"] = await users.GetUserAsync(User);
        return View(new ChangePasswordVm());
    }

    [HttpPost]
    public async Task<IActionResult> Profile(ChangePasswordVm vm)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        ViewData["User"] = user;
        if (!ModelState.IsValid) return View(vm);
        var res = await users.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await signIn.RefreshSignInAsync(user);
        audit.Log("تغییر رمز عبور", "User", user.Id);
        await db.SaveChangesAsync();
        Success("رمز عبور با موفقیت تغییر کرد.");
        return RedirectToAction(nameof(Profile));
    }
}
