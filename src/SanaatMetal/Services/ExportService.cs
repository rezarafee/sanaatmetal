using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

public record ReportColumn(string Header, bool Numeric = false, float Width = 1);

public class ReportTable
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public List<ReportColumn> Columns { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new();
    public object?[]? Totals { get; set; }
    public bool Landscape { get; set; } = true;
}

/// <summary>خروجی Excel (ClosedXML) و PDF (QuestPDF) با پشتیبانی کامل راست‌به‌چپ</summary>
public class ExportService
{
    public const string FontFamily = "Vazirmatn";
    private static bool _fontsLoaded;
    private static byte[]? _logo;
    private static readonly Lock FontLock = new();

    public static void EnsureFonts(string contentRoot)
    {
        lock (FontLock)
        {
            if (_fontsLoaded) return;
            QuestPDF.Settings.License = LicenseType.Community;
            foreach (var f in Directory.GetFiles(Path.Combine(contentRoot, "Fonts"), "*.ttf"))
            {
                using var s = File.OpenRead(f);
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(s);
            }
            var logo = Path.Combine(contentRoot, "wwwroot", "img", "brand", "logo-mark-256.png");
            if (File.Exists(logo)) _logo = File.ReadAllBytes(logo);
            _fontsLoaded = true;
        }
    }

