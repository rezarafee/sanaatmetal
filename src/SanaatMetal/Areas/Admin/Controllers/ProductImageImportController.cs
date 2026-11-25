using Microsoft.AspNetCore.Mvc;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

/// <summary>بارگذاری گروهی عکس محصولات با یک فایل ZIP؛ نام هر عکس = کد کالا</summary>
[HasPermission(Permissions.CatalogManage)]
public class ProductImageImportController(ProductImageImportService importer, AuditService audit, SanaatMetal.Data.AppDbContext db) : AdminControllerBase
{
    public IActionResult Index() => View();

    public async Task<IActionResult> Missing() =>
        File(await importer.MissingImagesCsvAsync(), "text/csv; charset=utf-8", $"products-without-images-{DateTime.UtcNow:yyyyMMdd}.csv");

    [HttpPost, RequestSizeLimit(ProductImageImportService.MaxZipBytes + 2 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageImportService.MaxZipBytes + 2 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, ImageImportMode mode = ImageImportMode.Append, bool dryRun = false)
    {
        if (file is null || file.Length == 0) { Error("فایلی انتخاب نشده است."); return RedirectToAction(nameof(Index)); }
        if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        { Error("فقط فایل ZIP قابل قبول است. عکس‌ها را انتخاب کنید و «Send to › Compressed (zipped) folder» بزنید."); return RedirectToAction(nameof(Index)); }
        if (file.Length > ProductImageImportService.MaxZipBytes)
        { Error($"حجم فایل ZIP بیشتر از {ProductImageImportService.MaxZipBytes / 1024 / 1024} مگابایت است. عکس‌ها را در چند ZIP جدا بارگذاری کنید."); return RedirectToAction(nameof(Index)); }

        ImageImportResult result;
        await using (var s = file.OpenReadStream())
            result = await importer.ImportAsync(s, file.FileName, mode, dryRun, HttpContext.RequestAborted);

        if (!dryRun && result.Added > 0)
        {
            audit.Log("بارگذاری گروهی عکس محصولات", "ProductImage", null,
                $"{result.FileName}: {result.Added} عکس برای {result.Products.Count(p => p.Added > 0)} محصول" +
                (mode == ImageImportMode.Replace ? $"، {result.Removed} عکس قبلی حذف شد" : "") +
                $"، {result.Unmatched.Count} بدون محصول، {result.Invalid.Count + result.Failed} نامعتبر");
            await db.SaveChangesAsync();
        }
        return View("Result", result);
    }
}
