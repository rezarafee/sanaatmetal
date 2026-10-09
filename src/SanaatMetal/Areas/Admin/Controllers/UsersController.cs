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
public class UsersController(UserManager<AppUser> users, AuditService audit, AppDbContext db) : AdminControllerBase
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
        return View(new UserInput { Id = u.Id, FullName = u.FullName, UserName = u.UserName!, Email = u.Email!, PhoneNumber = u.PhoneNumber, IsActive = u.IsActive, Role = roles.FirstOrDefault() ?? Roles.Secretary });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserInput input)
    {
        if (!Roles.All.Contains(input.Role)) ModelState.AddModelError("Role", "نقش نامعتبر است.");
        if (input.Role == Roles.SuperAdmin && !IsSuper) ModelState.AddModelError("Role", "فقط سوپر ادمین می‌تواند سوپر ادمین تعریف کند.");
        if (input.Id is null && string.IsNullOrWhiteSpace(input.Password)) ModelState.AddModelError("Password", "برای کاربر جدید رمز عبور الزامی است.");
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
            if (u.Id == users.GetUserId(User) && (!input.IsActive || input.Role != Roles.SuperAdmin) && IsSuper)
            {
                // جلوگیری از قفل شدن سیستم: سوپرادمین نمی‌تواند خودش را غیرفعال یا تنزل دهد
                input.IsActive = true; input.Role = Roles.SuperAdmin;
            }
            u.UserName = input.UserName.Trim(); u.Email = input.Email.Trim();
        }
        u.FullName = input.FullName.Trim(); u.PhoneNumber = input.PhoneNumber?.Trim(); u.IsActive = input.IsActive;

        var res = input.Id is null ? await users.CreateAsync(u, input.Password!) : await users.UpdateAsync(u);
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
        await db.SaveChangesAsync();
        Success("کاربر ذخیره شد.");
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
