using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Infrastructure;
using SanaatMetal.Models;
using SanaatMetal.Services;
using SanaatMetal.ViewModels;

namespace SanaatMetal.Controllers;

public static class ProductQueries
{
    public static async Task<List<ProductCardVm>> ToCardsAsync(IQueryable<Product> q, Lang lang)
    {
        var rows = await q.Select(p => new
        {
            p.Id, p.Slug, p.NameFa, p.NameEn, p.SummaryFa, p.SummaryEn, p.Standard, p.CallForPrice,
            CatFa = p.Category!.NameFa, CatEn = p.Category.NameEn,
            MatFa = p.Material != null ? p.Material.NameFa : null, MatEn = p.Material != null ? p.Material.NameEn : null,
            Image = p.Images.OrderByDescending(i => i.IsMain).ThenBy(i => i.SortOrder).Select(i => i.FilePath).FirstOrDefault(),
            Variants = p.Variants.Where(v => v.IsActive).Select(v => new { v.Id, v.SalePrice, v.DiscountPercent, v.CallForPrice }).ToList(),
        }).ToListAsync();

        return rows.Select(r =>
        {
            var priced = r.Variants.Where(v => !v.CallForPrice && v.SalePrice > 0).ToList();
            var cheapest = priced.OrderBy(v => v.SalePrice * (100 - v.DiscountPercent)).FirstOrDefault();
            return new ProductCardVm
            {
                Id = r.Id, Slug = r.Slug, Name = lang.Pick(r.NameFa, r.NameEn), Summary = lang.Pick(r.SummaryFa, r.SummaryEn),
                Category = lang.Pick(r.CatFa, r.CatEn), Material = r.MatFa is null ? null : lang.Pick(r.MatFa, r.MatEn),
                Standard = r.Standard, Image = r.Image ?? "/img/placeholder.svg",
                CallForPrice = r.CallForPrice || priced.Count == 0,
                MinPrice = cheapest?.SalePrice,
                MinFinalPrice = cheapest is null ? null : Math.Round(cheapest.SalePrice!.Value * (100 - cheapest.DiscountPercent) / 100m, 0),
                MaxDiscount = priced.Count == 0 ? 0 : priced.Max(v => v.DiscountPercent),
                VariantCount = r.Variants.Count,
                SingleVariantId = r.Variants.Count == 1 ? r.Variants[0].Id : null,
            };
        }).ToList();
    }
}

