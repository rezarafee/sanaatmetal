using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

/// <summary>ورود گروهی محصولات از Excel/CSV: آپلود ← پیش‌نمایش و بررسی خطاها ← ثبت نهایی</summary>
[HasPermission(Permissions.CatalogManage)]
public class ProductImportController(ProductImportService importer, AuditService audit, IMemoryCache cache, SanaatMetal.Data.AppDbContext db) : AdminControllerBase
{
    private const long MaxFileBytes = 15 * 1024 * 1024;
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private bool CanPrice => Can(Permissions.PricingManage);
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string CacheKey(string token) => $"pimport:{UserId}:{token}";

    private record Pending(List<ImportRow> Rows, string FileName);

    public IActionResult Index()
    {
        ViewBag.CanPrice = CanPrice;
        return View();
    }

    public async Task<IActionResult> Template(string format = "xlsx")
    {
        if (format == "csv") return File(ProductImportService.TemplateCsv(CanPrice), "text/csv; charset=utf-8", "products-template.csv");
        return File(await importer.TemplateXlsxAsync(CanPrice), XlsxType, "products-template.xlsx");
    }

    public async Task<IActionResult> Export()
    {
        var bytes = await importer.ExportXlsxAsync(Can(Permissions.PricingView) && CanPrice);
        return File(bytes, XlsxType, $"products-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost, RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Preview(IFormFile? file)
    {
        if (file is null || file.Length == 0) { Error("فایلی انتخاب نشده است."); return RedirectToAction(nameof(Index)); }
        if (file.Length > MaxFileBytes) { Error("حجم فایل بیشتر از ۱۵ مگابایت است. فایل را چند قسمت کنید."); return RedirectToAction(nameof(Index)); }

        List<ImportRow>? rows; List<string> unknown; string? error;
        await using (var s = file.OpenReadStream())
            (rows, unknown, error) = ProductImportService.Read(s, file.FileName);
        if (error is not null) { Error(error); return RedirectToAction(nameof(Index)); }

        var preview = await importer.AnalyzeAsync(rows!, CanPrice);
        preview.FileName = Path.GetFileName(file.FileName);
        preview.UnknownHeaders = unknown;
        preview.Token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(preview.Token), new Pending(rows!, preview.FileName), TimeSpan.FromMinutes(60));
        return View(preview);
    }

    [HttpPost]
    public async Task<IActionResult> Commit(string token)
    {
        if (string.IsNullOrEmpty(token) || !cache.TryGetValue(CacheKey(token), out Pending? pending) || pending is null)
        {
            Error("زمان پیش‌نمایش تمام شده یا فایل قبلاً ثبت شده است. لطفاً فایل را دوباره بارگذاری کنید.");
            return RedirectToAction(nameof(Index));
        }
        cache.Remove(CacheKey(token));
        var r = await importer.ApplyAsync(pending.Rows, CanPrice, UserId, User.DisplayName());
        audit.Log("ورود گروهی محصولات", "Product", null,
            $"{pending.FileName}: {r.ProductsCreated} محصول جدید، {r.ProductsUpdated} به‌روزرسانی، {r.VariantsCreated} سایز جدید، {r.VariantsUpdated} سایز به‌روز، {r.PricesSet} قیمت، {r.Skipped} رد شده");
        await db.SaveChangesAsync();

        var msg = $"ورود اطلاعات انجام شد: {Fmt.Number(r.ProductsCreated)} محصول جدید، {Fmt.Number(r.ProductsUpdated)} محصول به‌روز شد، " +
                  $"{Fmt.Number(r.VariantsCreated)} سایز جدید، {Fmt.Number(r.VariantsUpdated)} سایز به‌روز و {Fmt.Number(r.PricesSet)} قیمت ثبت شد.";
        if (r.CategoriesCreated + r.MaterialsCreated > 0) msg += $" ({Fmt.Number(r.CategoriesCreated)} دسته و {Fmt.Number(r.MaterialsCreated)} متریال جدید ساخته شد.)";
        if (r.Skipped > 0) msg += $" {Fmt.Number(r.Skipped)} محصول به‌خاطر خطا ثبت نشد.";
        Success(msg);
        return RedirectToAction("Index", "Products", new { area = "Admin" });
    }
}
