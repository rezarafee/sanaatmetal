using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class UserInput
{
    public string? Id { get; set; }
    [Required(ErrorMessage = "نام کامل الزامی است")] public string FullName { get; set; } = "";
    [Required(ErrorMessage = "نام کاربری الزامی است"), RegularExpression("^[a-zA-Z0-9._-]{3,50}$", ErrorMessage = "نام کاربری فقط حروف و اعداد انگلیسی (۳ تا ۵۰ کاراکتر)")]
    public string UserName { get; set; } = "";
    [Required(ErrorMessage = "ایمیل الزامی است"), EmailAddress(ErrorMessage = "ایمیل معتبر نیست")] public string Email { get; set; } = "";
    public string? PhoneNumber { get; set; }
    [Required] public string Role { get; set; } = Roles.Secretary;
    public bool IsActive { get; set; } = true;
    public string? Password { get; set; }
}

public class UserRow
{
    public AppUser User { get; set; } = null!;
    public List<string> Roles { get; set; } = new();
}

[HasPermission(Permissions.Users)]
public class UsersController(UserManager<AppUser> users, AuditService audit, AppDbContext db, PasswordResetService reset, PleskMailService plesk) : AdminControllerBase
{
    private bool IsSuper => User.IsInRole(Roles.SuperAdmin);

    public async Task<IActionResult> Index()
    {
        var list = await users.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();
        var rows = new List<UserRow>();
        foreach (var u in list) rows.Add(new UserRow { User = u, Roles = (await users.GetRolesAsync(u)).ToList() });
        return View(rows);
    }

