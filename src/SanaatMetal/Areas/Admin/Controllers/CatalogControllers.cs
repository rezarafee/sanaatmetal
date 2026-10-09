using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;
using SanaatMetal.ViewComponents;

namespace SanaatMetal.Areas.Admin.Controllers;

[HasPermission(Permissions.CatalogManage)]
public class CategoriesController(AppDbContext db, FileStorageService files, AuditService audit, IMemoryCache cache) : AdminControllerBase
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Counts = await db.Products.GroupBy(p => p.CategoryId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        return View(await db.Categories.AsNoTracking().Include(c => c.Parent).OrderBy(c => c.ParentId ?? c.Id).ThenBy(c => c.SortOrder).ToListAsync());
    }

    public async Task<IActionResult> Edit(int? id)
    {
        var c = id is null ? new Category() : await db.Categories.FindAsync(id);
        if (c is null) return NotFound();
        ViewBag.Parents = await db.Categories.AsNoTracking().Where(x => x.ParentId == null && x.Id != c.Id).OrderBy(x => x.SortOrder).ToListAsync();
        return View(c);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int? id, [Bind("NameFa,NameEn,Slug,DescriptionFa,DescriptionEn,Icon,SortOrder,IsActive,ParentId")] Category input, IFormFile? image)
    {
        var c = id is null or 0 ? new Category() : await db.Categories.FindAsync(id);
        if (c is null) return NotFound();
        input.Slug = Fmt.Slugify(string.IsNullOrWhiteSpace(input.Slug) ? (input.NameEn ?? input.NameFa) : input.Slug);
        if (await db.Categories.AnyAsync(x => x.Slug == input.Slug && x.Id != c.Id))
            ModelState.AddModelError("Slug", "این نامک قبلاً استفاده شده است.");
        if (input.ParentId == c.Id && c.Id != 0) input.ParentId = null;
        if (image is { Length: > 0 })
        {
            var (path, err) = await files.SaveAsync(image, "categories", UploadKind.Image);
            if (err is not null) ModelState.AddModelError("", err);
            else { files.Delete(c.ImagePath); c.ImagePath = path; }
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Parents = await db.Categories.AsNoTracking().Where(x => x.ParentId == null && x.Id != c.Id).ToListAsync();
            input.Id = c.Id; input.ImagePath = c.ImagePath;
            return View(input);
        }
        c.NameFa = input.NameFa; c.NameEn = input.NameEn; c.Slug = input.Slug; c.DescriptionFa = input.DescriptionFa;
        c.DescriptionEn = input.DescriptionEn; c.Icon = input.Icon; c.SortOrder = input.SortOrder; c.IsActive = input.IsActive;
        c.ParentId = input.ParentId; c.UpdatedAt = DateTime.UtcNow;
        if (c.Id == 0) db.Categories.Add(c);
        await db.SaveChangesAsync();
        audit.Log(id is null ? "ایجاد دسته‌بندی" : "ویرایش دسته‌بندی", "Category", c.Id, c.NameFa);
        await db.SaveChangesAsync();
        cache.Remove(NavCategoriesViewComponent.CacheKey);
        Success("دسته‌بندی ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await db.Categories.FindAsync(id);
        if (c is null) return NotFound();
        if (await db.Products.AnyAsync(p => p.CategoryId == id) || await db.Categories.AnyAsync(x => x.ParentId == id))
        {
            Error("این دسته‌بندی دارای محصول یا زیرمجموعه است و حذف نمی‌شود. می‌توانید آن را غیرفعال کنید.");
            return RedirectToAction(nameof(Index));
        }
        db.Categories.Remove(c);
        audit.Log("حذف دسته‌بندی", "Category", id, c.NameFa);
        await db.SaveChangesAsync();
        files.Delete(c.ImagePath);
        cache.Remove(NavCategoriesViewComponent.CacheKey);
        Success("دسته‌بندی حذف شد.");
        return RedirectToAction(nameof(Index));
    }
}

[HasPermission(Permissions.CatalogManage)]
public class MaterialsController(AppDbContext db, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Counts = await db.Products.Where(p => p.MaterialId != null).GroupBy(p => p.MaterialId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        return View(await db.Materials.AsNoTracking().OrderBy(m => m.NameFa).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Save(int id, [Bind("NameFa,NameEn,Code,Description")] Material input)
    {
        if (string.IsNullOrWhiteSpace(input.NameFa)) { Error("نام فارسی الزامی است."); return RedirectToAction(nameof(Index)); }
        var m = id == 0 ? new Material() : await db.Materials.FindAsync(id);
        if (m is null) return NotFound();
        m.NameFa = input.NameFa.Trim(); m.NameEn = input.NameEn?.Trim(); m.Code = input.Code?.Trim(); m.Description = input.Description;
        m.UpdatedAt = DateTime.UtcNow;
        if (id == 0) db.Materials.Add(m);
        await db.SaveChangesAsync();
        audit.Log(id == 0 ? "ایجاد متریال" : "ویرایش متریال", "Material", m.Id, m.NameFa);
        await db.SaveChangesAsync();
        Success("متریال ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await db.Materials.FindAsync(id);
        if (m is null) return NotFound();
        db.Materials.Remove(m);
        audit.Log("حذف متریال", "Material", id, m.NameFa);
        await db.SaveChangesAsync();
        Success("متریال حذف شد.");
        return RedirectToAction(nameof(Index));
    }
}
