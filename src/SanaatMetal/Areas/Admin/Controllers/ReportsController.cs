using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class ReportFilter
{
    public string Kind { get; set; } = "sales";
    public string? From { get; set; }
    public string? To { get; set; }
    public PaymentMethod? Payment { get; set; }
    public InvoiceType? Type { get; set; }
    public int? CategoryId { get; set; }
    public string Format { get; set; } = "view";
}

[HasPermission(Permissions.Reports)]
public class ReportsController(AppDbContext db, ExportService export, SettingsService settings) : AdminControllerBase
{
    public static readonly (string Key, string Title, string Icon, bool NeedsPricing)[] Kinds =
    [
        ("sales", "گزارش فروش (فاکتورها)", "bi-receipt", false),
        ("products", "فروش به تفکیک کالا", "bi-box-seam", false),
        ("customers", "فروش به تفکیک خریدار", "bi-people", false),
        ("pricelist", "لیست قیمت جاری", "bi-tags", true),
        ("pricehistory", "تاریخچه تغییرات قیمت", "bi-clock-history", true),
        ("orders", "سفارش‌های سایت", "bi-cart-check", false),
    ];

    public async Task<IActionResult> Index([FromQuery] ReportFilter f)
    {
        ViewBag.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Filter = f;
        var kind = Kinds.FirstOrDefault(k => k.Key == f.Kind);
        if (kind.Key is null) { f.Kind = "sales"; kind = Kinds[0]; }
        if (kind.NeedsPricing && !Can(Permissions.PricingView)) return Forbid();

        var table = await BuildAsync(f, kind.Title);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return f.Format switch
        {
            "excel" => File(export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{f.Kind}-{stamp}.xlsx"),
            "pdf" => File(export.ToPdf(table, (await settings.GetAsync()).CompanyNameFa), "application/pdf", $"{f.Kind}-{stamp}.pdf"),
            _ => View(table),
        };
    }

    private (DateTime? from, DateTime? to) Range(ReportFilter f)
    {
        DateTime? from = Fmt.ParseJalali(f.From) is { } a ? Fmt.ToUtc(a) : null;
        DateTime? to = Fmt.ParseJalali(f.To) is { } b ? Fmt.ToUtc(b.AddDays(1)) : null;
        return (from, to);
    }

    private string Subtitle(ReportFilter f)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.From)) parts.Add("از " + f.From);
        if (!string.IsNullOrWhiteSpace(f.To)) parts.Add("تا " + f.To);
        if (f.Payment.HasValue) parts.Add("پرداخت: " + f.Payment.Value.DisplayName());
        if (f.Type.HasValue) parts.Add(f.Type.Value.DisplayName());
        return parts.Count == 0 ? "همه تاریخ‌ها" : string.Join(" | ", parts);
    }

    private IQueryable<Invoice> Invoices(ReportFilter f)
    {
        var (from, to) = Range(f);
        var q = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft);
        q = f.Type.HasValue ? q.Where(i => i.Type == f.Type) : q.Where(i => i.Type == InvoiceType.Sale);
        if (from.HasValue) q = q.Where(i => i.IssueDate >= from);
        if (to.HasValue) q = q.Where(i => i.IssueDate < to);
        if (f.Payment.HasValue) q = q.Where(i => i.PaymentMethod == f.Payment);
        return q;
    }

    private async Task<ReportTable> BuildAsync(ReportFilter f, string title)
    {
        var t = new ReportTable { Title = title, Subtitle = Subtitle(f) };
        var profit = Can(Permissions.PricingView);
        switch (f.Kind)
        {
            case "products":
            {
                var q = db.InvoiceItems.AsNoTracking().Where(i => Invoices(f).Select(x => x.Id).Contains(i.InvoiceId));
                if (f.CategoryId.HasValue) q = q.Where(i => i.Variant != null && i.Variant.Product!.CategoryId == f.CategoryId);
                var rows = await q.Select(i => new { i.Description, i.Unit, i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal, i.PurchasePrice }).ToListAsync();
                var grouped = rows.GroupBy(r => r.Description).Select(g => new
                {
                    Name = g.Key, Unit = g.First().Unit, Qty = g.Sum(x => x.Quantity), Gross = g.Sum(x => Math.Round(x.UnitPrice * x.Quantity)),
                    Disc = g.Sum(x => x.DiscountAmount), Net = g.Sum(x => x.LineTotal), Cost = g.Sum(x => Math.Round(x.PurchasePrice * x.Quantity)),
                }).OrderByDescending(x => x.Net).ToList();
                t.Columns = [new("کالا", Width: 4), new("واحد"), new("تعداد", true), new("مبلغ ناخالص", true, 1.5f), new("تخفیف", true, 1.3f), new("مبلغ فروش", true, 1.5f)];
                if (profit) t.Columns.AddRange([new("بهای خرید", true, 1.5f), new("سود", true, 1.5f), new("درصد سود", true)]);
                foreach (var g in grouped)
                {
                    var r = new List<object?> { g.Name, g.Unit, g.Qty, g.Gross, g.Disc, g.Net };
                    if (profit) r.AddRange([g.Cost, g.Net - g.Cost, g.Cost == 0 ? null : Math.Round((g.Net - g.Cost) * 100 / g.Cost, 1)]);
                    t.Rows.Add(r.ToArray());
                }
                var tot = new List<object?> { "جمع کل", null, grouped.Sum(x => x.Qty), grouped.Sum(x => x.Gross), grouped.Sum(x => x.Disc), grouped.Sum(x => x.Net) };
                if (profit) tot.AddRange([grouped.Sum(x => x.Cost), grouped.Sum(x => x.Net - x.Cost), null]);
                t.Totals = tot.ToArray();
                break;
            }
            case "customers":
            {
                var rows = await Invoices(f).Select(i => new { i.CustomerName, i.CompanyName, i.Mobile, i.GrandTotal, i.CashAmount, i.ChequeAmount, i.DiscountTotal, i.ExtraDiscount }).ToListAsync();
                var grouped = rows.GroupBy(r => (r.Mobile ?? r.CustomerName)).Select(g => new
                {
                    Name = g.First().CustomerName, Company = g.First().CompanyName, Mobile = g.First().Mobile, Count = g.Count(),
                    Total = g.Sum(x => x.GrandTotal), Cash = g.Sum(x => x.CashAmount), Cheque = g.Sum(x => x.ChequeAmount), Disc = g.Sum(x => x.DiscountTotal + x.ExtraDiscount),
                }).OrderByDescending(x => x.Total).ToList();
                t.Columns = [new("خریدار", Width: 2), new("شرکت", Width: 2), new("موبایل", Width: 1.3f), new("تعداد فاکتور", true), new("تخفیف", true, 1.3f), new("نقدی", true, 1.5f), new("چکی", true, 1.5f), new("جمع خرید", true, 1.6f)];
                t.Rows = grouped.Select(g => new object?[] { g.Name, g.Company, g.Mobile, g.Count, g.Disc, g.Cash, g.Cheque, g.Total }).ToList();
                t.Totals = ["جمع کل", null, null, grouped.Sum(x => x.Count), grouped.Sum(x => x.Disc), grouped.Sum(x => x.Cash), grouped.Sum(x => x.Cheque), grouped.Sum(x => x.Total)];
                break;
            }
            case "pricelist":
            {
                var q = db.ProductVariants.AsNoTracking().Where(v => v.IsActive);
                if (f.CategoryId.HasValue) q = q.Where(v => v.Product!.CategoryId == f.CategoryId);
                var rows = await q.OrderBy(v => v.Product!.NameFa).ThenBy(v => v.SortOrder).Select(v => new
                {
                    v.Product!.NameFa, v.Product.Code, v.Size, v.PressureClass, v.Schedule, v.Unit, Call = v.CallForPrice || v.Product.CallForPrice,
                    P = v.Prices.Where(p => p.IsCurrent).OrderByDescending(p => p.Id).FirstOrDefault(),
                }).ToListAsync();
                t.Subtitle = "تاریخ: " + Fmt.Jalali(DateTime.UtcNow);
                t.Columns = [new("کالا", Width: 3), new("کد"), new("سایز / مشخصات", Width: 2), new("واحد"), new("قیمت خرید", true, 1.4f), new("قیمت فروش", true, 1.4f), new("تخفیف ٪", true), new("پس از تخفیف", true, 1.4f), new("نقدی", true, 1.4f), new("چکی", true, 1.4f), new("مدت چک", true), new("آخرین تغییر", Width: 1.3f)];
                foreach (var r in rows)
                {
                    var spec = string.Join(" - ", new[] { r.Size, r.PressureClass, r.Schedule }.Where(s => !string.IsNullOrEmpty(s)));
                    t.Rows.Add(r.P is null
                        ? [r.NameFa, r.Code, spec, r.Unit, null, r.Call ? "تماس بگیرید" : "ثبت نشده", null, null, null, null, null, null]
                        : [r.NameFa, r.Code, spec, r.Unit, r.P.PurchasePrice, r.P.SalePrice, r.P.DiscountPercent, Math.Round(r.P.SalePrice * (100 - r.P.DiscountPercent) / 100), r.P.CashPrice, r.P.ChequePrice, r.P.ChequeTermDays, Fmt.Jalali(r.P.CreatedAt)]);
                }
                break;
            }
            case "pricehistory":
            {
                var (from, to) = Range(f);
                var q = db.PriceRecords.AsNoTracking().AsQueryable();
                if (from.HasValue) q = q.Where(p => p.CreatedAt >= from);
                if (to.HasValue) q = q.Where(p => p.CreatedAt < to);
                if (f.CategoryId.HasValue) q = q.Where(p => p.Variant!.Product!.CategoryId == f.CategoryId);
                var rows = await q.OrderByDescending(p => p.Id).Take(5000).Select(p => new
                {
                    p.CreatedAt, p.Variant!.Product!.NameFa, p.Variant.Size, p.Variant.PressureClass, p.PurchasePrice, p.SalePrice, p.DiscountPercent,
                    p.CashPrice, p.ChequePrice, p.ChequeTermDays, p.Supplier, p.CreatedByName, p.Note,
                }).ToListAsync();
                t.Columns = [new("تاریخ", Width: 1.5f), new("کالا", Width: 3), new("سایز", Width: 1.3f), new("خرید", true, 1.4f), new("فروش", true, 1.4f), new("تخفیف ٪", true), new("نقدی", true, 1.4f), new("چکی", true, 1.4f), new("مدت چک", true), new("تأمین‌کننده", Width: 1.4f), new("ثبت‌کننده", Width: 1.4f), new("توضیح", Width: 1.6f)];
                t.Rows = rows.Select(r => new object?[] { Fmt.Jalali(r.CreatedAt, true), r.NameFa, $"{r.Size} {r.PressureClass}".Trim(), r.PurchasePrice, r.SalePrice, r.DiscountPercent, r.CashPrice, r.ChequePrice, r.ChequeTermDays, r.Supplier, r.CreatedByName, r.Note }).ToList();
                break;
            }
            case "orders":
            {
                var (from, to) = Range(f);
                var q = db.Orders.AsNoTracking().AsQueryable();
                if (from.HasValue) q = q.Where(o => o.CreatedAt >= from);
                if (to.HasValue) q = q.Where(o => o.CreatedAt < to);
                var rows = await q.OrderByDescending(o => o.Id).Select(o => new { o.Number, o.CreatedAt, o.Mobile, o.FullName, o.CompanyName, Items = o.Items.Count, o.EstimatedTotal, o.Status }).ToListAsync();
                t.Columns = [new("شماره"), new("تاریخ", Width: 1.5f), new("موبایل", Width: 1.3f), new("نام", Width: 1.6f), new("شرکت", Width: 2), new("اقلام", true), new("جمع تقریبی", true, 1.5f), new("وضعیت")];
                t.Rows = rows.Select(r => new object?[] { r.Number, Fmt.Jalali(r.CreatedAt, true), r.Mobile, r.FullName, r.CompanyName, r.Items, r.EstimatedTotal, r.Status.DisplayName() }).ToList();
                t.Totals = ["جمع", null, null, null, null, rows.Sum(r => r.Items), rows.Sum(r => r.EstimatedTotal), null];
                break;
            }
            default:
            {
                var rows = await Invoices(f).OrderBy(i => i.IssueDate).ToListAsync();
                t.Columns = [new("شماره"), new("تاریخ", Width: 1.2f), new("خریدار", Width: 2), new("شرکت", Width: 2), new("پرداخت"), new("جمع کل", true, 1.5f), new("تخفیف", true, 1.3f), new("مالیات", true, 1.3f), new("مبلغ نهایی", true, 1.6f), new("نقدی", true, 1.5f), new("چکی", true, 1.5f)];
                if (profit) t.Columns.Add(new("سود", true, 1.5f));
                foreach (var i in rows)
                {
                    var r = new List<object?> { i.Number, Fmt.Jalali(i.IssueDate), i.CustomerName, i.CompanyName, i.PaymentMethod.DisplayName(), i.Subtotal, i.DiscountTotal + i.ExtraDiscount, i.TaxAmount, i.GrandTotal, i.CashAmount, i.ChequeAmount };
                    if (profit) r.Add(i.Profit);
                    t.Rows.Add(r.ToArray());
                }
                var tot = new List<object?> { $"جمع ({rows.Count} فاکتور)", null, null, null, null, rows.Sum(i => i.Subtotal), rows.Sum(i => i.DiscountTotal + i.ExtraDiscount), rows.Sum(i => i.TaxAmount), rows.Sum(i => i.GrandTotal), rows.Sum(i => i.CashAmount), rows.Sum(i => i.ChequeAmount) };
                if (profit) tot.Add(rows.Sum(i => i.Profit));
                t.Totals = tot.ToArray();
                break;
            }
        }
        return t;
    }
}