public class HomeController(AppDbContext db, Lang lang, SettingsService settings, EmailService email, ILogger<HomeController> log) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var s = await settings.GetAsync();
        var vm = new HomeVm
        {
            Categories = await db.Categories.AsNoTracking().Where(c => c.IsActive && c.ParentId == null).OrderBy(c => c.SortOrder).ToListAsync(),
            Featured = await ProductQueries.ToCardsAsync(db.Products.AsNoTracking().Where(p => p.IsActive && p.IsFeatured).OrderBy(p => p.SortOrder).ThenByDescending(p => p.Id).Take(12), lang),
            Clients = await db.Clients.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(),
            ProductCount = await db.Products.CountAsync(p => p.IsActive),
            DocumentCount = await db.ProductDocuments.CountAsync(d => d.IsActive),
        };

        var slides = await db.ProductImages.AsNoTracking()
            .Where(i => i.ShowInHomeSlider && i.Product!.IsActive)
            .OrderBy(i => i.SortOrder).ThenByDescending(i => i.Id).Take(8)
            .Select(i => new { i.FilePath, i.Product!.NameFa, i.Product.NameEn, i.Product.SummaryFa, i.Product.SummaryEn, i.Product.Slug })
            .ToListAsync();
        vm.Slides = slides.Select(x => new HeroSlideVm
        {
            Image = x.FilePath, Title = lang.Pick(x.NameFa, x.NameEn), Subtitle = lang.Pick(x.SummaryFa, x.SummaryEn), Url = "/products/" + x.Slug,
        }).ToList();
        ViewBag.Settings = s;
        return View(vm);
    }

    [HttpGet("/about")]
    public async Task<IActionResult> About()
    {
        ViewBag.Clients = await db.Clients.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
        return View(await settings.GetAsync());
    }

    [HttpGet("/contact")]
    public async Task<IActionResult> Contact() => View(await settings.GetAsync());

    [HttpPost("/contact"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Contact([Bind(Prefix = "Contact")] ContactFormVm form, string? returnUrl)
    {
        var back = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/#contact";
        if (!string.IsNullOrEmpty(form.Website)) return LocalRedirect(back); // ربات
        if (!ModelState.IsValid)
        {
            TempData["ContactError"] = lang["ContactInvalid"];
            return LocalRedirect(back);
        }

        var msg = new ContactMessage
        {
            Name = form.Name.Trim(), Email = form.Email.Trim(), Phone = form.Phone?.Trim(), Company = form.Company?.Trim(),
            Subject = form.Subject?.Trim(), Body = form.Body.Trim(), Culture = lang.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };
        // ابتدا ذخیره در دیتابیس تا اگر سرور ایمیل قطع بود پیام از دست نرود
        db.ContactMessages.Add(msg);
        await db.SaveChangesAsync();

        var s = await settings.GetAsync();
        var to = s.NotifyEmail ?? s.Email;
        if (!string.IsNullOrWhiteSpace(to))
        {
            var e = (string? v) => WebUtility.HtmlEncode(v ?? "-");
            var html = $"""
                <div dir="rtl" style="font-family:Tahoma;line-height:2">
                <h3>درخواست جدید از سایت</h3>
                <p><b>نام:</b> {e(msg.Name)}<br><b>ایمیل:</b> {e(msg.Email)}<br><b>تلفن:</b> {e(msg.Phone)}<br>
                <b>شرکت:</b> {e(msg.Company)}<br><b>موضوع:</b> {e(msg.Subject)}</p>
                <p style="white-space:pre-wrap">{e(msg.Body)}</p></div>
                """;
            var (ok, err) = await email.SendAsync(to, "درخواست جدید: " + (msg.Subject ?? msg.Name), html, msg.Email);
            msg.EmailSent = ok;
            msg.EmailError = err;
            await db.SaveChangesAsync();
        }
        TempData["ContactOk"] = lang["ContactThanks"];
        return LocalRedirect(back);
    }

    [HttpGet("/lang/{code}")]
    public IActionResult SetLanguage(string code, string? returnUrl)
    {
        if (code is not ("fa" or "en")) code = "fa";
        Response.Cookies.Append(SiteCultureProvider.CookieName, code, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true,
            Secure = Request.IsHttps,
        });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [Route("/error/{code:int?}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Error(int? code)
    {
        if (code is null)
        {
            // کد پیگیری روی صفحه خطا نمایش داده می‌شود تا همان خطا در فایل لاگ پیدا شود
            var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            var refId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            log.LogError(feature?.Error, "Unhandled error [ref {Ref}] on {Method} {Path}", refId, Request.Method, feature?.Path ?? Request.Path.Value);
            ViewData["ErrorRef"] = refId;
        }
        Response.StatusCode = code ?? 500;
        return View("Error", code ?? 500);
    }
}

[Route("products")]
public class ProductsController(AppDbContext db, Lang lang) : Controller
{
    private const int PageSize = 12;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? cat, int? mat, string sort = "new", int page = 1)
    {
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        var current = string.IsNullOrEmpty(cat) ? null : categories.FirstOrDefault(c => c.Slug == cat);
        if (current is not null)
        {
            var ids = categories.Where(c => c.Id == current.Id || c.ParentId == current.Id).Select(c => c.Id).ToList();
            query = query.Where(p => ids.Contains(p.CategoryId));
        }
        if (mat.HasValue) query = query.Where(p => p.MaterialId == mat);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = Fmt.EnDigits(q.Trim());
            query = query.Where(p => p.NameFa.Contains(term) || (p.NameEn != null && p.NameEn.Contains(term)) || p.Code.Contains(term)
                || (p.Standard != null && p.Standard.Contains(term)) || p.Variants.Any(v => v.Size.Contains(term) || (v.Sku != null && v.Sku.Contains(term))));
        }
        query = sort switch
        {
            "name" => lang.IsFa ? query.OrderBy(p => p.NameFa) : query.OrderBy(p => p.NameEn ?? p.NameFa),
            "price" => query.OrderBy(p => p.Variants.Where(v => v.SalePrice != null).Min(v => v.SalePrice) ?? decimal.MaxValue),
            "price_desc" => query.OrderByDescending(p => p.Variants.Max(v => v.SalePrice) ?? 0),
            _ => query.OrderBy(p => p.SortOrder).ThenByDescending(p => p.Id),
        };
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var vm = new ProductListVm
        {
            Items = await ProductQueries.ToCardsAsync(query.Skip((page - 1) * PageSize).Take(PageSize), lang),
            Categories = categories,
            Materials = await db.Materials.AsNoTracking().OrderBy(m => m.NameFa).ToListAsync(),
            CategoryCounts = await db.Products.Where(p => p.IsActive).GroupBy(p => p.CategoryId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C),
            Q = q, Cat = cat, Mat = mat, Sort = sort, Page = page, TotalPages = pages, Total = total, CurrentCategory = current,
        };
        return View(vm);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var p = await db.Products
            .Include(x => x.Category).Include(x => x.Material)
            .Include(x => x.Images.OrderByDescending(i => i.IsMain).ThenBy(i => i.SortOrder))
            .Include(x => x.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder))
            .Include(x => x.Documents.Where(d => d.IsActive).OrderByDescending(d => d.IssuedAt ?? d.CreatedAt))
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive);
        if (p is null) return NotFound();

        await db.Products.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1));

        var vm = new ProductDetailVm
        {
            Product = p,
            Certificates = p.Documents.Where(d => d.Type == DocumentType.Certificate).ToList(),
            Datasheets = p.Documents.Where(d => d.Type != DocumentType.Certificate).ToList(),
            Related = await ProductQueries.ToCardsAsync(db.Products.AsNoTracking()
                .Where(x => x.IsActive && x.CategoryId == p.CategoryId && x.Id != p.Id).OrderByDescending(x => x.IsFeatured).Take(4), lang),
        };
        return View(vm);
    }

    [HttpGet("/downloads")]
    public async Task<IActionResult> Downloads(string? q, DocumentType? type)
    {
        var query = db.ProductDocuments.AsNoTracking().Include(d => d.Product)
            .Where(d => d.IsActive && d.Product!.IsActive);
        if (type.HasValue) query = query.Where(d => d.Type == type);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(d => d.TitleFa.Contains(q) || (d.TitleEn != null && d.TitleEn.Contains(q)) || d.Product!.NameFa.Contains(q) || (d.Product.NameEn != null && d.Product.NameEn.Contains(q)));
        ViewBag.Q = q;
        ViewBag.Type = type;
        return View(await query.OrderBy(d => d.Product!.NameFa).ThenBy(d => d.Type).Take(500).ToListAsync());
    }
}

