using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SanaatMetal.Data;
using SanaatMetal.Services;

namespace SanaatMetal.Controllers;

/// <summary>robots.txt و sitemap.xml برای موتورهای جستجو (ساخته‌شده از دیتابیس، بدون نیاز به فایل روی هاست)</summary>
public class SeoController(AppDbContext db, IMemoryCache cache) : Controller
{
    [HttpGet("/robots.txt")]
    public IActionResult Robots()
    {
        var b = Seo.BaseUrl(HttpContext);
        var txt = $"""
            User-agent: *
            Disallow: /admin
            Disallow: /cart
            Disallow: /api/
            Disallow: /diag
            Disallow: /error
            Disallow: /lang/
            Disallow: /*?*sort=
            Allow: /

            Sitemap: {b}/sitemap.xml
            """;
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(txt.Replace("\r\n", "\n") + "\n", "text/plain; charset=utf-8");
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var b = Seo.BaseUrl(HttpContext);
        var xml = await cache.GetOrCreateAsync("sitemap:" + b, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await BuildAsync(b);
        });
        Response.Headers.CacheControl = "public, max-age=1800";
        return Content(xml!, "application/xml; charset=utf-8");
    }

    private async Task<string> BuildAsync(string b)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Slug, Mod = p.UpdatedAt ?? p.CreatedAt, Images = p.Images.OrderByDescending(i => i.IsMain).ThenBy(i => i.SortOrder).Select(i => i.FilePath).Take(5).ToList() })
            .ToListAsync();
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Slug, Mod = c.UpdatedAt ?? c.CreatedAt }).ToListAsync();
        var lastProduct = products.Count > 0 ? products.Max(p => p.Mod) : (DateTime?)null;

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            const string sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
            const string xhtml = "http://www.w3.org/1999/xhtml";
            const string img = "http://www.google.com/schemas/sitemap-image/1.1";
            w.WriteStartDocument();
            w.WriteStartElement("urlset", sm);
            w.WriteAttributeString("xmlns", "xhtml", null, xhtml);
            w.WriteAttributeString("xmlns", "image", null, img);

            void Url(string rel, DateTime? mod, string freq, string priority, IEnumerable<string>? images = null)
            {
                var fa = b + rel;
                // هر نسخه زبانی یک آدرس جدا با لینک‌های hreflang متقابل
                foreach (var loc in new[] { fa, Seo.WithLang(fa, "en") })
                {
                    w.WriteStartElement("url", sm);
                    w.WriteElementString("loc", sm, loc);
                    if (mod.HasValue) w.WriteElementString("lastmod", sm, mod.Value.ToString("yyyy-MM-dd"));
                    w.WriteElementString("changefreq", sm, freq);
                    w.WriteElementString("priority", sm, loc == fa ? priority : "0.5");
                    foreach (var (lang, href) in new[] { ("fa", fa), ("en", Seo.WithLang(fa, "en")), ("x-default", fa) })
                    {
                        w.WriteStartElement("xhtml", "link", xhtml);
                        w.WriteAttributeString("rel", "alternate");
                        w.WriteAttributeString("hreflang", lang);
                        w.WriteAttributeString("href", href);
                        w.WriteEndElement();
                    }
                    foreach (var i in images ?? [])
                    {
                        w.WriteStartElement("image", "image", img);
                        w.WriteElementString("image", "loc", img, i.StartsWith("http") ? i : b + i);
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
            }

            Url("/", lastProduct, "daily", "1.0");
            Url("/products", lastProduct, "daily", "0.9");
            foreach (var c in categories) Url("/products?cat=" + Uri.EscapeDataString(c.Slug), c.Mod, "weekly", "0.8");
            foreach (var p in products) Url("/products/" + Uri.EscapeDataString(p.Slug), p.Mod, "weekly", "0.8", p.Images);
            Url("/downloads", null, "weekly", "0.6");
            Url("/about", null, "monthly", "0.5");
            Url("/contact", null, "monthly", "0.5");
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        // StringBuilder همیشه utf-16 در اعلان XML می‌نویسد؛ خروجی واقعی utf-8 است
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }
}
