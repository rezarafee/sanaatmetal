using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class VariantInput
{
    public int Id { get; set; }
    public string Size { get; set; } = "";
    public string? PressureClass { get; set; }
    public string? Schedule { get; set; }
    public string? Sku { get; set; }
    public string Unit { get; set; } = "عدد";
    public string? ExtraSpecs { get; set; }
    public decimal? Stock { get; set; }
    public bool IsActive { get; set; }
    public bool CallForPrice { get; set; }
    public int SortOrder { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SalePrice { get; set; }
}

public class DocumentInput
{
    public DocumentType Type { get; set; } = DocumentType.Certificate;
    public string TitleFa { get; set; } = "";
    public string? TitleEn { get; set; }
    public string? Version { get; set; }
    public string? Issuer { get; set; }
    public string? IssuedAt { get; set; }
    public string? ExpiresAt { get; set; }
    /// <summary>نسخه‌های قبلی هم‌نوع را غیرفعال کن</summary>
    public bool ReplacePrevious { get; set; } = true;
}

[HasPermission(Permissions.CatalogView)]
public class ProductsController(AppDbContext db, FileStorageService files, AuditService audit, PricingService pricing) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, int? cat, string? status, int page = 1)
    {
        var query = db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Material).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Fmt.EnDigits(q.Trim());
            query = query.Where(p => p.NameFa.Contains(t) || (p.NameEn != null && p.NameEn.Contains(t)) || p.Code.Contains(t));
        }
        if (cat.HasValue) query = query.Where(p => p.CategoryId == cat);
        if (status == "active") query = query.Where(p => p.IsActive);
        else if (status == "inactive") query = query.Where(p => !p.IsActive);
        else if (status == "featured") query = query.Where(p => p.IsFeatured);

        const int size = 20;
        var total = await query.CountAsync();
        page = Math.Max(1, page);
        var items = await query.OrderByDescending(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new ProductRow
            {
                Product = p,
                Image = p.Images.OrderByDescending(i => i.IsMain).Select(i => i.FilePath).FirstOrDefault(),
                Variants = p.Variants.Count,
                Docs = p.Documents.Count(d => d.IsActive),
            }).ToListAsync();
        ViewBag.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Q = q; ViewBag.Cat = cat; ViewBag.Status = status; ViewBag.Page = page;
        ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        ViewBag.Total = total;
        return View(items);
    }

    public class ProductRow
    {
        public Product Product { get; set; } = null!;
        public string? Image { get; set; }
        public int Variants { get; set; }
        public int Docs { get; set; }
    }

    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Create()
    {
        await LoadLookups();
        return View("Edit", new Product { IsActive = true });
    }

    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await db.Products
            .Include(x => x.Variants.OrderBy(v => v.SortOrder))
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.Documents.OrderByDescending(d => d.IsActive).ThenByDescending(d => d.Id))
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        await LoadLookups();
        return View(p);
    }

    private async Task LoadLookups()
    {
        ViewBag.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Materials = await db.Materials.AsNoTracking().OrderBy(m => m.NameFa).ToListAsync();
    }

    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Save(int id, [Bind("CategoryId,MaterialId,Code,Slug,NameFa,NameEn,SummaryFa,SummaryEn,DescriptionFa,DescriptionEn,Standard,Brand,Origin,IsActive,IsFeatured,CallForPrice,SortOrder")] Product input)
    {
        var p = id == 0 ? new Product() : await db.Products.FindAsync(id);
        if (p is null) return NotFound();
        input.Code = Fmt.EnDigits(input.Code?.Trim()).ToUpperInvariant();
        input.Slug = Fmt.Slugify(string.IsNullOrWhiteSpace(input.Slug) ? (input.NameEn ?? input.Code) : input.Slug);
        ModelState.Remove(nameof(Product.Slug));
        if (await db.Products.AnyAsync(x => x.Code == input.Code && x.Id != id)) ModelState.AddModelError("Code", "این کد کالا تکراری است.");
        if (await db.Products.AnyAsync(x => x.Slug == input.Slug && x.Id != id)) ModelState.AddModelError("Slug", "این نامک تکراری است.");
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId)) ModelState.AddModelError("CategoryId", "دسته‌بندی را انتخاب کنید.");
        if (!ModelState.IsValid)
        {
            await LoadLookups();
            if (id != 0)
            {
                var full = await db.Products.AsNoTracking().Include(x => x.Variants).Include(x => x.Images).Include(x => x.Documents).AsSplitQuery().FirstAsync(x => x.Id == id);
                input.Variants = full.Variants; input.Images = full.Images; input.Documents = full.Documents;
            }
            input.Id = id;
            return View("Edit", input);
        }
        p.CategoryId = input.CategoryId; p.MaterialId = input.MaterialId; p.Code = input.Code; p.Slug = input.Slug;
        p.NameFa = input.NameFa.Trim(); p.NameEn = input.NameEn?.Trim(); p.SummaryFa = input.SummaryFa; p.SummaryEn = input.SummaryEn;
        p.DescriptionFa = input.DescriptionFa; p.DescriptionEn = input.DescriptionEn; p.Standard = input.Standard; p.Brand = input.Brand;
        p.Origin = input.Origin; p.IsActive = input.IsActive; p.IsFeatured = input.IsFeatured; p.CallForPrice = input.CallForPrice;
        p.SortOrder = input.SortOrder; p.UpdatedAt = DateTime.UtcNow;
        if (id == 0) db.Products.Add(p);
        await db.SaveChangesAsync();
        audit.Log(id == 0 ? "ایجاد محصول" : "ویرایش محصول", "Product", p.Id, p.NameFa);
        await db.SaveChangesAsync();
        Success(id == 0 ? "محصول ایجاد شد. اکنون سایزها، عکس‌ها و مستندات را اضافه کنید." : "اطلاعات محصول ذخیره شد.");
        return RedirectToAction(nameof(Edit), new { id = p.Id });
    }

    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await db.Products.Include(x => x.Images).Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        var paths = p.Images.Select(i => i.FilePath).Concat(p.Documents.Select(d => d.FilePath)).ToList();
        db.Products.Remove(p);
        audit.Log("حذف محصول", "Product", id, p.NameFa);
        await db.SaveChangesAsync();
        paths.ForEach(files.Delete);
        Success("محصول حذف شد. فاکتورهای قبلی دست‌نخورده باقی می‌مانند.");
        return RedirectToAction(nameof(Index));
    }

    // ---------------- Variants ----------------
    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> SaveVariant(int productId, VariantInput input)
    {
        var product = await db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Size)) { Error("سایز الزامی است."); return RedirectToAction(nameof(Edit), new { id = productId }); }
        var v = input.Id == 0 ? new ProductVariant { ProductId = productId } : await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == input.Id && x.ProductId == productId);
        if (v is null) return NotFound();
        v.Size = input.Size.Trim(); v.PressureClass = input.PressureClass?.Trim(); v.Schedule = input.Schedule?.Trim();
        v.Sku = input.Sku?.Trim(); v.Unit = string.IsNullOrWhiteSpace(input.Unit) ? "عدد" : input.Unit.Trim(); v.ExtraSpecs = input.ExtraSpecs;
        v.Stock = input.Stock; v.IsActive = input.IsActive; v.CallForPrice = input.CallForPrice; v.SortOrder = input.SortOrder;
        v.UpdatedAt = DateTime.UtcNow;
        if (input.Id == 0) db.ProductVariants.Add(v);
        await db.SaveChangesAsync();
        if (input.Id == 0 && Can(Permissions.PricingManage) && input.SalePrice > 0)
        {
            await pricing.SetPriceAsync(v, new PriceRecord
            {
                PurchasePrice = input.PurchasePrice ?? 0, SalePrice = input.SalePrice.Value,
                CreatedById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, CreatedByName = User.DisplayName(),
            });
        }
        audit.Log(input.Id == 0 ? "افزودن سایز" : "ویرایش سایز", "ProductVariant", v.Id, $"{product.NameFa} - {v.Title}");
        await db.SaveChangesAsync();
        Success("سایز ذخیره شد.");
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#variants");
    }

    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> DeleteVariant(int productId, int id)
    {
        var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && x.ProductId == productId);
        if (v is null) return NotFound();
        db.ProductVariants.Remove(v);
        audit.Log("حذف سایز", "ProductVariant", id, v.Title);
        await db.SaveChangesAsync();
        Success("سایز حذف شد.");
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#variants");
    }

    // ---------------- Images ----------------
    [HttpPost, HasPermission(Permissions.CatalogManage), RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> UploadImages(int productId, List<IFormFile> images, bool showInSlider)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return NotFound();
        var errors = new List<string>();
        var order = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1;
        foreach (var f in images.Take(20))
        {
            var (path, err) = await files.SaveAsync(f, "products", UploadKind.Image);
            if (err is not null) { errors.Add($"{f.FileName}: {err}"); continue; }
            product.Images.Add(new ProductImage
            {
                FilePath = path!, SortOrder = order++, AltFa = product.NameFa, AltEn = product.NameEn,
                IsMain = product.Images.Count == 0, ShowInHomeSlider = showInSlider,
            });
        }
        await db.SaveChangesAsync();
        if (errors.Count > 0) Error(string.Join(" | ", errors)); else Success("تصاویر بارگذاری شد.");
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#images");
    }

    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> ImageAction(int productId, int id, string op)
    {
        var images = await db.ProductImages.Where(i => i.ProductId == productId).ToListAsync();
        var img = images.FirstOrDefault(i => i.Id == id);
        if (img is null) return NotFound();
        switch (op)
        {
            case "main": images.ForEach(i => i.IsMain = i.Id == id); break;
            case "slider": img.ShowInHomeSlider = !img.ShowInHomeSlider; break;
            case "up": img.SortOrder = Math.Max(0, img.SortOrder - 1); break;
            case "delete":
                db.ProductImages.Remove(img);
                if (img.IsMain && images.FirstOrDefault(i => i.Id != id) is { } next) next.IsMain = true;
                files.Delete(img.FilePath);
                break;
        }
        await db.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#images");
    }

    // ---------------- Documents (certificates / datasheets) ----------------
    [HttpPost, HasPermission(Permissions.CatalogManage), RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(int productId, DocumentInput input, IFormFile? file)
    {
        var product = await db.Products.FindAsync(productId);
        if (product is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.TitleFa)) { Error("عنوان مستند الزامی است."); return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#docs"); }
        var (path, err) = await files.SaveAsync(file, "docs", UploadKind.Document);
        if (err is not null) { Error(err); return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#docs"); }

        if (input.ReplacePrevious)
        {
            // نسخه‌های قبلی با همین نوع و عنوان غیرفعال می‌شوند (تاریخچه حفظ می‌شود)
            await db.ProductDocuments.Where(d => d.ProductId == productId && d.Type == input.Type && d.TitleFa == input.TitleFa.Trim() && d.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));
        }
        var doc = new ProductDocument
        {
            ProductId = productId, Type = input.Type, TitleFa = input.TitleFa.Trim(), TitleEn = input.TitleEn?.Trim(),
            Version = input.Version?.Trim(), Issuer = input.Issuer?.Trim(), FilePath = path!, OriginalFileName = Path.GetFileName(file!.FileName),
            FileSize = file.Length, IssuedAt = Fmt.ParseJalali(input.IssuedAt), ExpiresAt = Fmt.ParseJalali(input.ExpiresAt), IsActive = true,
        };
        db.ProductDocuments.Add(doc);
        audit.Log("بارگذاری مستند", "ProductDocument", null, $"{product.NameFa} - {doc.TitleFa} ({doc.Type.DisplayName()})");
        await db.SaveChangesAsync();
        Success("مستند بارگذاری شد.");
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#docs");
    }

    [HttpPost, HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> DocumentAction(int productId, int id, string op, string? returnUrl)
    {
        var doc = await db.ProductDocuments.FirstOrDefaultAsync(d => d.Id == id && d.ProductId == productId);
        if (doc is null) return NotFound();
        if (op == "toggle") doc.IsActive = !doc.IsActive;
        else if (op == "delete")
        {
            db.ProductDocuments.Remove(doc);
            files.Delete(doc.FilePath);
            audit.Log("حذف مستند", "ProductDocument", id, doc.TitleFa);
        }
        await db.SaveChangesAsync();
        if (Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl!);
        return Redirect(Url.Action(nameof(Edit), new { id = productId }) + "#docs");
    }
}

[HasPermission(Permissions.CatalogManage)]
public class DocumentsController(AppDbContext db) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, DocumentType? type, string? state)
    {
        var query = db.ProductDocuments.AsNoTracking().Include(d => d.Product).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(d => d.TitleFa.Contains(q) || d.Product!.NameFa.Contains(q) || (d.Issuer != null && d.Issuer.Contains(q)));
        if (type.HasValue) query = query.Where(d => d.Type == type);
        var now = DateTime.UtcNow;
        query = state switch
        {
            "expired" => query.Where(d => d.ExpiresAt != null && d.ExpiresAt < now),
            "soon" => query.Where(d => d.ExpiresAt != null && d.ExpiresAt >= now && d.ExpiresAt < now.AddDays(45)),
            "inactive" => query.Where(d => !d.IsActive),
            "all" => query,
            _ => query.Where(d => d.IsActive),
        };
        ViewBag.Q = q; ViewBag.Type = type; ViewBag.State = state;
        return View(await query.OrderBy(d => d.ExpiresAt == null).ThenBy(d => d.ExpiresAt).ThenByDescending(d => d.Id).Take(500).ToListAsync());
    }
}
