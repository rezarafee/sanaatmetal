using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Controllers;

public class InvoiceItemInput
{
    public int? VariantId { get; set; }
    public string Description { get; set; } = "";
    public string? Unit { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
}

public class InvoiceInput
{
    public int Id { get; set; }
    public InvoiceType Type { get; set; } = InvoiceType.Sale;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public string? IssueDate { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? Mobile { get; set; }
    public string? EconomicCode { get; set; }
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal CashAmount { get; set; }
    public int? ChequeCount { get; set; }
    public string? ChequeDueDate { get; set; }
    public string? ChequeDetails { get; set; }
    public decimal ExtraDiscount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal ShippingCost { get; set; }
    public string? Notes { get; set; }
    public int? OrderId { get; set; }
    public string? Number { get; set; }
    public List<InvoiceItemInput> Items { get; set; } = new();
}

public class InvoicesController(AppDbContext db, InvoiceService invoices, SettingsService settings, ExportService export, AuditService audit) : AdminControllerBase
{
    private bool CanList => Can(Permissions.InvoicesView) || Can(Permissions.InvoicesCreate) || Can(Permissions.InvoicesDelete);

    public async Task<IActionResult> Index(string? q, InvoiceStatus? status, InvoiceType? type, string? from, string? to, int page = 1)
    {
        if (!CanList) return Forbid();
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Fmt.EnDigits(q.Trim());
            query = query.Where(i => i.Number.Contains(t) || i.CustomerName.Contains(t) || (i.CompanyName != null && i.CompanyName.Contains(t)) || (i.Mobile != null && i.Mobile.Contains(t)));
        }
        if (status.HasValue) query = query.Where(i => i.Status == status);
        if (type.HasValue) query = query.Where(i => i.Type == type);
        if (Fmt.ParseJalali(from) is { } f) { var fu = Fmt.ToUtc(f); query = query.Where(i => i.IssueDate >= fu); }
        if (Fmt.ParseJalali(to) is { } tt) { var tu = Fmt.ToUtc(tt.AddDays(1)); query = query.Where(i => i.IssueDate < tu); }

