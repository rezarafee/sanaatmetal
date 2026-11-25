using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

public enum ImageImportMode { Append, Replace }

/// <summary>یک عکس داخل ZIP که به محصول وصل شد (یا در حالت بررسی وصل خواهد شد)</summary>
public class ImageImportFile
{
    public string Name { get; set; } = "";
    /// <summary>۰ = بدون شماره (عکس اصلی)، ۲ و بیشتر = عکس‌های بعدی گالری</summary>
    public int Index { get; set; }
    public bool Saved { get; set; }
    public bool IsMain { get; set; }
    public string? Error { get; set; }
}

public class ImageImportProduct
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int ExistingImages { get; set; }
    public int Removed { get; set; }
    public List<ImageImportFile> Files { get; set; } = new();
    public int Added => Files.Count(f => f.Saved);
    public bool HasError => Files.Any(f => f.Error is not null);
}

public record ImageImportSkipped(string Name, string Reason);

public class ImageImportResult
{
    public string FileName { get; set; } = "";
    public bool DryRun { get; set; }
    public ImageImportMode Mode { get; set; }
    public int ImageFiles { get; set; }
    public List<ImageImportProduct> Products { get; set; } = new();
    /// <summary>عکس‌هایی که محصولی با کد آن‌ها پیدا نشد</summary>
    public List<ImageImportSkipped> Unmatched { get; set; } = new();
    /// <summary>فایل‌های غیرعکس، خراب، خیلی بزرگ و ...</summary>
    public List<ImageImportSkipped> Invalid { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int ProductsWithoutImages { get; set; }

    public int Matched => Products.Sum(p => p.Files.Count(f => f.Error is null));
    public int Added => Products.Sum(p => p.Added);
    public int Removed => Products.Sum(p => p.Removed);
    public int Failed => Products.Sum(p => p.Files.Count(f => f.Error is not null));
}

/// <summary>
/// بارگذاری گروهی عکس محصولات از یک فایل ZIP. نام هر عکس کد کالای آن است:
/// <c>FL-150.jpg</c> عکس اصلی، <c>FL-150-2.jpg</c> و <c>FL-150-3.jpg</c> عکس‌های بعدی گالری.
/// فایل‌ها هیچ‌وقت روی دیسک باز نمی‌شوند (بدون مسیر پوشه)، پس zip-slip ممکن نیست؛ حجم و تعداد هم محدود است.
/// </summary>
public class ProductImageImportService(AppDbContext db, FileStorageService files, SettingsService settings, ILogger<ProductImageImportService> log)
{
    public const long MaxZipBytes = 95L * 1024 * 1024; // سقف web.config (requestLimits) صد مگابایت است
    public const int MaxImages = 1000;
    private const int MaxEntries = 3000;
    /// <summary>هر عکس داخل ZIP حداکثر این حجم را دارد (عکس دوربین؛ بعداً در سرور کوچک می‌شود)</summary>
    public const long MaxEntryBytes = 20L * 1024 * 1024;
    private const long MaxTotalBytes = 1536L * 1024 * 1024;

    // FL-150-2 / FL-150_2 / FL-150 (2) / FL-150 2
    private static readonly Regex SuffixRx = new(@"^(?<base>.+?)(?:[-_ ]+(?<n>\d{1,2})|\s*\((?<n>\d{1,2})\))$", RegexOptions.CultureInvariant);

    /// <summary>کلید مقایسه نام فایل با کد کالا؛ کاراکترهایی که در نام فایل مجاز نیستند (مثل /) با - یکی می‌شوند</summary>
    public static string Key(string? s)
    {
        var n = ProductImportService.Norm((s ?? "").Normalize(NormalizationForm.FormC));
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n) sb.Append(ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : ch);
        return sb.ToString();
    }