    public byte[] ToExcel(ReportTable t)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(t.Title));
        ws.RightToLeft = true;
        var col = t.Columns.Count;

        ws.Cell(1, 1).Value = t.Title;
        ws.Range(1, 1, 1, col).Merge().Style.Font.SetBold().Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        var row = 2;
        if (!string.IsNullOrEmpty(t.Subtitle))
        {
            ws.Cell(2, 1).Value = t.Subtitle;
            ws.Range(2, 1, 2, col).Merge().Style.Font.SetFontColor(XLColor.Gray)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row = 3;
        }
        row++;
        var headerRow = row;
        for (var c = 0; c < col; c++) ws.Cell(row, c + 1).Value = t.Columns[c].Header;
        ws.Range(row, 1, row, col).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#0f3d5e"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        foreach (var r in t.Rows) { row++; WriteRow(ws, row, r, t.Columns); }
        if (t.Totals is not null)
        {
            row++;
            WriteRow(ws, row, t.Totals, t.Columns);
            ws.Range(row, 1, row, col).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8eef3"));
        }
        var range = ws.Range(headerRow, 1, row, col);
        range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Hair);
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, col).AdjustToContents(headerRow, row, 10, 60);
        ws.Style.Font.FontName = "Tahoma";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteRow(IXLWorksheet ws, int row, object?[] values, List<ReportColumn> cols)
    {
        for (var c = 0; c < values.Length && c < cols.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            switch (values[c])
            {
                case null: break;
                case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0"; break;
                case int i: cell.Value = i; break;
                case double db: cell.Value = db; cell.Style.NumberFormat.Format = "#,##0.##"; break;
                default: cell.Value = values[c]!.ToString(); break;
            }
        }
    }

    private static string SafeSheetName(string s)
    {
        var name = new string(s.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        return name.Length > 31 ? name[..31] : (name.Length == 0 ? "Report" : name);
    }

    private static string Cell(object? v) => v switch
    {
        null => "",
        decimal d => Fmt.Number(d),
        int i => Fmt.Number(i),
        double db => Fmt.Number((decimal)db),
        _ => Fmt.FaDigits(v.ToString()),
    };

    public byte[] ToPdf(ReportTable t, string companyName)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(t.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(22);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(8.5f));

            page.Header().PaddingBottom(8).Row(r =>
            {
                if (_logo != null) r.ConstantItem(40).PaddingLeft(8).Image(_logo);
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text(t.Title).FontSize(14).Bold().FontColor("#0f3d5e");
                    if (!string.IsNullOrEmpty(t.Subtitle)) c.Item().Text(Fmt.FaDigits(t.Subtitle)).FontColor(Colors.Grey.Darken1);
                });
                r.ConstantItem(200).AlignLeft().Column(c =>
                {
                    c.Item().AlignLeft().Text(companyName).Bold();
                    c.Item().AlignLeft().Text("تاریخ گزارش: " + Fmt.Jalali(DateTime.UtcNow, true)).FontSize(8);
                });
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(26);
                    foreach (var c in t.Columns) cd.RelativeColumn(c.Width);
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("#");
                    foreach (var c in t.Columns) h.Cell().Element(HeaderCell).Text(c.Header);
                });
                var i = 0;
                foreach (var r in t.Rows)
                {
                    i++;
                    var bg = i % 2 == 0 ? "#f4f7fa" : "#ffffff";
                    table.Cell().Element(x => BodyCell(x, bg)).AlignCenter().Text(Fmt.FaDigits(i.ToString()));
                    for (var c = 0; c < t.Columns.Count; c++)
                    {
                        var v = c < r.Length ? r[c] : null;
                        var cell = table.Cell().Element(x => BodyCell(x, bg));
                        (t.Columns[c].Numeric ? cell.AlignLeft() : cell).Text(Cell(v));
                    }
                }
                if (t.Totals is not null)
                {
                    table.Cell().Element(x => BodyCell(x, "#e2eaf1")).Text("");
                    for (var c = 0; c < t.Columns.Count; c++)
                    {
                        var v = c < t.Totals.Length ? t.Totals[c] : null;
                        var cell = table.Cell().Element(x => BodyCell(x, "#e2eaf1"));
                        (t.Columns[c].Numeric ? cell.AlignLeft() : cell).Text(Cell(v)).Bold();
                    }
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("صفحه ");
                x.CurrentPageNumber();
                x.Span(" از ");
                x.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background("#0f3d5e").Border(0.5f).BorderColor("#0b2c44").PaddingVertical(4).PaddingHorizontal(3)
         .AlignCenter().AlignMiddle().DefaultTextStyle(x => x.FontColor(Colors.White).Bold());

    private static IContainer BodyCell(IContainer c, string bg) =>
        c.Background(bg).BorderBottom(0.5f).BorderColor("#d5dde5").PaddingVertical(3).PaddingHorizontal(3).AlignMiddle();

    public byte[] InvoicePdf(Invoice inv, SiteSettings s)
    {
        var cur = s.CurrencyFa;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Row(r =>
                {
                    if (_logo != null) r.ConstantItem(54).PaddingLeft(8).Image(_logo);
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text(s.CompanyNameFa).FontSize(16).Bold().FontColor("#0f3d5e");
                        if (!string.IsNullOrEmpty(s.SloganFa)) c.Item().Text(s.SloganFa).FontColor(Colors.Grey.Darken1);
                        var meta = new List<string>();
                        if (!string.IsNullOrEmpty(s.EconomicCode)) meta.Add("کد اقتصادی: " + s.EconomicCode);
                        if (!string.IsNullOrEmpty(s.RegistrationNumber)) meta.Add("شماره ثبت: " + s.RegistrationNumber);
                        if (meta.Count > 0) c.Item().Text(Fmt.FaDigits(string.Join("   ", meta))).FontSize(8);
                    });
                    r.ConstantItem(170).Border(1).BorderColor("#0f3d5e").Padding(6).Column(c =>
                    {
                        c.Item().AlignCenter().Text(inv.Type.DisplayName()).FontSize(13).Bold().FontColor("#0f3d5e");
                        c.Item().Text("شماره: " + Fmt.FaDigits(inv.Number));
                        c.Item().Text("تاریخ: " + Fmt.Jalali(inv.IssueDate));
                        if (inv.Status == InvoiceStatus.Cancelled) c.Item().Text("باطل شده").FontColor(Colors.Red.Medium).Bold();
                    });
                });
                col.Item().PaddingTop(8).Border(0.7f).BorderColor("#b8c6d3").Background("#f4f7fa").Padding(6).Column(c =>
                {
                    c.Item().Text("مشخصات خریدار").Bold().FontColor("#0f3d5e");
                    c.Item().Row(r =>
                    {
                        r.RelativeItem().Text("نام: " + inv.CustomerName);
                        r.RelativeItem().Text("شرکت: " + (inv.CompanyName ?? "-"));
                        r.RelativeItem().Text("موبایل: " + Fmt.FaDigits(inv.Mobile ?? "-"));
                    });
                    c.Item().Row(r =>
                    {
                        r.RelativeItem().Text("کد اقتصادی: " + Fmt.FaDigits(inv.EconomicCode ?? "-"));
                        r.RelativeItem().Text("شناسه ملی: " + Fmt.FaDigits(inv.NationalId ?? "-"));
                        r.RelativeItem().Text("");
                    });
                    if (!string.IsNullOrEmpty(inv.Address)) c.Item().Text("نشانی: " + Fmt.FaDigits(inv.Address));
                });
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(28); cd.RelativeColumn(4); cd.RelativeColumn(1); cd.RelativeColumn(1);
                        cd.RelativeColumn(1.6f); cd.RelativeColumn(1.4f); cd.RelativeColumn(1.8f);
                    });
                    table.Header(h =>
                    {
                        foreach (var t in new[] { "#", "شرح کالا", "تعداد", "واحد", $"فی ({cur})", "تخفیف", $"مبلغ ({cur})" })
                            h.Cell().Element(HeaderCell).Text(t);
                    });
                    var i = 0;
                    foreach (var it in inv.Items.OrderBy(x => x.SortOrder))
                    {
                        i++;
                        var bg = i % 2 == 0 ? "#f4f7fa" : "#ffffff";
                        table.Cell().Element(x => BodyCell(x, bg)).AlignCenter().Text(Fmt.FaDigits(i.ToString()));
                        table.Cell().Element(x => BodyCell(x, bg)).Text(Fmt.FaDigits(it.Description));
                        table.Cell().Element(x => BodyCell(x, bg)).AlignCenter().Text(Fmt.Number(it.Quantity));
                        table.Cell().Element(x => BodyCell(x, bg)).AlignCenter().Text(it.Unit ?? "");
                        table.Cell().Element(x => BodyCell(x, bg)).AlignLeft().Text(Fmt.Number(it.UnitPrice));
                        table.Cell().Element(x => BodyCell(x, bg)).AlignLeft().Text(it.DiscountAmount > 0 ? Fmt.Number(it.DiscountAmount) : "-");
                        table.Cell().Element(x => BodyCell(x, bg)).AlignLeft().Text(Fmt.Number(it.LineTotal));
                    }
                });

                col.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().PaddingLeft(10).Column(c =>
                    {
                        c.Item().Text("نحوه پرداخت: " + inv.PaymentMethod.DisplayName()).Bold();
                        if (inv.CashAmount > 0) c.Item().Text($"مبلغ نقدی: {Fmt.Number(inv.CashAmount)} {cur}");
                        if (inv.ChequeAmount > 0)
                        {
                            c.Item().Text($"مبلغ چک: {Fmt.Number(inv.ChequeAmount)} {cur}" +
                                (inv.ChequeCount > 0 ? $" ({Fmt.FaDigits(inv.ChequeCount.ToString())} فقره)" : ""));
                            if (inv.ChequeDueDate.HasValue) c.Item().Text("سررسید: " + Fmt.FaDigits(Fmt.JalaliDateOnly(inv.ChequeDueDate)));
                            if (!string.IsNullOrEmpty(inv.ChequeDetails)) c.Item().Text(Fmt.FaDigits(inv.ChequeDetails));
                        }
                        if (!string.IsNullOrEmpty(inv.Notes)) c.Item().PaddingTop(6).Text("توضیحات: " + Fmt.FaDigits(inv.Notes));
                    });
                    r.ConstantItem(220).Border(0.7f).BorderColor("#b8c6d3").Column(c =>
                    {
                        void Line(string title, decimal v, bool bold = false)
                        {
                            c.Item().BorderBottom(0.5f).BorderColor("#d5dde5").Padding(4).Row(rr =>
                            {
                                var t = rr.RelativeItem().Text(title);
                                var n = rr.RelativeItem().AlignLeft().Text($"{Fmt.Number(v)} {cur}");
                                if (bold) { t.Bold(); n.Bold(); }
                            });
                        }
                        Line("جمع کل", inv.Subtotal);
                        if (inv.DiscountTotal > 0) Line("تخفیف اقلام", inv.DiscountTotal);
                        if (inv.ExtraDiscount > 0) Line("تخفیف فاکتور", inv.ExtraDiscount);
                        if (inv.TaxAmount > 0) Line($"مالیات و عوارض ({Fmt.Number(inv.TaxPercent)}٪)", inv.TaxAmount);
                        if (inv.ShippingCost > 0) Line("هزینه حمل", inv.ShippingCost);
                        c.Item().Background("#0f3d5e").Padding(5).Row(rr =>
                        {
                            rr.RelativeItem().Text("مبلغ قابل پرداخت").FontColor(Colors.White).Bold();
                            rr.RelativeItem().AlignLeft().Text($"{Fmt.Number(inv.GrandTotal)} {cur}").FontColor(Colors.White).Bold();
                        });
                    });
                });

                col.Item().PaddingTop(30).Row(r =>
                {
                    r.RelativeItem().AlignCenter().Text("مهر و امضای فروشنده");
                    r.RelativeItem().AlignCenter().Text("مهر و امضای خریدار");
                });
            });

            page.Footer().Column(c =>
            {
                if (!string.IsNullOrEmpty(s.InvoiceFooter)) c.Item().AlignCenter().Text(Fmt.FaDigits(s.InvoiceFooter)).FontSize(8);
                var contact = string.Join("  |  ", new[] { s.AddressFa, s.Phone, s.Email }.Where(x => !string.IsNullOrEmpty(x)));
                if (contact.Length > 0) c.Item().AlignCenter().Text(Fmt.FaDigits(contact)).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
    }
}
