using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

/// <summary>یک ستون فایل ورود گروهی محصولات</summary>
public record ImportColumn(string Key, string Header, bool Required, string Help, string[] Aliases, bool Price = false);

/// <summary>یک ردیف خوانده‌شده از فایل (همه مقادیر متنی و خام)</summary>
public class ImportRow
{
    public int Line { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
    public string Get(string key) => Values.TryGetValue(key, out var v) ? v : "";
}

public class ImportIssue
{
    public int Line { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>نتیجه تحلیل یک محصول (همه ردیف‌های هم‌کد)</summary>
public class ImportProductPlan
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public bool IsNew { get; set; }
    public bool HasError { get; set; }
    public int FirstLine { get; set; }
    public int VariantsNew { get; set; }
    public int VariantsUpdated { get; set; }
    public int PriceChanges { get; set; }
    public List<ImportRow> Rows { get; set; } = new();
}

public class ImportPreview
{
    public string FileName { get; set; } = "";
    public string Token { get; set; } = "";
    public int RowCount { get; set; }
    public List<ImportProductPlan> Products { get; set; } = new();
    public List<ImportIssue> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NewCategories { get; set; } = new();
    public List<string> NewMaterials { get; set; } = new();
    public List<string> UnknownHeaders { get; set; } = new();
    public bool PricesIgnored { get; set; }

    public int NewProducts => Products.Count(p => p.IsNew && !p.HasError);
    public int UpdatedProducts => Products.Count(p => !p.IsNew && !p.HasError);
    public int ErrorProducts => Products.Count(p => p.HasError);
    public int VariantsNew => Products.Where(p => !p.HasError).Sum(p => p.VariantsNew);
    public int VariantsUpdated => Products.Where(p => !p.HasError).Sum(p => p.VariantsUpdated);
    public int PriceChanges => Products.Where(p => !p.HasError).Sum(p => p.PriceChanges);
}

public class ImportResult
{
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int VariantsCreated { get; set; }
    public int VariantsUpdated { get; set; }
    public int PricesSet { get; set; }
    public int CategoriesCreated { get; set; }
    public int MaterialsCreated { get; set; }
    public int Skipped { get; set; }
}

/// <summary>
/// ورود و خروج گروهی محصولات از Excel/CSV.
/// هر ردیف = یک سایز؛ ردیف‌های با «کد کالا»ی یکسان یک محصول هستند.
/// کلید یکتا: کد کالا (محصول) و SKU یا ترکیب سایز+کلاس+اسکجول (سایز). خانه خالی = مقدار قبلی حفظ شود.
/// </summary>
public class ProductImportService(AppDbContext db)
{
    public const int MaxRows = 20000;
    public const string DefaultSize = "استاندارد";

    static ProductImportService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static readonly ImportColumn[] Columns =
    [
        new("Code", "کد کالا", true, "شناسه یکتای محصول. با همین کد، محصول در آپلودهای بعدی پیدا و به‌روز می‌شود (تکراری ساخته نمی‌شود). چند ردیف با کد یکسان = سایزهای مختلف یک محصول.", ["code", "productcode", "کد", "کدمحصول"]),
        new("NameFa", "نام فارسی", true, "نام محصول. برای محصول جدید الزامی است.", ["namefa", "name", "نام", "نام محصول", "نام کالا"]),
        new("NameEn", "نام انگلیسی", false, "نام انگلیسی (برای سایت انگلیسی).", ["nameen", "englishname"]),
        new("Category", "دسته‌بندی", true, "نام فارسی دسته (برای محصول جدید الزامی). اگر وجود نداشته باشد ساخته می‌شود. برای زیردسته: «فلنج > فلنج گلودار».", ["category", "دسته", "گروه"]),
        new("Material", "متریال", false, "نام یا کد متریال/جنس، مثل A105. اگر نباشد ساخته می‌شود.", ["material", "جنس", "متریال / جنس"]),
        new("Size", "سایز", false, "سایز این ردیف، مثل 2\" یا DN50. اگر خالی باشد ولی قیمت یا موجودی داشته باشد، «استاندارد» ثبت می‌شود.", ["size", "سایز / اندازه", "اندازه"]),
        new("PressureClass", "کلاس فشار", false, "مثل 150# یا PN16.", ["pressureclass", "class", "کلاس"]),
        new("Schedule", "اسکجول", false, "مثل SCH40 یا STD.", ["schedule", "sch", "ضخامت"]),
        new("Sku", "کد سایز", false, "کد یکتای این سایز (اختیاری). اگر پر باشد، سایز با همین کد پیدا می‌شود.", ["sku", "کدسایز", "کد سایز (sku)"]),
        new("Unit", "واحد", false, "عدد، متر، کیلو، شاخه ... (پیش‌فرض: عدد).", ["unit"]),
        new("SalePrice", "قیمت فروش", false, "به ریال، فقط عدد. فقط اگر عوض شده باشد در تاریخچه قیمت ثبت می‌شود.", ["saleprice", "price", "قیمت"], true),
        new("PurchasePrice", "قیمت خرید", false, "به ریال، فقط عدد (فقط در پنل دیده می‌شود).", ["purchaseprice", "cost"], true),
        new("Discount", "تخفیف درصد", false, "عدد ۰ تا ۱۰۰.", ["discount", "discountpercent", "تخفیف", "تخفیف ٪"], true),
        new("Stock", "موجودی", false, "عدد (اختیاری).", ["stock", "qty"]),
        new("ExtraSpecs", "مشخصات سایز", false, "توضیح کوتاه این سایز، مثل وزن یا ابعاد.", ["extraspecs", "specs", "مشخصات"]),
        new("Standard", "استاندارد", false, "مثل ASME B16.5.", ["standard"]),
        new("Brand", "برند", false, "", ["brand"]),
        new("Origin", "کشور سازنده", false, "", ["origin", "کشور", "سازنده"]),
        new("SummaryFa", "خلاصه فارسی", false, "یک یا دو جمله برای کارت محصول.", ["summaryfa", "summary", "خلاصه"]),
        new("SummaryEn", "خلاصه انگلیسی", false, "", ["summaryen"]),
        new("DescriptionFa", "توضیحات فارسی", false, "متن کامل صفحه محصول.", ["descriptionfa", "description", "توضیحات"]),
        new("DescriptionEn", "توضیحات انگلیسی", false, "", ["descriptionen"]),
        new("Slug", "نامک", false, "آدرس صفحه محصول (اختیاری). اگر خالی باشد خودکار ساخته می‌شود.", ["slug", "آدرس", "نامک (آدرس)"]),
        new("Active", "فعال", false, "بله / خیر (پیش‌فرض برای محصول جدید: بله).", ["active", "isactive", "وضعیت"]),
        new("Featured", "ویژه", false, "بله / خیر — نمایش در محصولات ویژه.", ["featured", "isfeatured"]),
        new("CallForPrice", "تماس بگیرید", false, "بله / خیر — به‌جای قیمت «تماس بگیرید» نشان داده شود.", ["callforprice", "تماس"]),
    ];

