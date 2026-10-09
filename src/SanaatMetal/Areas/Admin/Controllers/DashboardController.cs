using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class DashboardVm
{
    public int Products { get; set; }
    public int Variants { get; set; }
    public int NewOrders { get; set; }
    public int UnreadMessages { get; set; }
    public int FailedEmails { get; set; }
    public int InvoicesThisMonth { get; set; }
    public decimal SalesThisMonth { get; set; }
    public decimal ProfitThisMonth { get; set; }
    public int Customers { get; set; }
    public string ChartJson { get; set; } = "{}";
    public List<Order> RecentOrders { get; set; } = new();
    public List<ContactMessage> RecentMessages { get; set; } = new();
    public List<ProductDocument> ExpiringDocs { get; set; } = new();
    public List<Invoice> RecentInvoices { get; set; } = new();
}

[HasPermission(Permissions.Dashboard)]
public class DashboardController(AppDbContext db) : AdminControllerBase
{
    private static readonly PersianCalendar Pc = new();
    private static readonly string[] MonthNames = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public async Task<IActionResult> Index()
    {
        var now = Fmt.ToLocal(DateTime.UtcNow);
        var monthStart = Fmt.ToUtc(Pc.ToDateTime(Pc.GetYear(now), Pc.GetMonth(now), 1, 0, 0, 0, 0));
        var vm = new DashboardVm
        {
            Products = await db.Products.CountAsync(),
            Variants = await db.ProductVariants.CountAsync(),
            Customers = await db.Customers.CountAsync(),
        };
        if (Can(Permissions.OrdersView))
        {
            vm.NewOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New);
            vm.RecentOrders = await db.Orders.AsNoTracking().OrderByDescending(o => o.Id).Take(6).ToListAsync();
        }
        if (Can(Permissions.MessagesView))
        {
            vm.UnreadMessages = await db.ContactMessages.CountAsync(m => !m.IsRead && !m.IsArchived);
            vm.FailedEmails = await db.ContactMessages.CountAsync(m => !m.EmailSent && !m.IsArchived);
            vm.RecentMessages = await db.ContactMessages.AsNoTracking().Where(m => !m.IsArchived).OrderByDescending(m => m.Id).Take(5).ToListAsync();
        }
        if (Can(Permissions.CatalogManage))
        {
            var soon = DateTime.UtcNow.AddDays(45);
            vm.ExpiringDocs = await db.ProductDocuments.AsNoTracking().Include(d => d.Product)
                .Where(d => d.IsActive && d.ExpiresAt != null && d.ExpiresAt < soon).OrderBy(d => d.ExpiresAt).Take(8).ToListAsync();
        }
        if (Can(Permissions.InvoicesView) || Can(Permissions.Reports))
        {
            var valid = db.Invoices.AsNoTracking().Where(i => i.Type == InvoiceType.Sale && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft);
            var month = await valid.Where(i => i.IssueDate >= monthStart).Select(i => new { i.GrandTotal, i.TaxAmount, i.ShippingCost, i.CostTotal }).ToListAsync();
            vm.InvoicesThisMonth = month.Count;
            vm.SalesThisMonth = month.Sum(i => i.GrandTotal);
            vm.ProfitThisMonth = month.Sum(i => i.GrandTotal - i.TaxAmount - i.ShippingCost - i.CostTotal);
            vm.RecentInvoices = await db.Invoices.AsNoTracking().OrderByDescending(i => i.Id).Take(6).ToListAsync();

            // نمودار فروش ۶ ماه اخیر شمسی
            var labels = new List<string>();
            var starts = new List<DateTime>();
            int y = Pc.GetYear(now), m = Pc.GetMonth(now);
            for (var k = 5; k >= 0; k--)
            {
                int mm = m - k, yy = y;
                while (mm < 1) { mm += 12; yy--; }
                starts.Add(Fmt.ToUtc(Pc.ToDateTime(yy, mm, 1, 0, 0, 0, 0)));
                labels.Add(MonthNames[mm - 1]);
            }
            var rows = await valid.Where(i => i.IssueDate >= starts[0])
                .Select(i => new { i.IssueDate, i.GrandTotal, i.CashAmount, i.ChequeAmount, i.TaxAmount, i.ShippingCost, i.CostTotal }).ToListAsync();
            var sales = new decimal[6]; var cash = new decimal[6]; var cheque = new decimal[6]; var profit = new decimal[6];
            foreach (var r in rows)
            {
                var idx = starts.FindLastIndex(s => r.IssueDate >= s);
                if (idx < 0) continue;
                sales[idx] += r.GrandTotal; cash[idx] += r.CashAmount; cheque[idx] += r.ChequeAmount;
                profit[idx] += r.GrandTotal - r.TaxAmount - r.ShippingCost - r.CostTotal;
            }
            vm.ChartJson = JsonSerializer.Serialize(new
            {
                labels, sales, cash, cheque,
                profit = Can(Permissions.PricingView) ? profit : null,
            });
        }
        return View(vm);
    }
}