        const int size = 25;
        var total = await query.CountAsync();
        ViewBag.Sum = total == 0 ? 0 : (await query.Where(i => i.Status != InvoiceStatus.Cancelled).Select(i => i.GrandTotal).ToListAsync()).Sum();
        ViewBag.Q = q; ViewBag.Status = status; ViewBag.Type = type; ViewBag.From = from; ViewBag.To = to;
        ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size); ViewBag.Total = total;
        return View(await query.OrderByDescending(i => i.Serial).Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!CanList) return Forbid();
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Items.OrderBy(x => x.SortOrder)).FirstOrDefaultAsync(i => i.Id == id);
        if (inv is null) return NotFound();
        ViewBag.Settings = await settings.GetAsync();
        return View(inv);
    }

    public async Task<IActionResult> Pdf(int id)
    {
        if (!CanList) return Forbid();
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (inv is null) return NotFound();
        var bytes = export.InvoicePdf(inv, await settings.GetAsync());
        return File(bytes, "application/pdf", $"invoice-{inv.Number}.pdf");
    }

    [HasPermission(Permissions.InvoicesCreate)]
    public async Task<IActionResult> Create(int? orderId)
    {
        var s = await settings.GetAsync();
        var vm = new InvoiceInput { TaxPercent = s.DefaultTaxPercent, IssueDate = Fmt.JalaliDateOnly(Fmt.ToLocal(DateTime.UtcNow)) };
        if (orderId.HasValue && await db.Orders.Include(o => o.Items).ThenInclude(i => i.Variant).Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == orderId) is { } order)
        {
            vm.OrderId = order.Id;
            vm.CustomerName = order.FullName ?? order.CompanyName ?? order.Mobile;
            vm.CompanyName = order.CompanyName; vm.Mobile = order.Mobile;
            vm.EconomicCode = order.Customer?.EconomicCode; vm.NationalId = order.Customer?.NationalId; vm.Address = order.Customer?.Address;
            vm.Type = InvoiceType.Proforma;
            vm.Notes = order.Note;
            foreach (var it in order.Items)
                vm.Items.Add(new InvoiceItemInput
                {
                    VariantId = it.VariantId, Description = $"{it.ProductName} - {it.VariantTitle}", Unit = it.Variant?.Unit,
                    Quantity = it.Quantity, UnitPrice = it.Variant?.SalePrice ?? it.UnitPrice ?? 0, DiscountPercent = it.Variant?.DiscountPercent ?? it.DiscountPercent,
                });
        }
        if (vm.Items.Count == 0) vm.Items.Add(new InvoiceItemInput());
        return View("Edit", vm);
    }

    [HasPermission(Permissions.InvoicesEdit)]
    public async Task<IActionResult> Edit(int id)
    {
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Items.OrderBy(x => x.SortOrder)).FirstOrDefaultAsync(i => i.Id == id);
        if (inv is null) return NotFound();
        return View(new InvoiceInput
        {
            Id = inv.Id, Number = inv.Number, Type = inv.Type, Status = inv.Status, IssueDate = Fmt.JalaliDateOnly(Fmt.ToLocal(inv.IssueDate)),
            CustomerName = inv.CustomerName, CompanyName = inv.CompanyName, Mobile = inv.Mobile, EconomicCode = inv.EconomicCode,
            NationalId = inv.NationalId, Address = inv.Address, PaymentMethod = inv.PaymentMethod, CashAmount = inv.CashAmount,
            ChequeCount = inv.ChequeCount, ChequeDueDate = Fmt.JalaliDateOnly(inv.ChequeDueDate), ChequeDetails = inv.ChequeDetails,
            ExtraDiscount = inv.ExtraDiscount, TaxPercent = inv.TaxPercent, ShippingCost = inv.ShippingCost, Notes = inv.Notes, OrderId = inv.OrderId,
            Items = inv.Items.Select(x => new InvoiceItemInput
            {
                VariantId = x.VariantId, Description = x.Description, Unit = x.Unit, Quantity = x.Quantity, UnitPrice = x.UnitPrice, DiscountPercent = x.DiscountPercent,
            }).ToList(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(InvoiceInput input)
    {
        var isNew = input.Id == 0;
        if (isNew ? !Can(Permissions.InvoicesCreate) : !Can(Permissions.InvoicesEdit)) return Forbid();

        input.Items = input.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description) && i.Quantity > 0).ToList();
        if (string.IsNullOrWhiteSpace(input.CustomerName)) ModelState.AddModelError(nameof(input.CustomerName), "نام خریدار الزامی است.");
        if (input.Items.Count == 0) ModelState.AddModelError("", "حداقل یک ردیف کالا وارد کنید.");
        var mobile = string.IsNullOrWhiteSpace(input.Mobile) ? null : Fmt.NormalizeMobile(input.Mobile);
        if (!string.IsNullOrWhiteSpace(input.Mobile) && mobile is null) ModelState.AddModelError(nameof(input.Mobile), "شماره موبایل معتبر نیست.");
        if (!ModelState.IsValid) { if (input.Items.Count == 0) input.Items.Add(new()); return View("Edit", input); }

        var inv = isNew
            ? new Invoice { CreatedByName = User.DisplayName() }
            : await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == input.Id);
        if (inv is null) return NotFound();

        // اطلاعات قیمت سایزها از سرور
        var variantIds = input.Items.Where(i => i.VariantId.HasValue).Select(i => i.VariantId!.Value).Distinct().ToList();
        var variants = await db.ProductVariants.Include(v => v.Product).Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        var purchase = await db.PriceRecords.Where(p => variantIds.Contains(p.VariantId) && p.IsCurrent)
            .GroupBy(p => p.VariantId).Select(g => new { g.Key, P = g.OrderByDescending(x => x.Id).First().PurchasePrice }).ToDictionaryAsync(x => x.Key, x => x.P);
        var canPrice = Can(Permissions.PricingManage) || Can(Permissions.InvoicesEdit);

        // در ویرایش، قیمت خرید ثبت‌شده قبلی حفظ می‌شود
        var oldPurchase = inv.Items.Where(i => i.VariantId.HasValue).GroupBy(i => i.VariantId!.Value).ToDictionary(g => g.Key, g => g.First().PurchasePrice);
        db.InvoiceItems.RemoveRange(inv.Items);
        inv.Items = new List<InvoiceItem>();
        foreach (var it in input.Items)
        {
            var item = new InvoiceItem
            {
                Description = it.Description.Trim(), Unit = it.Unit?.Trim(), Quantity = it.Quantity,
                UnitPrice = Math.Max(0, it.UnitPrice), DiscountPercent = it.DiscountPercent,
            };
            if (it.VariantId is int vid && variants.TryGetValue(vid, out var v))
            {
                item.VariantId = vid;
                item.Unit ??= v.Unit;
                item.PurchasePrice = oldPurchase.TryGetValue(vid, out var op) ? op : purchase.GetValueOrDefault(vid);
                if (!canPrice)
                {
                    // کاربر بدون دسترسی قیمت‌گذاری نمی‌تواند قیمت سایزهای کاتالوگ را تغییر دهد
                    item.UnitPrice = v.SalePrice ?? item.UnitPrice;
                    item.DiscountPercent = v.DiscountPercent;
                }
            }
            inv.Items.Add(item);
        }

        inv.Type = input.Type;
        inv.Status = isNew && !Can(Permissions.InvoicesEdit) ? InvoiceStatus.Issued : input.Status;
        var issue = Fmt.ParseJalali(input.IssueDate) ?? Fmt.ToLocal(DateTime.UtcNow).Date;
        inv.IssueDate = Fmt.ToUtc(issue.Date.AddHours(12));
        inv.CustomerName = input.CustomerName.Trim(); inv.CompanyName = input.CompanyName?.Trim(); inv.Mobile = mobile;
        inv.EconomicCode = input.EconomicCode?.Trim(); inv.NationalId = input.NationalId?.Trim(); inv.Address = input.Address?.Trim();
        inv.PaymentMethod = input.PaymentMethod; inv.CashAmount = input.CashAmount; inv.ChequeCount = input.ChequeCount;
        inv.ChequeDueDate = Fmt.ParseJalali(input.ChequeDueDate); inv.ChequeDetails = input.ChequeDetails?.Trim();
        inv.ExtraDiscount = input.ExtraDiscount; inv.TaxPercent = input.TaxPercent; inv.ShippingCost = input.ShippingCost;
        inv.Notes = input.Notes?.Trim(); inv.OrderId = input.OrderId;
        inv.Customer = await invoices.UpsertCustomerAsync(mobile, input.CustomerName, input.CompanyName, input.EconomicCode, input.NationalId, input.Address);
        InvoiceService.Recalculate(inv);

        if (isNew)
        {
            inv.Serial = await invoices.NextSerialAsync();
            inv.Number = InvoiceService.FormatNumber(inv.Serial, inv.Type);
            db.Invoices.Add(inv);
        }
        else
        {
            inv.Number = InvoiceService.FormatNumber(inv.Serial, inv.Type);
            inv.UpdatedAt = DateTime.UtcNow; inv.UpdatedByName = User.DisplayName();
        }
        if (input.OrderId is int oid && await db.Orders.FindAsync(oid) is { } order) order.Status = OrderStatus.Invoiced;
        await db.SaveChangesAsync();
        if (input.OrderId is int oid2 && await db.Orders.FindAsync(oid2) is { } o2) { o2.InvoiceId = inv.Id; }
        audit.Log(isNew ? "ثبت فاکتور" : "ویرایش فاکتور", "Invoice", inv.Id, $"{inv.Number} - {inv.CustomerName} - {inv.GrandTotal:N0}");
        await db.SaveChangesAsync();
        Success(isNew ? $"فاکتور {inv.Number} ثبت شد." : $"فاکتور {inv.Number} ویرایش شد.");
        return RedirectToAction(nameof(Details), new { id = inv.Id });
    }

    [HttpPost, HasPermission(Permissions.InvoicesEdit)]
    public async Task<IActionResult> SetStatus(int id, InvoiceStatus status)
    {
        var inv = await db.Invoices.FindAsync(id);
        if (inv is null) return NotFound();
        inv.Status = status; inv.UpdatedAt = DateTime.UtcNow; inv.UpdatedByName = User.DisplayName();
        audit.Log("تغییر وضعیت فاکتور", "Invoice", id, $"{inv.Number} → {status.DisplayName()}");
        await db.SaveChangesAsync();
        Success("وضعیت فاکتور تغییر کرد.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, HasPermission(Permissions.InvoicesDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await db.Invoices.FindAsync(id);
        if (inv is null) return NotFound();
        inv.IsDeleted = true; inv.DeletedAt = DateTime.UtcNow; inv.DeletedByName = User.DisplayName();
        audit.Log("حذف فاکتور", "Invoice", id, $"{inv.Number} - {inv.CustomerName} - {inv.GrandTotal:N0}");
        await db.SaveChangesAsync();
        Success($"فاکتور {inv.Number} حذف شد (در سطل بازیافت قابل بازگردانی است).");
        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Permissions.InvoicesEdit)]
    public async Task<IActionResult> Trash() =>
        View(await db.Invoices.IgnoreQueryFilters().AsNoTracking().Where(i => i.IsDeleted).OrderByDescending(i => i.DeletedAt).Take(200).ToListAsync());

    [HttpPost, HasPermission(Permissions.InvoicesEdit)]
    public async Task<IActionResult> Restore(int id)
    {
        var inv = await db.Invoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id && i.IsDeleted);
        if (inv is null) return NotFound();
        inv.IsDeleted = false; inv.DeletedAt = null; inv.DeletedByName = null;
        audit.Log("بازگردانی فاکتور", "Invoice", id, inv.Number);
        await db.SaveChangesAsync();
        Success($"فاکتور {inv.Number} بازگردانی شد.");
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>جستجوی سایزها برای افزودن به فاکتور</summary>
    [HttpGet]
    public async Task<IActionResult> Variants(string? q)
    {
        if (!Can(Permissions.InvoicesCreate) && !Can(Permissions.InvoicesEdit)) return Forbid();
        var t = Fmt.EnDigits(q?.Trim());
        var query = db.ProductVariants.AsNoTracking().Where(v => v.IsActive);
        if (!string.IsNullOrEmpty(t))
            query = query.Where(v => v.Product!.NameFa.Contains(t) || v.Product.Code.Contains(t) || v.Size.Contains(t) || (v.Sku != null && v.Sku.Contains(t)) || (v.Product.NameEn != null && v.Product.NameEn.Contains(t)));
        var rows = await query.OrderBy(v => v.Product!.NameFa).ThenBy(v => v.SortOrder).Take(30)
            .Select(v => new { v.Id, v.Size, v.PressureClass, v.Schedule, v.Unit, v.SalePrice, v.DiscountPercent, Name = v.Product!.NameFa, v.Product.Code })
            .ToListAsync();
        return Json(rows.Select(r => new
        {
            id = r.Id,
            text = $"{r.Name} - {string.Join(" - ", new[] { r.Size, r.PressureClass, r.Schedule }.Where(s => !string.IsNullOrEmpty(s)))}",
            code = r.Code, unit = r.Unit, price = r.SalePrice ?? 0, discount = r.DiscountPercent,
        }));
    }
}