    public async Task<IActionResult> Edit(string? id)
    {
        if (id is null) return View(new UserInput());
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        var roles = await users.GetRolesAsync(u);
        if (roles.Contains(Roles.SuperAdmin) && !IsSuper) return Forbid();
        if (IsSuper && plesk.Configured && plesk.LocalPartOf(u.Email) is { } local)
        {
            var (exists, err) = await plesk.ExistsAsync(local);
            ViewBag.PleskExists = exists;
            ViewBag.PleskError = err;
        }
        return View(new UserInput { Id = u.Id, FullName = u.FullName, UserName = u.UserName!, Email = u.Email!, PhoneNumber = u.PhoneNumber, IsActive = u.IsActive, Role = roles.FirstOrDefault() ?? Roles.Secretary });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserInput input)
    {
        if (!Roles.All.Contains(input.Role)) ModelState.AddModelError("Role", "نقش نامعتبر است.");
        if (input.Role == Roles.SuperAdmin && !IsSuper) ModelState.AddModelError("Role", "فقط سوپر ادمین می‌تواند سوپر ادمین تعریف کند.");
        // تعیین / ریست رمز عبور کاربران فقط توسط سوپر ادمین؛ بقیه فقط می‌توانند لینک تعیین رمز را برای کاربر جدید ایمیل کنند
        if (!IsSuper && !string.IsNullOrWhiteSpace(input.Password)) { ModelState.AddModelError("Password", "فقط مدیر کل (سوپر ادمین) می‌تواند رمز عبور کاربران را تعیین یا ریست کند."); input.Password = null; }
        if (!ModelState.IsValid) return View(input);

        AppUser? u;
        if (input.Id is null)
        {
            u = new AppUser { UserName = input.UserName.Trim(), Email = input.Email.Trim(), EmailConfirmed = true };
        }
        else
        {
            u = await users.FindByIdAsync(input.Id);
            if (u is null) return NotFound();
            if ((await users.GetRolesAsync(u)).Contains(Roles.SuperAdmin) && !IsSuper) return Forbid();
            // ایمیل راه بازیابی رمز است؛ فقط سوپر ادمین می‌تواند ایمیل کاربر موجود را تغییر دهد
            if (!IsSuper) input.Email = u.Email ?? input.Email;
            if (u.Id == users.GetUserId(User) && (!input.IsActive || input.Role != Roles.SuperAdmin) && IsSuper)
            {
                // جلوگیری از قفل شدن سیستم: سوپرادمین نمی‌تواند خودش را غیرفعال یا تنزل دهد
                input.IsActive = true; input.Role = Roles.SuperAdmin;
            }
            u.UserName = input.UserName.Trim(); u.Email = input.Email.Trim();
        }
        u.FullName = input.FullName.Trim(); u.PhoneNumber = input.PhoneNumber?.Trim(); u.IsActive = input.IsActive;

        var hasPassword = !string.IsNullOrWhiteSpace(input.Password);
        var res = input.Id is null
            ? (hasPassword ? await users.CreateAsync(u, input.Password!) : await users.CreateAsync(u))
            : await users.UpdateAsync(u);
        if (res.Succeeded && input.Id is not null && !string.IsNullOrWhiteSpace(input.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(u);
            res = await users.ResetPasswordAsync(u, token, input.Password);
        }
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(input);
        }
        var current = await users.GetRolesAsync(u);
        if (!current.SequenceEqual([input.Role]))
        {
            await users.RemoveFromRolesAsync(u, current);
            await users.AddToRoleAsync(u, input.Role);
        }
        await users.UpdateSecurityStampAsync(u); // اعمال سریع تغییر نقش / غیرفعال‌سازی
        if (!input.IsActive) await users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue);
        else await users.SetLockoutEndDateAsync(u, null);
        audit.Log(input.Id is null ? "ایجاد کاربر" : "ویرایش کاربر", "User", u.Id, $"{u.UserName} ({Roles.Title(input.Role)})");
        if (input.Id is not null && hasPassword) audit.Log("ریست رمز عبور توسط سوپر ادمین", "User", u.Id, u.UserName);
        await db.SaveChangesAsync();
        if (input.Id is null && !hasPassword)
        {
            // کاربر جدید بدون رمز: لینک تعیین رمز به ایمیلش فرستاده می‌شود
            var (ok, err) = input.IsActive ? await reset.SendLinkAsync(u, newAccount: true) : (false, "حساب غیرفعال است");
            audit.Log(ok ? "ارسال لینک تعیین رمز" : "ارسال لینک تعیین رمز ناموفق", "User", u.Id, err);
            await db.SaveChangesAsync();
            if (ok) Success($"کاربر ساخته شد و لینک تعیین رمز به {u.Email} ایمیل شد.");
            else Error($"کاربر ساخته شد ولی ایمیل تعیین رمز ارسال نشد ({err}). مدیر کل باید رمز را تعیین کند یا دوباره لینک بفرستد.");
            return RedirectToAction(nameof(Index));
        }
        Success("کاربر ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    // ---------- صندوق ایمیل کاربر در Plesk (فقط سوپر ادمین) ----------

    [HttpPost]
    public async Task<IActionResult> PleskTest()
    {
        if (!IsSuper) return Forbid();
        var r = await plesk.TestAsync();
        if (r.Ok) Success($"اتصال به Plesk برقرار است (دامنه {plesk.Options.DomainName})."); else Error("اتصال به Plesk ناموفق: " + r.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ساخت صندوق ایمیل name@domain برای کاربر یا تعیین رمز جدید برای صندوق موجود</summary>
    [HttpPost]
    public async Task<IActionResult> PleskMail(string id, string mode, string? mailName, string? mailPassword, bool setAsUserEmail = true)
    {
        if (!IsSuper) return Forbid();
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (!plesk.Configured) { Error("اتصال به Plesk تنظیم نشده است."); return RedirectToAction(nameof(Edit), new { id }); }

        var name = (mode == "reset" ? plesk.LocalPartOf(u.Email) : mailName)?.Trim().ToLowerInvariant();
        if (!PleskMailService.ValidLocalPart(name)) { Error("نام ایمیل نامعتبر است (فقط حروف کوچک و اعداد انگلیسی، نقطه، خط تیره)."); return RedirectToAction(nameof(Edit), new { id }); }
        var address = $"{name}@{plesk.Options.DomainName}";

        var password = string.IsNullOrWhiteSpace(mailPassword) ? PleskMailService.GeneratePassword() : mailPassword.Trim();
        if (password.Length < 8) { Error("رمز ایمیل باید حداقل ۸ کاراکتر باشد (یا خالی بگذارید تا خودکار ساخته شود)."); return RedirectToAction(nameof(Edit), new { id }); }

        var r = mode == "reset" ? await plesk.SetPasswordAsync(name!, password) : await plesk.CreateMailboxAsync(name!, password);
        audit.Log(mode == "reset" ? (r.Ok ? "تغییر رمز ایمیل در Plesk" : "تغییر رمز ایمیل در Plesk ناموفق") : (r.Ok ? "ساخت ایمیل در Plesk" : "ساخت ایمیل در Plesk ناموفق"),
            "User", u.Id, r.Ok ? address : $"{address}: {r.Error}");
        if (r.Ok && mode != "reset" && setAsUserEmail && !string.Equals(u.Email, address, StringComparison.OrdinalIgnoreCase))
        {
            var er = await users.SetEmailAsync(u, address);
            if (er.Succeeded) { u.EmailConfirmed = true; await users.UpdateAsync(u); }
            else Error("ایمیل ساخته شد ولی به عنوان ایمیل کاربر ثبت نشد: " + string.Join("، ", er.Errors.Select(e => e.Description)));
        }
        await db.SaveChangesAsync();
        // رمز فقط همین یک بار به سوپر ادمین نشان داده می‌شود و جایی ذخیره نمی‌شود
        if (r.Ok) TempData["PleskPassword"] = $"{address}|{password}";
        else Error("Plesk: " + r.Error);
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>ارسال لینک ریست رمز به ایمیل کاربر — فقط سوپر ادمین</summary>
    [HttpPost]
    public async Task<IActionResult> SendResetLink(string id)
    {
        if (!IsSuper) return Forbid();
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (!u.IsActive) { Error("این حساب غیرفعال است؛ ابتدا آن را فعال کنید."); return RedirectToAction(nameof(Index)); }
        var (ok, err) = await reset.SendLinkAsync(u);
        audit.Log(ok ? "ارسال لینک ریست رمز توسط سوپر ادمین" : "ارسال لینک ریست رمز ناموفق", "User", u.Id, err ?? u.UserName);
        await db.SaveChangesAsync();
        if (ok) Success($"لینک ریست رمز به {u.Email} ارسال شد (۲ ساعت اعتبار دارد)."); else Error("ارسال ایمیل ناموفق بود: " + err);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (u.Id == users.GetUserId(User)) { Error("نمی‌توانید حساب خودتان را حذف کنید."); return RedirectToAction(nameof(Index)); }
        if ((await users.GetRolesAsync(u)).Contains(Roles.SuperAdmin) && !IsSuper) return Forbid();
        await users.DeleteAsync(u);
        audit.Log("حذف کاربر", "User", id, u.UserName);
        await db.SaveChangesAsync();
        Success("کاربر حذف شد.");
        return RedirectToAction(nameof(Index));
    }
}

[HasPermission(Permissions.Users)]
public class RolesController(AppDbContext db, IPermissionService permissions, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.RolePermissions.AsNoTracking().ToListAsync();
        ViewBag.Map = rows.GroupBy(r => r.RoleName).ToDictionary(g => g.Key, g => g.Select(r => r.Permission).ToHashSet());
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Index(Dictionary<string, List<string>>? perms)
    {
        perms ??= new();
        var valid = Permissions.All.ToHashSet();
        var existing = await db.RolePermissions.Where(r => r.RoleName != Roles.SuperAdmin).ToListAsync();
        db.RolePermissions.RemoveRange(existing);
        foreach (var role in Roles.All.Where(r => r != Roles.SuperAdmin))
            foreach (var p in (perms.GetValueOrDefault(role) ?? []).Where(valid.Contains).Distinct())
                db.RolePermissions.Add(new RolePermission { RoleName = role, Permission = p });
        audit.Log("تغییر سطوح دسترسی", "Roles");
        await db.SaveChangesAsync();
        permissions.Invalidate();
        Success("سطوح دسترسی ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Reset()
    {
        db.RolePermissions.RemoveRange(await db.RolePermissions.ToListAsync());
        foreach (var (role, perms) in Permissions.Defaults)
            foreach (var p in perms) db.RolePermissions.Add(new RolePermission { RoleName = role, Permission = p });
        audit.Log("بازنشانی سطوح دسترسی", "Roles");
        await db.SaveChangesAsync();
        permissions.Invalidate();
        Success("دسترسی‌ها به حالت پیش‌فرض برگشت.");
        return RedirectToAction(nameof(Index));
    }
}