    private static readonly string[] VariantKeys = ["Size", "PressureClass", "Schedule", "Sku", "Unit", "SalePrice", "PurchasePrice", "Discount", "Stock", "ExtraSpecs"];

    // ---------------- Reading ----------------

    /// <summary>خواندن فایل xlsx یا csv. خطای قابل نمایش برمی‌گرداند.</summary>
    public static (List<ImportRow>? rows, List<string> unknown, string? error) Read(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        List<string[]> table;
        try
        {
            table = ext switch
            {
                ".xlsx" or ".xlsm" => ReadXlsx(stream),
                ".csv" or ".txt" => ReadCsv(stream),
                ".xls" => throw new ImportFormatException("فرمت قدیمی xls پشتیبانی نمی‌شود. فایل را در اکسل با «Save As» به صورت xlsx ذخیره کنید."),
                _ => throw new ImportFormatException("فقط فایل اکسل (xlsx) یا CSV قابل قبول است."),
            };
        }
        catch (ImportFormatException ex) { return (null, [], ex.Message); }
        catch (Exception) { return (null, [], "فایل خوانده نشد. مطمئن شوید فایل سالم است و در برنامه دیگری قفل نیست."); }

        // ردیف سرستون: اولین ردیف (از ۱۰ ردیف اول) که حداقل ۲ ستون شناخته‌شده دارد
        int headerIdx = -1; Dictionary<int, string> map = new();
        for (var i = 0; i < Math.Min(10, table.Count) && headerIdx < 0; i++)
        {
            var m = MapHeaders(table[i]);
            if (m.Count >= 2) { headerIdx = i; map = m; }
        }
        if (headerIdx < 0) return (null, [], "سرستون‌ها پیدا نشد. ردیف اول فایل باید نام ستون‌ها مثل «کد کالا» و «نام فارسی» باشد (از فایل نمونه استفاده کنید).");
        if (!map.ContainsValue("Code")) return (null, [], "ستون «کد کالا» در فایل نیست. این ستون برای تشخیص محصول‌ها الزامی است.");

        var unknown = table[headerIdx].Select((h, i) => (h, i)).Where(x => !string.IsNullOrWhiteSpace(x.h) && !map.ContainsKey(x.i)).Select(x => x.h.Trim()).ToList();
        var rows = new List<ImportRow>();
        for (var i = headerIdx + 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var row = new ImportRow { Line = i + 1 };
            foreach (var (idx, key) in map)
                if (idx < cells.Length) row.Values[key] = Clean(cells[idx]);
            if (row.Values.Values.All(string.IsNullOrEmpty)) continue;
            rows.Add(row);
            if (rows.Count > MaxRows) return (null, unknown, $"حداکثر {Fmt.FaDigits(MaxRows.ToString("#,0"))} ردیف در هر فایل مجاز است. فایل را چند قسمت کنید.");
        }
        if (rows.Count == 0) return (null, unknown, "فایل هیچ ردیف داده‌ای ندارد.");
        return (rows, unknown, null);
    }

    private class ImportFormatException(string message) : Exception(message);

    /// <summary>حذف فاصله‌های اضافه و یکسان‌سازی ی و ک عربی (CSV ذخیره‌شده با ANSI ویندوز) به فارسی</summary>
    private static string Clean(string? s) => (s ?? "").Replace('\u00A0', ' ').Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Trim();

    /// <summary>یکسان‌سازی متن برای مقایسه: ی/ک عربی، فاصله‌ها، نیم‌فاصله، ارقام</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in Fmt.EnDigits(s).Trim().ToLowerInvariant())
        {
            var c = ch switch { 'ي' => 'ی', 'ى' => 'ی', 'ك' => 'ک', 'ة' => 'ه', '\u200c' => ' ', '\u00a0' => ' ', _ => ch };
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
            else sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }

    private static string HeaderKey(string s) =>
        new(Norm(s).Where(ch => char.IsLetterOrDigit(ch)).ToArray());

    private static readonly Dictionary<string, string> HeaderLookup = BuildHeaderLookup();

    private static Dictionary<string, string> BuildHeaderLookup()
    {
        var d = new Dictionary<string, string>();
        foreach (var c in Columns)
            foreach (var name in c.Aliases.Append(c.Header).Append(c.Key))
                d.TryAdd(HeaderKey(name), c.Key);
        return d;
    }

    private static Dictionary<int, string> MapHeaders(string[] cells)
    {
        var map = new Dictionary<int, string>();
        for (var i = 0; i < cells.Length; i++)
        {
            var k = HeaderKey(cells[i]);
            if (k.Length > 0 && HeaderLookup.TryGetValue(k, out var key) && !map.ContainsValue(key)) map[i] = key;
        }
        return map;
    }

    private static List<string[]> ReadXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        // اولین برگه‌ای که «کد کالا» دارد؛ وگرنه برگه اول
        var ws = wb.Worksheets.FirstOrDefault(w => w.RangeUsed() is { } r && r.Rows().Take(10).Any(row => MapHeaders(row.Cells().Select(c => c.GetString()).ToArray()).ContainsValue("Code")))
                 ?? wb.Worksheets.First();
        var used = ws.RangeUsed();
        var result = new List<string[]>();
        if (used is null) return result;
        int lastCol = used.LastColumn().ColumnNumber(), lastRow = used.LastRow().RowNumber();
        for (var r = 1; r <= lastRow; r++)
        {
            var arr = new string[lastCol];
            for (var c = 1; c <= lastCol; c++) arr[c - 1] = CellText(ws.Cell(r, c));
            result.Add(arr);
        }
        return result;
    }