[HasPermission(Permissions.OrdersView)]
public class OrdersController(AppDbContext db, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, OrderStatus? status, int page = 1)
    {
        var query = db.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Fmt.EnDigits(q.Trim());
            query = query.Where(o => o.Number.Contains(t) || o.Mobile.Contains(t) || (o.FullName != null && o.FullName.Contains(t)) || (o.CompanyName != null && o.CompanyName.Contains(t)));
        }
        if (status.HasValue) query = query.Where(o => o.Status == status);
        const int size = 25;
        var total = await query.CountAsync();
        ViewBag.Q = q; ViewBag.Status = status; ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        return View(await query.OrderByDescending(o => o.Id).Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var o = await db.Orders.AsNoTracking().Include(x => x.Items).ThenInclude(i => i.Variant).Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        return o is null ? NotFound() : View(o);
    }

    [HttpPost, HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Update(int id, OrderStatus status, string? staffNote)
    {
        var o = await db.Orders.FindAsync(id);
        if (o is null) return NotFound();
        o.Status = status; o.StaffNote = staffNote?.Trim(); o.UpdatedAt = DateTime.UtcNow;
        audit.Log("پیگیری سفارش", "Order", id, $"{o.Number} → {status.DisplayName()}");
        await db.SaveChangesAsync();
        Success("سفارش به‌روزرسانی شد.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Delete(int id)
    {
        var o = await db.Orders.FindAsync(id);
        if (o is null) return NotFound();
        db.Orders.Remove(o);
        audit.Log("حذف سفارش", "Order", id, o.Number);
        await db.SaveChangesAsync();
        Success("سفارش حذف شد.");
        return RedirectToAction(nameof(Index));
    }
}

[HasPermission(Permissions.Customers)]
public class CustomersController(AppDbContext db, AuditService audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Fmt.EnDigits(q.Trim());
            query = query.Where(c => c.Mobile.Contains(t) || (c.FullName != null && c.FullName.Contains(t)) || (c.CompanyName != null && c.CompanyName.Contains(t)));
        }
        const int size = 30;
        var total = await query.CountAsync();
        ViewBag.Q = q; ViewBag.Page = Math.Max(1, page); ViewBag.Pages = (int)Math.Ceiling(total / (double)size);
        var rows = await query.OrderByDescending(c => c.Id).Skip((Math.Max(1, page) - 1) * size).Take(size)
            .Select(c => new CustomerRow { Customer = c, Orders = c.Orders.Count, Invoices = c.Invoices.Count }).ToListAsync();
        return View(rows);
    }

    public class CustomerRow
    {
        public Customer Customer { get; set; } = null!;
        public int Orders { get; set; }
        public int Invoices { get; set; }
    }

    public async Task<IActionResult> Edit(int? id)
    {
        var c = id is null ? new Customer() : await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (c.Id != 0)
        {
            ViewBag.Orders = await db.Orders.AsNoTracking().Where(o => o.CustomerId == c.Id).OrderByDescending(o => o.Id).Take(20).ToListAsync();
            ViewBag.Invoices = Can(Permissions.InvoicesView)
                ? await db.Invoices.AsNoTracking().Where(i => i.CustomerId == c.Id).OrderByDescending(i => i.Id).Take(20).ToListAsync()
                : new List<Invoice>();
        }
        return View(c);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int? id, [Bind("Mobile,FullName,CompanyName,EconomicCode,NationalId,Email,City,Address,Note")] Customer input)
    {
        var mobile = Fmt.NormalizeMobile(input.Mobile);
        if (mobile is null) ModelState.AddModelError("Mobile", "شماره موبایل معتبر نیست.");
        else if (await db.Customers.AnyAsync(c => c.Mobile == mobile && c.Id != (id ?? 0))) ModelState.AddModelError("Mobile", "این شماره قبلاً ثبت شده است.");
        if (!ModelState.IsValid) { input.Id = id ?? 0; return View(input); }
        var c = id is null or 0 ? new Customer() : await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        c.Mobile = mobile!; c.FullName = input.FullName; c.CompanyName = input.CompanyName; c.EconomicCode = input.EconomicCode;
        c.NationalId = input.NationalId; c.Email = input.Email; c.City = input.City; c.Address = input.Address; c.Note = input.Note; c.UpdatedAt = DateTime.UtcNow;
        if (c.Id == 0) db.Customers.Add(c);
        await db.SaveChangesAsync();
        audit.Log("ذخیره خریدار", "Customer", c.Id, c.Mobile);
        await db.SaveChangesAsync();
        Success("اطلاعات خریدار ذخیره شد.");
        return RedirectToAction(nameof(Index));
    }
}
