using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class LoginVm
{
    [Required(ErrorMessage = "نام کاربری را وارد کنید")] public string UserName { get; set; } = "";
    [Required(ErrorMessage = "رمز عبور را وارد کنید"), DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ChangePasswordVm
{
    [Required(ErrorMessage = "رمز فعلی را وارد کنید")] public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "رمز جدید را وارد کنید"), MinLength(10, ErrorMessage = "حداقل ۱۰ کاراکتر")] public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست")] public string ConfirmPassword { get; set; } = "";
}

public class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditService audit, AppDbContext db) : AdminControllerBase
{
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
