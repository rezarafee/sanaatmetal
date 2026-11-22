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
public class CategoriesController(AppDbContext db, FileStorageService files, AuditService audit, IMemoryCache cache, ProductBulkService bulk) : AdminControllerBase
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
            Error("این دسته‌بندی دارای محصول یا زیرمجموعه است و حذف نمی‌شود. می‌توانید آن را غیرفعال کنید یا از دکمه «حذف محصولات دسته» استفاده کنید.");
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

    // ---------------- حذف گروهی محصولات یک دسته ----------------
    public class DeleteProductsVm
    {
        public Category Category { get; set; } = null!;
        public List<(Category Cat, int Products)> Parts { get; set; } = new();
        public ProductBulkService.DeleteResult Stats { get; set; } = null!;
    }

    /// <summary>دسته و زیرمجموعه‌هایش (درخت فقط یک سطح است ولی برای اطمینان همه نسل‌ها جمع می‌شوند)</summary>
    private async Task<List<int>> TreeIds(int id)
    {
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var result = new List<int> { id };
        for (var i = 0; i < result.Count; i++)
            result.AddRange(all.Where(c => c.ParentId == result[i] && !result.Contains(c.Id)).Select(c => c.Id));
        return result;
    }

    /// <summary>عربی/فارسی (ي/ی، ك/ک) و فاصله‌ها یکسان شوند تا تایپ نام دسته برای تأیید راحت باشد</summary>
    private static string NormName(string? s) => string.Join(' ', (s ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ')
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public async Task<IActionResult> DeleteProducts(int id)
    {
        var c = await db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        var tree = await TreeIds(id);
        var cats = await db.Categories.AsNoTracking().Where(x => tree.Contains(x.Id)).ToListAsync();
        var counts = await db.Products.Where(p => tree.Contains(p.CategoryId)).GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        return View(new DeleteProductsVm
        {
            Category = c,
            Parts = cats.OrderBy(x => x.Id == id ? 0 : 1).ThenBy(x => x.SortOrder).Select(x => (x, counts.GetValueOrDefault(x.Id))).ToList(),
            Stats = await bulk.PreviewAsync(db.Products.Where(p => tree.Contains(p.CategoryId))),
        });
    }

    [HttpPost, ActionName("DeleteProducts")]
    public async Task<IActionResult> DeleteProductsConfirmed(int id, string? confirmName, bool deleteCategory)
    {
        var c = await db.Categories.FindAsync(id);
        if (c is null) return NotFound();
        if (NormName(confirmName) != NormName(c.NameFa))
        {
            Error($"برای تأیید باید نام دسته را دقیقاً بنویسید: «{c.NameFa}»");
            return RedirectToAction(nameof(DeleteProducts), new { id });
        }
        var tree = await TreeIds(id);
        var r = await bulk.DeleteAsync(db.Products.Where(p => tree.Contains(p.CategoryId)));
        var msg = $"{Fmt.Number(r.Products)} محصول با {Fmt.Number(r.Variants)} سایز، {Fmt.Number(r.Images)} عکس و {Fmt.Number(r.Documents)} مستند حذف شد.";
        var details = $"{r.Products} محصول، {r.Variants} سایز، {r.Images} عکس، {r.Documents} مستند؛ سطرهای فاکتور جداشده: {r.InvoiceLines}، سطرهای سفارش: {r.OrderLines}";
        if (deleteCategory)
        {
            var cats = await db.Categories.Where(x => tree.Contains(x.Id)).ToListAsync();
            var images = cats.Select(x => x.ImagePath).Where(x => x is not null).Cast<string>().ToList();
            // اول زیرمجموعه‌ها (کلید والد Restrict است)، بعد خود دسته
            db.Categories.RemoveRange(cats.Where(x => x.Id != id));
            await db.SaveChangesAsync();
            db.Categories.Remove(c);
            details += $" — دسته و {cats.Count - 1} زیرمجموعه هم حذف شد";
            msg += cats.Count > 1 ? $" دسته «{c.NameFa}» و {Fmt.Number(cats.Count - 1)} زیرمجموعه‌اش هم حذف شدند." : $" دسته «{c.NameFa}» هم حذف شد.";
            audit.Log("حذف گروهی محصولات دسته", "Category", id, $"«{c.NameFa}»: {details}");
            await db.SaveChangesAsync();
            await files.DeleteManyAsync(images);
        }
        else
        {
            audit.Log("حذف گروهی محصولات دسته", "Category", id, $"«{c.NameFa}»: {details}");
            await db.SaveChangesAsync();
        }
        cache.Remove(NavCategoriesViewComponent.CacheKey);
        Success(msg + " فاکتورها و سفارش‌های قبلی دست‌نخورده باقی می‌مانند.");
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