    public async Task<ImageImportResult> ImportAsync(Stream zipStream, string fileName, ImageImportMode mode, bool dryRun, CancellationToken ct = default)
    {
        var result = new ImageImportResult { FileName = Path.GetFileName(fileName), DryRun = dryRun, Mode = mode };

        ZipArchive zip;
        try { zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException)
        {
            result.Warnings.Add("فایل ZIP معتبر نیست یا خراب است. عکس‌ها را دوباره زیپ کنید (فرمت ZIP معمولی، نه RAR).");
            return result;
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries)
            {
                result.Warnings.Add($"تعداد فایل‌های داخل ZIP بیشتر از {Fmt.Number(MaxEntries)} است. عکس‌ها را در چند فایل ZIP جدا بارگذاری کنید.");
                return result;
            }

            // ---- کد کالا و SKU سایزها → محصول
            var products = await db.Products.AsNoTracking()
                .Select(p => new { p.Id, p.Code, p.NameFa, Images = p.Images.Count, Skus = p.Variants.Where(v => v.Sku != null).Select(v => v.Sku!).ToList() })
                .ToListAsync(ct);
            var byCode = new Dictionary<string, int>();
            foreach (var p in products) byCode.TryAdd(Key(p.Code), p.Id);
            var bySku = new Dictionary<string, int>();
            foreach (var p in products) foreach (var sku in p.Skus) bySku.TryAdd(Key(sku), p.Id);
            var info = products.ToDictionary(p => p.Id);

            int? Find(string key) => byCode.TryGetValue(key, out var id) ? id : bySku.TryGetValue(key, out id) ? id : null;

            // ---- مرحله ۱: فقط نام‌ها؛ هنوز چیزی خوانده نمی‌شود
            var plan = new Dictionary<int, List<(ZipArchiveEntry Entry, ImageImportFile File)>>();
            long declaredTotal = 0;
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // پوشه
                var full = entry.FullName.Replace('\\', '/');
                var name = Path.GetFileName(full);
                // فایل‌های سیستمی macOS / ویندوز
                if (full.StartsWith("__MACOSX/") || full.Contains("/__MACOSX/") || name.StartsWith('.') ||
                    name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!FileStorageService.IsImageExtension(name)) { result.Invalid.Add(new(full, "فرمت مجاز نیست (فقط jpg، png و webp)")); continue; }
                result.ImageFiles++;
                if (entry.Length > MaxEntryBytes) { result.Invalid.Add(new(full, $"حجم عکس بیشتر از {MaxEntryBytes / 1024 / 1024} مگابایت است")); continue; }
                if (entry.Length == 0) { result.Invalid.Add(new(full, "فایل خالی است")); continue; }

                var stem = Key(Path.GetFileNameWithoutExtension(name));
                var index = 0;
                var productId = Find(stem);
                if (productId is null && SuffixRx.Match(stem) is { Success: true } m)
                {
                    productId = Find(m.Groups["base"].Value.TrimEnd());
                    index = int.Parse(m.Groups["n"].Value);
                    if (index < 1) index = 1;
                }
                if (productId is null) { result.Unmatched.Add(new(full, "محصولی با این کد پیدا نشد")); continue; }

                if (plan.Values.Sum(l => l.Count) >= MaxImages)
                {
                    result.Invalid.Add(new(full, $"بیشتر از {Fmt.Number(MaxImages)} عکس در یک ZIP؛ در فایل بعدی بارگذاری کنید"));
                    continue;
                }
                declaredTotal += entry.Length;
                if (declaredTotal > MaxTotalBytes)
                {
                    result.Invalid.Add(new(full, "حجم کل عکس‌ها بیش از حد است؛ در فایل بعدی بارگذاری کنید"));
                    continue;
                }
                if (!plan.TryGetValue(productId.Value, out var list)) plan[productId.Value] = list = new();
                list.Add((entry, new ImageImportFile { Name = full, Index = index }));
            }

            var profile = ImageProfile.Product(await settings.GetAsync());
            var toDelete = new List<string>();

