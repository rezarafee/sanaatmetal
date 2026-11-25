using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

/// <summary>
/// عملیات گروهی روی محصولات (حذف، فعال/غیرفعال، انتقال دسته) با کوئری‌های دسته‌ای،
/// نه یک درخواست برای هر محصول. فاکتورها و سفارش‌ها نام و قیمت کالا را جداگانه نگه می‌دارند،
/// پس با حذف محصول فقط اتصالشان به سایز (VariantId) خالی می‌شود و خودشان دست‌نخورده می‌مانند.
/// </summary>
public class ProductBulkService(AppDbContext db, FileStorageService files, ILogger<ProductBulkService> log)
{
    // سقف پارامترهای SQL Server حدود ۲۱۰۰ است؛ شناسه‌ها تکه‌تکه فرستاده می‌شوند
    private const int Chunk = 500;

    public record DeleteResult(int Products, int Variants, int Images, int Documents, int InvoiceLines, int OrderLines);

    /// <summary>آمار قبل از حذف، برای نمایش به کاربر</summary>
    public async Task<DeleteResult> PreviewAsync(IQueryable<Product> scope)
    {
        var ids = scope.Select(p => p.Id);
        var variantIds = db.ProductVariants.Where(v => ids.Contains(v.ProductId)).Select(v => v.Id);
        return new DeleteResult(
            await scope.CountAsync(),
            await variantIds.CountAsync(),
            await db.ProductImages.CountAsync(i => ids.Contains(i.ProductId)),
            await db.ProductDocuments.CountAsync(d => ids.Contains(d.ProductId)),
            await db.InvoiceItems.IgnoreQueryFilters().CountAsync(x => x.VariantId != null && variantIds.Contains(x.VariantId.Value)),
            await db.OrderItems.CountAsync(x => x.VariantId != null && variantIds.Contains(x.VariantId.Value)));
    }

    /// <summary>حذف دائمی محصولات همراه سایزها، قیمت‌ها، عکس‌ها و مستندات</summary>
    public async Task<DeleteResult> DeleteAsync(IQueryable<Product> scope)
    {
        var ids = await scope.Select(p => p.Id).ToListAsync();
        if (ids.Count == 0) return new DeleteResult(0, 0, 0, 0, 0, 0);

        var paths = new List<string>();
        int variants = 0, images = 0, docs = 0, invoiceLines = 0, orderLines = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            paths.Clear(); variants = images = docs = invoiceLines = orderLines = 0;
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var part in ids.Chunk(Chunk))
            {
                paths.AddRange(await db.ProductImages.Where(i => part.Contains(i.ProductId)).Select(i => i.FilePath).ToListAsync());
                paths.AddRange(await db.ProductDocuments.Where(d => part.Contains(d.ProductId)).Select(d => d.FilePath).ToListAsync());
                var vids = db.ProductVariants.Where(v => part.Contains(v.ProductId)).Select(v => v.Id);

                // سطرهای فاکتور/سفارش (حتی فاکتورهای سطل زباله) فقط از سایز جدا می‌شوند؛ نام، مقدار و مبلغشان می‌ماند
                invoiceLines += await db.InvoiceItems.IgnoreQueryFilters().Where(x => x.VariantId != null && vids.Contains(x.VariantId.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.VariantId, (int?)null));
                orderLines += await db.OrderItems.Where(x => x.VariantId != null && vids.Contains(x.VariantId.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.VariantId, (int?)null));

                await db.PriceRecords.Where(r => vids.Contains(r.VariantId)).ExecuteDeleteAsync();
                variants += await db.ProductVariants.Where(v => part.Contains(v.ProductId)).ExecuteDeleteAsync();
                images += await db.ProductImages.Where(i => part.Contains(i.ProductId)).ExecuteDeleteAsync();
                docs += await db.ProductDocuments.Where(d => part.Contains(d.ProductId)).ExecuteDeleteAsync();
                await db.Products.Where(p => part.Contains(p.Id)).ExecuteDeleteAsync();
            }
            await tx.CommitAsync();
        });

        // فایل‌ها بعد از ثبت قطعی حذف می‌شوند؛ فایلی که هنوز محصول دیگری به آن اشاره دارد نگه داشته می‌شود
        var distinct = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        var stillUsed = new HashSet<string>();
        foreach (var part in distinct.Chunk(Chunk))
        {
            stillUsed.UnionWith(await db.ProductImages.Where(i => part.Contains(i.FilePath)).Select(i => i.FilePath).ToListAsync());
            stillUsed.UnionWith(await db.ProductDocuments.Where(d => part.Contains(d.FilePath)).Select(d => d.FilePath).ToListAsync());
        }
        try { await files.DeleteManyAsync(distinct.Where(p => !stillUsed.Contains(p)).ToList()); }
        catch (Exception ex) { log.LogWarning(ex, "Deleting files of bulk-deleted products failed"); }

        return new DeleteResult(ids.Count, variants, images, docs, invoiceLines, orderLines);
    }

    public Task<int> SetActiveAsync(IQueryable<Product> scope, bool active) =>
        scope.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, active).SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

    public Task<int> MoveAsync(IQueryable<Product> scope, int categoryId) =>
        scope.ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, categoryId).SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
}