public class CartController(AppDbContext db, Lang lang, InvoiceService invoices, SettingsService settings, EmailService email) : Controller
{
    [HttpGet("/cart")]
    public IActionResult Index() => View(new CheckoutVm());

    /// <summary>اطلاعات اقلام سبد (قیمت همیشه از سرور خوانده می‌شود)</summary>
    [HttpGet("/api/cart")]
    public async Task<IActionResult> Items(string? ids)
    {
        var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).Distinct().Take(100).ToList();
        var rows = await db.ProductVariants.AsNoTracking()
            .Where(v => list.Contains(v.Id) && v.IsActive && v.Product!.IsActive)
            .Select(v => new
            {
                v.Id, v.Size, v.PressureClass, v.Schedule, v.Unit, v.SalePrice, v.DiscountPercent,
                Call = v.CallForPrice || v.Product!.CallForPrice || v.SalePrice == null,
                v.Product!.NameFa, v.Product.NameEn, v.Product.Slug,
                Image = v.Product.Images.OrderByDescending(i => i.IsMain).Select(i => i.FilePath).FirstOrDefault(),
            }).ToListAsync();

        return Json(rows.Select(r => new
        {
            id = r.Id,
            name = lang.Pick(r.NameFa, r.NameEn),
            variant = string.Join(" - ", new[] { r.Size, r.PressureClass, r.Schedule }.Where(s => !string.IsNullOrEmpty(s))),
            unit = r.Unit,
            url = "/products/" + r.Slug,
            image = r.Image ?? "/img/placeholder.svg",
            callForPrice = r.Call,
            price = r.Call ? 0 : r.SalePrice,
            discount = r.Call ? 0 : r.DiscountPercent,
            finalPrice = r.Call ? 0 : Math.Round(r.SalePrice!.Value * (100 - r.DiscountPercent) / 100m, 0),
        }));
    }

    [HttpPost("/cart/checkout"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Checkout(CheckoutVm vm)
    {
        if (!string.IsNullOrEmpty(vm.Website)) return Redirect("/");
        var mobile = Fmt.NormalizeMobile(vm.Mobile);
        if (mobile is null) ModelState.AddModelError(nameof(vm.Mobile), lang["InvalidMobile"]);

        List<CartLine> lines;
        try { lines = JsonSerializer.Deserialize<List<CartLine>>(vm.ItemsJson ?? "[]") ?? []; }
        catch (JsonException) { lines = []; }
        lines = lines.Where(l => l.v > 0 && l.q > 0 && l.q <= 100000).GroupBy(l => l.v)
            .Select(g => new CartLine(g.Key, g.Sum(x => x.q))).Take(100).ToList();

        var ids = lines.Select(l => l.v).ToList();
        var variants = await db.ProductVariants.Include(v => v.Product)
            .Where(v => ids.Contains(v.Id) && v.IsActive && v.Product!.IsActive).ToListAsync();
        if (variants.Count == 0) ModelState.AddModelError("", lang["CartEmpty"]);
        if (!ModelState.IsValid) return View("Index", vm);

        var customer = await invoices.UpsertCustomerAsync(mobile, vm.FullName, vm.CompanyName);
        var order = new Order
        {
            Number = await invoices.NewOrderNumberAsync(),
            Customer = customer, Mobile = mobile!, FullName = vm.FullName?.Trim(), CompanyName = vm.CompanyName?.Trim(),
            Note = vm.Note?.Trim(), Culture = lang.Code, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };
        foreach (var l in lines)
        {
            var v = variants.FirstOrDefault(x => x.Id == l.v);
            if (v is null) continue;
            var call = v.CallForPrice || v.Product!.CallForPrice || v.SalePrice is null;
            order.Items.Add(new OrderItem
            {
                VariantId = v.Id, ProductName = v.Product!.NameFa, VariantTitle = v.Title, Quantity = l.q,
                UnitPrice = call ? null : v.SalePrice, DiscountPercent = call ? 0 : v.DiscountPercent,
            });
            if (!call) order.EstimatedTotal += Math.Round(v.FinalPrice!.Value * l.q, 0);
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var s = await settings.GetAsync();
        var to = s.NotifyEmail ?? s.Email;
        if (!string.IsNullOrWhiteSpace(to))
        {
            var rows = string.Join("", order.Items.Select(i => $"<li>{WebUtility.HtmlEncode(i.ProductName)} ({WebUtility.HtmlEncode(i.VariantTitle)}) × {i.Quantity:0.##}</li>"));
            _ = await email.SendAsync(to, $"سفارش جدید {order.Number} - {order.Mobile}",
                $"<div dir='rtl' style='font-family:Tahoma'><h3>سفارش جدید {order.Number}</h3><p>موبایل: {order.Mobile}<br>نام: {WebUtility.HtmlEncode(order.FullName ?? "-")}</p><ul>{rows}</ul></div>");
        }
        return RedirectToAction(nameof(Success), new { number = order.Number });
    }

    [HttpGet("/cart/success/{number}")]
    public async Task<IActionResult> Success(string number)
    {
        var exists = await db.Orders.AnyAsync(o => o.Number == number && o.CreatedAt > DateTime.UtcNow.AddHours(-2));
        if (!exists) return Redirect("/");
        return View(model: number);
    }
}