            foreach (var (productId, list) in plan.OrderBy(kv => info[kv.Key].Code))
            {
                ct.ThrowIfCancellationRequested();
                var p = info[productId];
                var item = new ImageImportProduct { ProductId = productId, Code = p.Code, Name = p.NameFa, ExistingImages = p.Images };
                // بدون شماره اول، بعد به ترتیب شماره و نام
                list.Sort((a, b) => a.File.Index != b.File.Index ? a.File.Index.CompareTo(b.File.Index) : string.Compare(a.File.Name, b.File.Name, StringComparison.OrdinalIgnoreCase));
                item.Files = list.Select(x => x.File).ToList();
                result.Products.Add(item);

                var hasUnnumbered = list[0].File.Index == 0;
                if (dryRun)
                {
                    // بررسی سبک: فقط خواندن و امضای فایل (کوچک‌سازی عکس در بررسی انجام نمی‌شود)
                    foreach (var (entry, file) in list)
                    {
                        var (bytes, readErr) = await ReadEntryAsync(entry, ct);
                        file.Error = readErr ?? (FileStorageService.HasValidSignature(bytes!, file.Name) ? null : "محتوای فایل با پسوند آن مطابقت ندارد.");
                    }
                    var firstOk = list.FirstOrDefault(x => x.File.Error is null).File;
                    // عکس اصلی: فایل بدون شماره، یا اولین عکس اگر محصول عکسی ندارد یا عکس‌ها جایگزین می‌شوند
                    if (firstOk is null) continue;
                    if ((hasUnnumbered && firstOk.Index == 0) || mode == ImageImportMode.Replace || p.Images == 0) firstOk.IsMain = true;
                    if (mode == ImageImportMode.Replace) item.Removed = p.Images;
                    continue;
                }

                var product = await db.Products.Include(x => x.Images).FirstAsync(x => x.Id == productId, ct);
                var old = product.Images.ToList();
                var order = old.Count == 0 || mode == ImageImportMode.Replace ? 0 : old.Max(i => i.SortOrder) + 1;
                var added = new List<ProductImage>();
                foreach (var (entry, file) in list)
                {
                    var (bytes, readErr) = await ReadEntryAsync(entry, ct);
                    if (readErr is not null) { file.Error = readErr; continue; }
                    var (path, err) = await files.SaveImageBytesAsync(bytes!, file.Name, "products", profile);
                    if (err is not null) { file.Error = err; continue; }
                    var img = new ProductImage { FilePath = path!, SortOrder = order++, AltFa = product.NameFa, AltEn = product.NameEn };
                    product.Images.Add(img);
                    added.Add(img);
                    file.Saved = true;
                }
                if (added.Count == 0) continue;

                if (mode == ImageImportMode.Replace)
                {
                    foreach (var o in old) { db.ProductImages.Remove(o); product.Images.Remove(o); }
                    item.Removed = old.Count;
                }
                var firstSaved = list.First(x => x.File.Saved).File;
                if ((hasUnnumbered && firstSaved.Index == 0) || !product.Images.Any(i => i.IsMain))
                {
                    foreach (var i in product.Images) i.IsMain = false;
                    added[0].IsMain = true;
                    firstSaved.IsMain = true;
                }
                product.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await db.SaveChangesAsync(ct);
                    if (mode == ImageImportMode.Replace) toDelete.AddRange(old.Select(o => o.FilePath));
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Saving imported images failed for product {Id}", productId);
                    db.ChangeTracker.Clear();
                    await files.DeleteManyAsync(added.Select(a => a.FilePath).ToList());
                    foreach (var f in item.Files.Where(f => f.Saved)) { f.Saved = false; f.IsMain = false; f.Error = "ذخیره در دیتابیس ممکن نشد"; }
                    item.Removed = 0;
                }
                db.ChangeTracker.Clear();
            }

            // فایل عکس‌های جایگزین‌شده (اگر جای دیگری استفاده نشده باشند)
            if (toDelete.Count > 0)
            {
                var stillUsed = await db.ProductImages.Where(i => toDelete.Contains(i.FilePath)).Select(i => i.FilePath).ToListAsync(ct);
                await files.DeleteManyAsync(toDelete.Except(stillUsed).ToList());
            }
        }

        result.ProductsWithoutImages = await db.Products.CountAsync(p => p.IsActive && !p.Images.Any(), ct);
        if (dryRun)
        {
            var newlyCovered = result.Products.Count(p => p.ExistingImages == 0 && p.Files.Any(f => f.Error is null));
            result.ProductsWithoutImages = Math.Max(0, result.ProductsWithoutImages - newlyCovered);
        }
        return result;
    }

    /// <summary>خواندن یک فایل از ZIP با سقف حجم واقعی (حجم اعلام‌شده در ZIP ممکن است دروغ باشد)</summary>
    private static async Task<(byte[]? Data, string? Error)> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        try
        {
            await using var s = entry.Open();
            using var ms = new MemoryStream((int)Math.Min(entry.Length, MaxEntryBytes));
            var buffer = new byte[81920];
            int read;
            while ((read = await s.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + read > MaxEntryBytes) return (null, $"حجم عکس بیشتر از {MaxEntryBytes / 1024 / 1024} مگابایت است");
                ms.Write(buffer, 0, read);
            }
            return (ms.ToArray(), null);
        }
        catch (InvalidDataException) { return (null, "فایل داخل ZIP خراب است یا رمز دارد"); }
        catch (NotSupportedException) { return (null, "روش فشرده‌سازی این فایل پشتیبانی نمی‌شود"); }
    }

    /// <summary>فهرست محصولات فعال بدون عکس (CSV) تا معلوم باشد از کدام کالاها عکس لازم است</summary>
    public async Task<byte[]> MissingImagesCsvAsync()
    {
        var rows = await db.Products.AsNoTracking().Where(p => p.IsActive && !p.Images.Any())
            .OrderBy(p => p.Code).Select(p => new { p.Code, p.NameFa, Cat = p.Category!.NameFa }).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("کد کالا,نام محصول,دسته‌بندی,نام فایل عکس");
        static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var r in rows) sb.AppendLine($"{Q(r.Code)},{Q(r.NameFa)},{Q(r.Cat)},{Q(SafeFileName(r.Code) + ".jpg")}");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>نام فایل پیشنهادی برای کد کالا (کاراکترهای غیرمجاز نام فایل → -)</summary>
    public static string SafeFileName(string code) =>
        string.Concat(code.Trim().Select(ch => ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : ch));
}