    private static string CellText(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsNumber) return v.GetNumber().ToString("0.############", CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean() ? "بله" : "خیر";
        if (v.IsText) return v.GetText();
        return cell.GetFormattedString();
    }

    private static List<string[]> ReadCsv(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1256).GetString(bytes); } // CSV فارسی ذخیره‌شده با اکسل ویندوز (ANSI)
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var firstLine = text.Split('\n', 2)[0];
        var delim = new[] { ',', ';', '\t', '|' }.OrderByDescending(d => firstLine.Count(ch => ch == d)).First();

        var rows = new List<string[]>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"' && sb.Length == 0) quoted = true;
            else if (ch == delim) { cur.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\r') { }
            else if (ch == '\n') { cur.Add(sb.ToString()); sb.Clear(); rows.Add(cur.ToArray()); cur.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur.ToArray()); }
        return rows;
    }

    // ---------------- Parsing helpers ----------------

    public static bool TryDecimal(string s, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = Fmt.EnDigits(s).Replace("٬", "").Replace(",", "").Replace("،", "").Replace(" ", "")
            .Replace("٫", ".").Replace("ریال", "").Replace("تومان", "").Replace("%", "").Replace("٪", "");
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
        return false;
    }

    public static bool TryBool(string s, out bool? value)
    {
        value = null;
        var t = Norm(s);
        if (t.Length == 0) return true;
        if (t is "بله" or "بلی" or "آره" or "دارد" or "فعال" or "1" or "true" or "yes" or "y" or "x" or "✓" or "✔" or "*") { value = true; return true; }
        if (t is "خیر" or "نه" or "ندارد" or "غیرفعال" or "غیر فعال" or "0" or "false" or "no" or "n" or "-") { value = false; return true; }
        return false;
    }

    private static string NormCode(string s) => Fmt.EnDigits(s).Trim().ToUpperInvariant();

    private static string VariantKey(string? size, string? cls, string? sch) => $"{Norm(size)}|{Norm(cls)}|{Norm(sch)}";

    private static bool HasVariantData(ImportRow r) => VariantKeys.Any(k => r.Get(k).Length > 0);

    private static readonly Dictionary<string, int> MaxLen = new()
    {
        ["Code"] = 60, ["NameFa"] = 250, ["NameEn"] = 250, ["SummaryFa"] = 600, ["SummaryEn"] = 600, ["Standard"] = 150, ["Brand"] = 100,
        ["Origin"] = 100, ["Slug"] = 200, ["Size"] = 60, ["PressureClass"] = 60, ["Schedule"] = 60, ["Sku"] = 60, ["Unit"] = 30, ["ExtraSpecs"] = 300,
        ["Category"] = 400, ["Material"] = 150,
    };

    // ---------------- Analyze ----------------

    private record Lookups(
        Dictionary<string, Product> Products, Dictionary<string, string> SlugOwner,
        Dictionary<string, Category> Categories, Dictionary<string, Material> Materials,
        Dictionary<int, List<PriceRecord>> CurrentPrices);

