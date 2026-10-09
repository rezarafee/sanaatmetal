using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class PriceInput
{
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal? CashPrice { get; set; }
    public decimal? ChequePrice { get; set; }
    public int? ChequeTermDays { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? Supplier { get; set; }
    public string? Note { get; set; }
    public bool CallForPrice { get; set; }
}

public class PriceRow
{
    public ProductVariant Variant { get; set; } = null!;
    public string ProductName { get; set; } = "";
    public string? Category { get; set; }
    public bool ProductCallForPrice { get; set; }
    public PriceRecord? Current { get; set; }
}

[HasPermission(Permissions.PricingView)]
public class PricingController(AppDbContext db, PricingService pricing, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, int? cat, int page = 1)
    {
        var query = db.ProductVariants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Fmt.EnDigits(q.Trim());
            query = query.Where(v => v.Product!.NameFa.Contains(t) || v.Product.Code.Contains(t) || v.Size.Contains(t) || (v.Sku != null && v.Sku.Contains(t)));
        }
        if (cat.HasValue) query = query.Where(v => v.Product!.CategoryId == cat);
        const int size = 40;
        var total = await query.CountAsync();
        var rows = await query.OrderBy(v => v.Product!.NameFa).ThenBy(v => v.SortOrder)
            .Skip((Math.Max(1, page) - 1) * size).Take(size)
            .Select(v => new PriceRow
            {
                Variant = v, ProductName = v.Product!.NameFa, Category = v.Product.Category!.NameFa, ProductCallForPrice = v.Product.CallForPrice,
                Current = v.Prices.Where(p => p.IsCurrent).OrderByDescending(p => p.Id).FirstOrDefault(),
            }).ToListAsync();
        ViewBag.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Q = q; ViewBag.Cat = cat; ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        return View(rows);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var v = await db.ProductVariants.Include(x => x.Product).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound();
        ViewBag.Variant = v;
        ViewBag.History = await db.PriceRecords.AsNoTracking().Where(p => p.VariantId == id).OrderByDescending(p => p.Id).Take(100).ToListAsync();
        var cur = await db.PriceRecords.AsNoTracking().Where(p => p.VariantId == id && p.IsCurrent).OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        return View(new PriceInput
        {
            PurchasePrice = cur?.PurchasePrice ?? 0, SalePrice = cur?.SalePrice ?? 0, CashPrice = cur?.CashPrice, ChequePrice = cur?.ChequePrice,
            ChequeTermDays = cur?.ChequeTermDays, DiscountPercent = cur?.DiscountPercent ?? 0, Supplier = cur?.Supplier, CallForPrice = v.CallForPrice,
        });
    }

    [HttpPost, HasPermission(Permissions.PricingManage)]
    public async Task<IActionResult> Edit(int id, PriceInput input)
    {
        var v = await db.ProductVariants.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound();
        v.CallForPrice = input.CallForPrice;
        if (input.SalePrice <= 0 && !input.CallForPrice)
        {
            Error("قیمت فروش را وارد کنید یا گزینه «تماس بگیرید» را فعال کنید.");
            return RedirectToAction(nameof(Edit), new { id });
        }
        if (input.SalePrice > 0)
        {
            await pricing.SetPriceAsync(v, new PriceRecord
            {
                PurchasePrice = Math.Max(0, input.PurchasePrice), SalePrice = input.SalePrice, CashPrice = input.CashPrice, ChequePrice = input.ChequePrice,
                ChequeTermDays = input.ChequeTermDays, DiscountPercent = Math.Clamp(input.DiscountPercent, 0, 100), Supplier = input.Supplier?.Trim(),
                Note = input.Note?.Trim(), CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier), CreatedByName = User.DisplayName(),
            });
        }
        audit.Log("ثبت قیمت", "ProductVariant", id, $"{v.Product!.NameFa} {v.Title}: فروش {input.SalePrice:N0}، خرید {input.PurchasePrice:N0}، تخفیف {input.DiscountPercent}٪");
        await db.SaveChangesAsync();
        Success("قیمت جدید ثبت شد و قیمت قبلی در تاریخچه ماند.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>تغییر گروهی قیمت (مثلاً افزایش ماهانه) برای یک دسته یا همه</summary>
    [HttpPost, HasPermission(Permissions.PricingManage)]
    public async Task<IActionResult> Bulk(int? categoryId, decimal percent, bool applyPurchase, string? note)
    {
        if (percent is 0 or < -90 or > 500) { Error("درصد تغییر معتبر نیست."); return RedirectToAction(nameof(Index)); }
        var variants = await db.ProductVariants.Include(v => v.Product)
            .Where(v => v.SalePrice != null && (categoryId == null || v.Product!.CategoryId == categoryId)).ToListAsync();
        var ids = variants.Select(v => v.Id).ToList();
        var currents = await db.PriceRecords.Where(p => ids.Contains(p.VariantId) && p.IsCurrent).ToListAsync();
        var factor = 1 + percent / 100m;
        static decimal R(decimal x) => Math.Round(x / 1000m) * 1000m;
        foreach (var v in variants)
        {
            var cur = currents.Where(c => c.VariantId == v.Id).OrderByDescending(c => c.Id).FirstOrDefault();
            if (cur is null) continue;
            await pricing.SetPriceAsync(v, new PriceRecord
            {
                PurchasePrice = applyPurchase ? R(cur.PurchasePrice * factor) : cur.PurchasePrice,
                SalePrice = R(cur.SalePrice * factor),
                CashPrice = cur.CashPrice is null ? null : R(cur.CashPrice.Value * factor),
                ChequePrice = cur.ChequePrice is null ? null : R(cur.ChequePrice.Value * factor),
                ChequeTermDays = cur.ChequeTermDays, DiscountPercent = cur.DiscountPercent, Supplier = cur.Supplier,
                Note = string.IsNullOrWhiteSpace(note) ? $"تغییر گروهی {percent}٪" : note,
                CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier), CreatedByName = User.DisplayName(),
            });
        }
        audit.Log("تغییر گروهی قیمت", "Pricing", categoryId, $"{percent}٪ روی {variants.Count} سایز");
        await db.SaveChangesAsync();
        Success($"قیمت {Fmt.Number(variants.Count)} سایز به میزان {Fmt.Number(percent)}٪ تغییر کرد.");
        return RedirectToAction(nameof(Index), new { cat = categoryId });
    }
}
