using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

public class PricingService(AppDbContext db)
{
    /// <summary>ثبت قیمت جدید؛ قیمت قبلی در تاریخچه می‌ماند</summary>
    public async Task SetPriceAsync(ProductVariant variant, PriceRecord record)
    {
        var current = await db.PriceRecords.Where(p => p.VariantId == variant.Id && p.IsCurrent).ToListAsync();
        foreach (var c in current) c.IsCurrent = false;
        record.VariantId = variant.Id;
        record.IsCurrent = true;
        db.PriceRecords.Add(record);
        variant.SalePrice = record.SalePrice;
        variant.DiscountPercent = record.DiscountPercent;
        variant.UpdatedAt = DateTime.UtcNow;
    }
}

public class InvoiceService(AppDbContext db)
{
    public async Task<int> NextSerialAsync()
    {
        var max = await db.Invoices.IgnoreQueryFilters().MaxAsync(i => (int?)i.Serial) ?? 1000;
        return max + 1;
    }

    public static string FormatNumber(int serial, InvoiceType type) =>
        (type == InvoiceType.Proforma ? "P-" : "") + serial;

    /// <summary>محاسبه مجدد جمع‌ها در سرور (به مقادیر ارسالی مرورگر اعتماد نمی‌شود)</summary>
    public static void Recalculate(Invoice inv)
    {
        decimal subtotal = 0, discount = 0, cost = 0;
        var i = 0;
        foreach (var item in inv.Items)
        {
            item.SortOrder = i++;
            item.Quantity = Math.Max(0, item.Quantity);
            item.DiscountPercent = Math.Clamp(item.DiscountPercent, 0, 100);
            var gross = Math.Round(item.UnitPrice * item.Quantity, 0);
            item.DiscountAmount = Math.Round(gross * item.DiscountPercent / 100m, 0);
            item.LineTotal = gross - item.DiscountAmount;
            subtotal += gross;
            discount += item.DiscountAmount;
            cost += Math.Round(item.PurchasePrice * item.Quantity, 0);
        }
        inv.Subtotal = subtotal;
        inv.DiscountTotal = discount;
        inv.ExtraDiscount = Math.Clamp(inv.ExtraDiscount, 0, subtotal - discount);
        inv.CostTotal = cost;
        var taxable = subtotal - discount - inv.ExtraDiscount;
        inv.TaxPercent = Math.Clamp(inv.TaxPercent, 0, 100);
        inv.TaxAmount = Math.Round(taxable * inv.TaxPercent / 100m, 0);
        inv.ShippingCost = Math.Max(0, inv.ShippingCost);
        inv.GrandTotal = taxable + inv.TaxAmount + inv.ShippingCost;

        switch (inv.PaymentMethod)
        {
            case PaymentMethod.Cash: inv.CashAmount = inv.GrandTotal; inv.ChequeAmount = 0; break;
            case PaymentMethod.Cheque: inv.ChequeAmount = inv.GrandTotal; inv.CashAmount = 0; break;
            case PaymentMethod.Mixed:
                inv.CashAmount = Math.Clamp(inv.CashAmount, 0, inv.GrandTotal);
                inv.ChequeAmount = inv.GrandTotal - inv.CashAmount; break;
            default: inv.CashAmount = 0; inv.ChequeAmount = 0; break;
        }
    }

    public async Task<Customer?> UpsertCustomerAsync(string? mobile, string? name, string? company,
        string? economicCode = null, string? nationalId = null, string? address = null)
    {
        var m = Fmt.NormalizeMobile(mobile);
        if (m is null) return null;
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Mobile == m);
        if (c is null)
        {
            c = new Customer { Mobile = m };
            db.Customers.Add(c);
        }
        if (!string.IsNullOrWhiteSpace(name)) c.FullName = name.Trim();
        if (!string.IsNullOrWhiteSpace(company)) c.CompanyName = company.Trim();
        if (!string.IsNullOrWhiteSpace(economicCode)) c.EconomicCode = economicCode.Trim();
        if (!string.IsNullOrWhiteSpace(nationalId)) c.NationalId = nationalId.Trim();
        if (!string.IsNullOrWhiteSpace(address)) c.Address = address.Trim();
        c.UpdatedAt = DateTime.UtcNow;
        return c;
    }

    public async Task<string> NewOrderNumberAsync()
    {
        var d = Fmt.JalaliDateOnly(Fmt.ToLocal(DateTime.UtcNow)).Replace("/", "")[2..];
        for (var i = 0; i < 10; i++)
        {
            var n = $"{d}{RandomNumberGenerator.GetInt32(1000, 9999)}";
            if (!await db.Orders.AnyAsync(o => o.Number == n)) return n;
        }
        return $"{d}{RandomNumberGenerator.GetInt32(100000, 999999)}";
    }
}