    private async Task<Lookups> LoadLookupsAsync(bool track)
    {
        IQueryable<Product> pq = db.Products.Include(p => p.Variants);
        if (!track) pq = pq.AsNoTracking();
        var products = await pq.AsSplitQuery().ToListAsync();
        var cats = track ? await db.Categories.ToListAsync() : await db.Categories.AsNoTracking().ToListAsync();
        var mats = track ? await db.Materials.ToListAsync() : await db.Materials.AsNoTracking().ToListAsync();
        var prices = track ? await db.PriceRecords.Where(p => p.IsCurrent).ToListAsync() : await db.PriceRecords.AsNoTracking().Where(p => p.IsCurrent).ToListAsync();

        var catMap = new Dictionary<string, Category>();
        var byId = cats.ToDictionary(c => c.Id);
        foreach (var c in cats)
        {
            catMap.TryAdd(Norm(c.NameFa), c);
            catMap.TryAdd(Norm(c.Slug), c);
            if (!string.IsNullOrWhiteSpace(c.NameEn)) catMap.TryAdd(Norm(c.NameEn), c);
            if (c.ParentId is int pid && byId.TryGetValue(pid, out var parent)) catMap.TryAdd(Norm(parent.NameFa) + " > " + Norm(c.NameFa), c);
        }
        var matMap = new Dictionary<string, Material>();
        foreach (var m in mats)
        {
            matMap.TryAdd(Norm(m.NameFa), m);
            if (!string.IsNullOrWhiteSpace(m.Code)) matMap.TryAdd(Norm(m.Code), m);
            if (!string.IsNullOrWhiteSpace(m.NameEn)) matMap.TryAdd(Norm(m.NameEn), m);
        }
        return new Lookups(
            products.GroupBy(p => p.Code.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First()),
            products.GroupBy(p => p.Slug.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Code.ToUpperInvariant()),
            catMap, matMap, prices.GroupBy(p => p.VariantId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    private static string CatPathKey(string s) =>
        string.Join(" > ", s.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Norm));

    /// <summary>بررسی ردیف‌ها و ساخت پیش‌نمایش (چیزی ذخیره نمی‌شود)</summary>
    public async Task<ImportPreview> AnalyzeAsync(List<ImportRow> rows, bool canPrice)
    {
        var lk = await LoadLookupsAsync(track: false);
        return Analyze(rows, canPrice, lk);
    }

    private static ImportPreview Analyze(List<ImportRow> rows, bool canPrice, Lookups lk)
    {
        var pv = new ImportPreview { RowCount = rows.Count };
        var newCats = new HashSet<string>(); var newMats = new HashSet<string>();
        var slugsInFile = new Dictionary<string, string>();

        void Err(ImportProductPlan? plan, ImportRow r, string msg)
        {
            pv.Errors.Add(new ImportIssue { Line = r.Line, Code = plan?.Code ?? r.Get("Code"), Message = msg });
            if (plan is not null) plan.HasError = true;
        }

        // ردیف‌های بدون کد
        foreach (var r in rows.Where(r => NormCode(r.Get("Code")).Length == 0))
            Err(null, r, "«کد کالا» خالی است.");

        var groups = rows.Where(r => NormCode(r.Get("Code")).Length > 0)
            .GroupBy(r => NormCode(r.Get("Code")));
        foreach (var g in groups)
        {
            var plan = new ImportProductPlan { Code = g.Key, Rows = g.ToList(), FirstLine = g.First().Line };
            pv.Products.Add(plan);
            lk.Products.TryGetValue(g.Key, out var existing);
            plan.IsNew = existing is null;
            string First(string key) => plan.Rows.Select(r => r.Get(key)).FirstOrDefault(v => v.Length > 0) ?? "";
            plan.Name = First("NameFa") is { Length: > 0 } n ? n : existing?.NameFa ?? "";
            var catText = First("Category");
            plan.Category = catText.Length > 0 ? catText : null;

            foreach (var r in plan.Rows)
            {
                foreach (var (key, max) in MaxLen)
                    if (r.Get(key).Length > max) Err(plan, r, $"«{Header(key)}» بیشتر از {Fmt.FaDigits(max.ToString())} حرف است.");
                foreach (var key in new[] { "SalePrice", "PurchasePrice", "Discount", "Stock" })
                    if (!TryDecimal(r.Get(key), out var d)) Err(plan, r, $"«{Header(key)}» عدد معتبر نیست: {r.Get(key)}");
                    else if (d < 0) Err(plan, r, $"«{Header(key)}» نمی‌تواند منفی باشد.");
                    else if (key == "Discount" && d > 100) Err(plan, r, "«تخفیف درصد» باید بین ۰ تا ۱۰۰ باشد.");
                foreach (var key in new[] { "Active", "Featured", "CallForPrice" })
                    if (!TryBool(r.Get(key), out _)) Err(plan, r, $"«{Header(key)}» باید «بله» یا «خیر» باشد: {r.Get(key)}");
            }

            if (plan.IsNew && plan.Name.Length == 0) Err(plan, plan.Rows[0], "محصول جدید است و «نام فارسی» ندارد.");
            if (plan.IsNew && catText.Length == 0) Err(plan, plan.Rows[0], "محصول جدید است و «دسته‌بندی» ندارد.");
            if (catText.Length > 0 && !lk.Categories.ContainsKey(CatPathKey(catText)) && !lk.Categories.ContainsKey(Norm(catText)))
                newCats.Add(string.Join(" > ", catText.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
            var matText = First("Material");
            if (matText.Length > 0 && !lk.Materials.ContainsKey(Norm(matText))) newMats.Add(matText.Trim());

            // نامک
            var slugIn = First("Slug");
            if (slugIn.Length > 0)
            {
                var slug = Fmt.Slugify(slugIn);
                if (lk.SlugOwner.TryGetValue(slug, out var owner) && owner != g.Key) Err(plan, plan.Rows[0], $"نامک «{slug}» مال محصول دیگری (کد {owner}) است.");
                else if (slugsInFile.TryGetValue(slug, out var other) && other != g.Key) Err(plan, plan.Rows[0], $"نامک «{slug}» در فایل برای کد {other} هم آمده است.");
                else slugsInFile[slug] = g.Key;
            }

            // سایزها
            var seen = new Dictionary<string, int>();
            var existingVariants = existing?.Variants ?? [];
            foreach (var r in plan.Rows.Where(HasVariantData))
            {
                var size = r.Get("Size").Length > 0 ? r.Get("Size") : DefaultSize;
                var key = VariantKey(size, r.Get("PressureClass"), r.Get("Schedule"));
                var sku = r.Get("Sku");
                var dupKey = sku.Length > 0 ? "sku:" + Norm(sku) : key;
                if (seen.TryGetValue(dupKey, out var prevLine)) { Err(plan, r, $"این سایز در همین فایل تکراری است (ردیف {Fmt.FaDigits(prevLine.ToString())})."); continue; }
                seen[dupKey] = r.Line;

                var v = FindVariant(existingVariants, sku, key);
                if (v is null) plan.VariantsNew++; else plan.VariantsUpdated++;
                if (canPrice && NewPrice(r, v, lk.CurrentPrices) is not null) plan.PriceChanges++;
            }
            if (!canPrice && plan.Rows.Any(r => r.Get("SalePrice").Length + r.Get("PurchasePrice").Length + r.Get("Discount").Length > 0)) pv.PricesIgnored = true;
        }

        pv.Products = pv.Products.OrderBy(p => p.FirstLine).ToList();
        pv.Errors = pv.Errors.OrderBy(e => e.Line).ToList();
        pv.NewCategories = newCats.OrderBy(x => x).ToList();
        pv.NewMaterials = newMats.OrderBy(x => x).ToList();
        if (pv.PricesIgnored) pv.Warnings.Add("شما دسترسی مدیریت قیمت ندارید؛ ستون‌های قیمت و تخفیف نادیده گرفته می‌شوند.");
        return pv;
    }

    private static string Header(string key) => Columns.First(c => c.Key == key).Header;

    private static ProductVariant? FindVariant(IEnumerable<ProductVariant> variants, string sku, string key)
    {
        if (sku.Length > 0)
        {
            var bySku = variants.FirstOrDefault(v => !string.IsNullOrEmpty(v.Sku) && Norm(v.Sku) == Norm(sku));
            if (bySku is not null) return bySku;
        }
        return variants.FirstOrDefault(v => VariantKey(v.Size, v.PressureClass, v.Schedule) == key);
    }

    /// <summary>قیمت جدید این ردیف، یا null اگر قیمتی نیامده یا با قیمت جاری فرقی ندارد</summary>
    private static PriceRecord? NewPrice(ImportRow r, ProductVariant? v, Dictionary<int, List<PriceRecord>> current)
    {
        TryDecimal(r.Get("SalePrice"), out var sale);
        TryDecimal(r.Get("PurchasePrice"), out var buy);
        TryDecimal(r.Get("Discount"), out var disc);
        if (sale is null && buy is null && disc is null) return null;
        var last = v is not null && v.Id != 0 && current.TryGetValue(v.Id, out var list) ? list.OrderByDescending(x => x.Id).First() : null;
        var newSale = sale ?? last?.SalePrice ?? v?.SalePrice ?? 0;
        var newBuy = buy ?? last?.PurchasePrice ?? 0;
        var newDisc = disc ?? last?.DiscountPercent ?? v?.DiscountPercent ?? 0;
        if (newSale <= 0) return null;
        if (last is not null && last.SalePrice == newSale && last.PurchasePrice == newBuy && last.DiscountPercent == newDisc) return null;
        return new PriceRecord
        {
            SalePrice = newSale, PurchasePrice = newBuy, DiscountPercent = newDisc,
            CashPrice = last?.CashPrice, ChequePrice = last?.ChequePrice, ChequeTermDays = last?.ChequeTermDays, Supplier = last?.Supplier,
        };
    }

    // ---------------- Apply ----------------

    /// <summary>ذخیره در پایگاه داده (همه یا هیچ). محصولات دارای خطا رد می‌شوند.</summary>
    public async Task<ImportResult> ApplyAsync(List<ImportRow> rows, bool canPrice, string? userId, string? userName)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await ApplyCoreAsync(rows, canPrice, userId, userName);
            await tx.CommitAsync();
            return result;
        });
    }

    private async Task<ImportResult> ApplyCoreAsync(List<ImportRow> rows, bool canPrice, string? userId, string? userName)
    {
        var lk = await LoadLookupsAsync(track: true);
        var pv = Analyze(rows, canPrice, lk);
        var res = new ImportResult { Skipped = pv.ErrorProducts };
        var now = DateTime.UtcNow;

        Category ResolveCategory(string text)
        {
            if (lk.Categories.TryGetValue(CatPathKey(text), out var c) || lk.Categories.TryGetValue(Norm(text), out c)) return c;
            Category? parent = null;
            var path = "";
            foreach (var part in text.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                path = path.Length == 0 ? Norm(part) : path + " > " + Norm(part);
                if (lk.Categories.TryGetValue(path, out var found) || (parent is null && lk.Categories.TryGetValue(Norm(part), out found))) { parent = found; continue; }
                var cat = new Category { NameFa = Trunc(part, 150), Slug = UniqueCategorySlug(part), IsActive = true, Parent = parent, SortOrder = 100 };
                db.Categories.Add(cat);
                lk.Categories[path] = cat;
                if (parent is null) lk.Categories.TryAdd(Norm(part), cat);
                res.CategoriesCreated++;
                parent = cat;
            }
            return parent!;
        }

        string UniqueCategorySlug(string name)
        {
            var baseSlug = Fmt.Slugify(name); var s = baseSlug; var i = 2;
            while (lk.Categories.ContainsKey(Norm(s)) || db.Categories.Local.Any(c => c.Slug == s)) s = $"{baseSlug}-{i++}";
            return s;
        }

        Material ResolveMaterial(string text)
        {
            if (lk.Materials.TryGetValue(Norm(text), out var m)) return m;
            m = new Material { NameFa = Trunc(text.Trim(), 150), NameEn = IsAscii(text) ? Trunc(text.Trim(), 150) : null };
            db.Materials.Add(m);
            lk.Materials[Norm(text)] = m;
            res.MaterialsCreated++;
            return m;
        }

        string UniqueProductSlug(string seed, string code)
        {
            var baseSlug = Fmt.Slugify(seed); var s = baseSlug;
            if (lk.SlugOwner.TryGetValue(s, out var owner) && owner != code) s = Fmt.Slugify($"{baseSlug}-{code}");
            var i = 2; var b2 = s;
            while (lk.SlugOwner.TryGetValue(s, out owner) && owner != code) s = $"{b2}-{i++}";
            lk.SlugOwner[s] = code;
            return s;
        }

        foreach (var plan in pv.Products.Where(p => !p.HasError))
        {
            string First(string key) => plan.Rows.Select(r => r.Get(key)).FirstOrDefault(v => v.Length > 0) ?? "";
            bool? FirstBool(string key) { TryBool(First(key), out var b); return b; }
            void SetText(string key, Action<string> set) { var v = First(key); if (v.Length > 0) set(v); }

            lk.Products.TryGetValue(plan.Code, out var p);
            var isNew = p is null;
            if (p is null)
            {
                p = new Product { Code = plan.Code, IsActive = true, CreatedAt = now };
                db.Products.Add(p);
                lk.Products[plan.Code] = p;
                res.ProductsCreated++;
            }
            else res.ProductsUpdated++;

            SetText("NameFa", v => p.NameFa = v);
            SetText("NameEn", v => p.NameEn = v);
            SetText("Category", v => p.Category = ResolveCategory(v));
            SetText("Material", v => p.Material = ResolveMaterial(v));
            SetText("Standard", v => p.Standard = v);
            SetText("Brand", v => p.Brand = v);
            SetText("Origin", v => p.Origin = v);
            SetText("SummaryFa", v => p.SummaryFa = v);
            SetText("SummaryEn", v => p.SummaryEn = v);
            SetText("DescriptionFa", v => p.DescriptionFa = v);
            SetText("DescriptionEn", v => p.DescriptionEn = v);
            if (FirstBool("Active") is bool a) p.IsActive = a;
            if (FirstBool("Featured") is bool f) p.IsFeatured = f;
            if (FirstBool("CallForPrice") is bool cfp) p.CallForPrice = cfp;
            var slugIn = First("Slug");
            if (slugIn.Length > 0) { p.Slug = Fmt.Slugify(slugIn); lk.SlugOwner[p.Slug] = plan.Code; }
            else if (isNew) p.Slug = UniqueProductSlug(string.IsNullOrWhiteSpace(p.NameEn) ? p.Code : p.NameEn!, plan.Code);
            if (!isNew) p.UpdatedAt = now;

            var order = p.Variants.Count == 0 ? 0 : p.Variants.Max(v => v.SortOrder) + 1;
            foreach (var r in plan.Rows.Where(HasVariantData))
            {
                var size = r.Get("Size").Length > 0 ? r.Get("Size") : DefaultSize;
                var sku = r.Get("Sku");
                var v = FindVariant(p.Variants, sku, VariantKey(size, r.Get("PressureClass"), r.Get("Schedule")));
                if (v is null)
                {
                    v = new ProductVariant { Size = size, IsActive = true, SortOrder = order++, CreatedAt = now };
                    p.Variants.Add(v);
                    res.VariantsCreated++;
                }
                else { v.UpdatedAt = now; res.VariantsUpdated++; }
                v.Size = size;
                if (r.Get("PressureClass").Length > 0) v.PressureClass = r.Get("PressureClass");
                if (r.Get("Schedule").Length > 0) v.Schedule = r.Get("Schedule");
                if (sku.Length > 0) v.Sku = sku;
                if (r.Get("Unit").Length > 0) v.Unit = r.Get("Unit");
                if (r.Get("ExtraSpecs").Length > 0) v.ExtraSpecs = r.Get("ExtraSpecs");
                TryDecimal(r.Get("Stock"), out var stock);
                if (stock is not null) v.Stock = stock;

                if (!canPrice || NewPrice(r, v, lk.CurrentPrices) is not { } rec) continue;
                if (v.Id != 0 && lk.CurrentPrices.TryGetValue(v.Id, out var cur)) foreach (var c in cur) c.IsCurrent = false;
                rec.Note = "ورود گروهی از فایل"; rec.EffectiveFrom = now; rec.IsCurrent = true; rec.CreatedAt = now;
                rec.CreatedById = userId; rec.CreatedByName = userName;
                v.Prices.Add(rec);
                v.SalePrice = rec.SalePrice; v.DiscountPercent = rec.DiscountPercent;
                res.PricesSet++;
            }
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        return res;
    }

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
    private static bool IsAscii(string s) => s.All(ch => ch < 128);

    // ---------------- Template / export ----------------

    public static IEnumerable<ImportColumn> VisibleColumns(bool canPrice) => Columns.Where(c => canPrice || !c.Price);

    /// <summary>فایل نمونه xlsx: برگه داده + برگه راهنما + برگه دسته‌ها و متریال‌های موجود</summary>
    public async Task<byte[]> TemplateXlsxAsync(bool canPrice)
    {
        var cols = VisibleColumns(canPrice).ToList();
        var samples = new List<Dictionary<string, string>>
        {
            new() { ["Code"] = "FL-WN-150", ["NameFa"] = "فلنج گلودار کلاس ۱۵۰", ["NameEn"] = "Weld Neck Flange Class 150", ["Category"] = "فلنج", ["Material"] = "A105",
                    ["Size"] = "2\"", ["PressureClass"] = "150#", ["Schedule"] = "SCH40", ["Unit"] = "عدد", ["SalePrice"] = "1250000", ["PurchasePrice"] = "1000000", ["Stock"] = "40",
                    ["Standard"] = "ASME B16.5", ["Origin"] = "ایران", ["SummaryFa"] = "فلنج فولادی فورج", ["Active"] = "بله", ["Featured"] = "خیر", ["CallForPrice"] = "خیر" },
            new() { ["Code"] = "FL-WN-150", ["Size"] = "3\"", ["PressureClass"] = "150#", ["Schedule"] = "SCH40", ["Unit"] = "عدد", ["SalePrice"] = "1850000", ["PurchasePrice"] = "1500000", ["Stock"] = "25" },
            new() { ["Code"] = "FL-WN-150", ["Size"] = "4\"", ["PressureClass"] = "150#", ["Schedule"] = "SCH40", ["Unit"] = "عدد", ["SalePrice"] = "2400000", ["Discount"] = "5" },
            new() { ["Code"] = "VL-BALL-01", ["NameFa"] = "شیر توپی", ["Category"] = "شیرآلات > شیر توپی", ["Material"] = "SS316", ["Size"] = "1\"", ["Unit"] = "عدد", ["SalePrice"] = "3200000", ["Brand"] = "نمونه" },
        };
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("محصولات");
        ws.RightToLeft = true;
        for (var i = 0; i < cols.Count; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = cols[i].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml(cols[i].Required ? "#b45309" : "#22318a"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            if (cols[i].Help.Length > 0) cell.CreateComment().AddText(cols[i].Help);
        }
        for (var r = 0; r < samples.Count; r++)
            for (var i = 0; i < cols.Count; i++)
                if (samples[r].TryGetValue(cols[i].Key, out var v)) SetCell(ws.Cell(r + 2, i + 1), cols[i].Key, v);
        FinishSheet(ws, cols.Count, samples.Count + 1);

        var help = wb.Worksheets.Add("راهنما");
        help.RightToLeft = true;
        help.Cell(1, 1).Value = "ستون"; help.Cell(1, 2).Value = "الزامی"; help.Cell(1, 3).Value = "توضیح";
        help.Range(1, 1, 1, 3).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8eef3"));
        for (var i = 0; i < cols.Count; i++)
        {
            help.Cell(i + 2, 1).Value = cols[i].Header;
            help.Cell(i + 2, 2).Value = cols[i].Required ? (cols[i].Key == "Code" ? "همیشه" : "برای محصول جدید") : "";
            help.Cell(i + 2, 3).Value = cols[i].Help;
        }
        var n = cols.Count + 3;
        foreach (var line in GuideLines)
            help.Cell(n++, 1).Value = line;
        help.Column(1).Width = 18; help.Column(2).Width = 16; help.Column(3).Width = 90;
        help.Column(3).Style.Alignment.WrapText = true;
        help.Style.Font.FontName = "Tahoma";

        var lists = wb.Worksheets.Add("دسته‌ها و متریال‌ها");
        lists.RightToLeft = true;
        lists.Cell(1, 1).Value = "دسته‌بندی‌های موجود"; lists.Cell(1, 2).Value = "متریال‌های موجود";
        lists.Range(1, 1, 1, 2).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8eef3"));
        var cats = await db.Categories.AsNoTracking().Include(c => c.Parent).OrderBy(c => c.SortOrder).ThenBy(c => c.NameFa).ToListAsync();
        var r1 = 2;
        foreach (var c in cats) lists.Cell(r1++, 1).Value = c.Parent is null ? c.NameFa : $"{c.Parent.NameFa} > {c.NameFa}";
        var r2 = 2;
        foreach (var m in await db.Materials.AsNoTracking().OrderBy(m => m.NameFa).ToListAsync()) lists.Cell(r2++, 2).Value = m.NameFa;
        lists.Columns(1, 2).Width = 35;
        lists.Style.Font.FontName = "Tahoma";

        ws.SetTabActive(); ws.Select();
        return Save(wb);
    }

    public static readonly string[] GuideLines =
    [
        "نکته‌ها:",
        "• هر ردیف یک سایز است. برای محصولی با چند سایز، چند ردیف با «کد کالا»ی یکسان بنویسید؛ مشخصات محصول (نام، دسته و ...) فقط در ردیف اول کافی است.",
        "• اگر محصول سایز ندارد، ستون سایز را خالی بگذارید و فقط قیمت را بنویسید؛ یک سایز «استاندارد» برایش ساخته می‌شود.",
        "• خانه خالی یعنی «تغییر نده». با آپلود دوباره فقط خانه‌های پر، اطلاعات قبلی را عوض می‌کنند و چیزی پاک نمی‌شود.",
        "• قیمت‌ها به ریال و فقط عدد (ارقام فارسی و جداکننده هزارگان مشکلی ندارد).",
        "• ردیف اول باید همین سرستون‌ها باشد. ترتیب ستون‌ها مهم نیست و ستون‌های اضافه نادیده گرفته می‌شوند.",
        "• عکس محصولات از طریق صفحه ویرایش هر محصول اضافه می‌شود.",
    ];

    public static byte[] TemplateCsv(bool canPrice)
    {
        var cols = VisibleColumns(canPrice).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", cols.Select(c => Csv(c.Header))));
        var sample = new Dictionary<string, string> { ["Code"] = "FL-WN-150", ["NameFa"] = "فلنج گلودار کلاس ۱۵۰", ["Category"] = "فلنج", ["Material"] = "A105", ["Size"] = "2\"", ["PressureClass"] = "150#", ["Unit"] = "عدد", ["SalePrice"] = "1250000", ["Active"] = "بله" };
        sb.AppendLine(string.Join(",", cols.Select(c => Csv(sample.GetValueOrDefault(c.Key, "")))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Csv(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>خروجی همه محصولات با همان قالب فایل ورود (برای ویرایش در اکسل و آپلود دوباره)</summary>
    public async Task<byte[]> ExportXlsxAsync(bool canPrice)
    {
        var cols = VisibleColumns(canPrice).ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.Category!).ThenInclude(c => c.Parent).Include(p => p.Material).Include(p => p.Variants)
            .AsSplitQuery().OrderBy(p => p.Code).ToListAsync();
        var purchase = canPrice
            ? (await db.PriceRecords.AsNoTracking().Where(r => r.IsCurrent).Select(r => new { r.VariantId, r.Id, r.PurchasePrice }).ToListAsync())
                .GroupBy(r => r.VariantId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().PurchasePrice)
            : new Dictionary<int, decimal>();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("محصولات");
        ws.RightToLeft = true;
        for (var i = 0; i < cols.Count; i++)
        {
            ws.Cell(1, i + 1).Value = cols[i].Header;
            ws.Cell(1, i + 1).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml(cols[i].Required ? "#b45309" : "#22318a"));
        }
        var row = 1;
        foreach (var p in products)
        {
            var variants = p.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id).ToList();
            if (variants.Count == 0) variants.Add(null!);
            var first = true;
            foreach (var v in variants)
            {
                row++;
                var vals = new Dictionary<string, string?> { ["Code"] = p.Code };
                if (first)
                {
                    vals["NameFa"] = p.NameFa; vals["NameEn"] = p.NameEn;
                    vals["Category"] = p.Category is null ? null : p.Category.Parent is null ? p.Category.NameFa : $"{p.Category.Parent.NameFa} > {p.Category.NameFa}";
                    vals["Material"] = p.Material?.NameFa; vals["Standard"] = p.Standard; vals["Brand"] = p.Brand; vals["Origin"] = p.Origin;
                    vals["SummaryFa"] = p.SummaryFa; vals["SummaryEn"] = p.SummaryEn; vals["DescriptionFa"] = p.DescriptionFa; vals["DescriptionEn"] = p.DescriptionEn;
                    vals["Slug"] = p.Slug; vals["Active"] = p.IsActive ? "بله" : "خیر"; vals["Featured"] = p.IsFeatured ? "بله" : "خیر"; vals["CallForPrice"] = p.CallForPrice ? "بله" : "خیر";
                }
                if (v is not null)
                {
                    vals["Size"] = v.Size; vals["PressureClass"] = v.PressureClass; vals["Schedule"] = v.Schedule; vals["Sku"] = v.Sku; vals["Unit"] = v.Unit;
                    vals["ExtraSpecs"] = v.ExtraSpecs; vals["Stock"] = v.Stock?.ToString("0.##", CultureInfo.InvariantCulture);
                    vals["SalePrice"] = v.SalePrice?.ToString("0", CultureInfo.InvariantCulture);
                    vals["Discount"] = v.DiscountPercent == 0 ? null : v.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture);
                    vals["PurchasePrice"] = purchase.TryGetValue(v.Id, out var pp) ? pp.ToString("0", CultureInfo.InvariantCulture) : null;
                }
                for (var i = 0; i < cols.Count; i++)
                    if (vals.TryGetValue(cols[i].Key, out var val) && !string.IsNullOrEmpty(val)) SetCell(ws.Cell(row, i + 1), cols[i].Key, val);
                first = false;
            }
        }
        FinishSheet(ws, cols.Count, row);
        return Save(wb);
    }

    private static void SetCell(IXLCell cell, string key, string value)
    {
        if (key is "SalePrice" or "PurchasePrice" or "Stock" or "Discount" && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            cell.Value = d;
            cell.Style.NumberFormat.Format = key == "Discount" ? "0.##" : "#,##0.##";
        }
        else
        {
            cell.Value = value;
            if (key is "Code" or "Sku" or "Size") cell.Style.NumberFormat.Format = "@"; // متن بماند (صفرهای ابتدایی، " در سایز)
        }
    }

    private static void FinishSheet(IXLWorksheet ws, int cols, int lastRow)
    {
        ws.Style.Font.FontName = "Tahoma";
        ws.SheetView.FreezeRows(1);
        // ستون‌های کد/سایز متنی باشند تا اکسل آن‌ها را عدد یا تاریخ نکند
        ws.Columns(1, cols).AdjustToContents(1, Math.Min(lastRow, 300), 10, 45);
        ws.Range(1, 1, Math.Max(lastRow, 1), cols).SetAutoFilter();
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
