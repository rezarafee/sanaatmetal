using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace SanaatMetal.Services;

/// <summary>آدرس اصلی سایت، لینک canonical/hreflang و داده ساختاریافته (JSON-LD) برای موتورهای جستجو</summary>
public static class Seo
{
    /// <summary>
    /// آدرس اصلی سایت. اگر Site:BaseUrl در تنظیمات (appsettings) پر شده باشد همان استفاده می‌شود
    /// (برای لینک‌های ایمیل بازیابی رمز امن‌تر است)، وگرنه از آدرس درخواست فعلی ساخته می‌شود.
    /// </summary>
    public static string BaseUrl(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetService<IConfiguration>()?["Site:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
            return u.GetLeftPart(UriPartial.Authority);
        return $"{ctx.Request.Scheme}://{ctx.Request.Host.ToUriComponent()}";
    }

    public static string Abs(HttpContext ctx, string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return BaseUrl(ctx) + "/";
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return pathOrUrl;
        return BaseUrl(ctx) + (pathOrUrl.StartsWith('/') ? "" : "/") + pathOrUrl;
    }

    /// <summary>نسخه انگلیسی هر صفحه با ?lang=en و نسخه فارسی بدون پارامتر (زبان پیش‌فرض)</summary>
    public static string WithLang(string url, string lang) =>
        lang == "en" ? url + (url.Contains('?') ? "&" : "?") + "lang=en" : url;

    /// <summary>متن کوتاه برای meta description (حداکثر ~۱۶۰ کاراکتر، بدون خط جدید)</summary>
    public static string? Describe(string? text, int max = 160)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t.Length <= max) return t;
        var cut = t.LastIndexOf(' ', max - 1);
        return t[..(cut > max / 2 ? cut : max - 1)] + "…";
    }

    private static readonly JsonSerializerOptions LdOptions = new()
    {
        // حروف فارسی خوانا می‌مانند ولی < > & ' همچنان escape می‌شوند تا داخل تگ script امن باشد
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>JSON امن برای قرار دادن داخل &lt;script type="application/ld+json"&gt;</summary>
    public static string Json(object data)
    {
        var node = JsonSerializer.SerializeToNode(data, LdOptions);
        StripNulls(node);
        return node!.ToJsonString(LdOptions);
    }

    // فیلدهای خالی (مثلاً برند ثبت‌نشده) از داده ساختاریافته حذف می‌شوند
    private static void StripNulls(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonObject o)
        {
            foreach (var k in o.Where(p => p.Value is null).Select(p => p.Key).ToList()) o.Remove(k);
            foreach (var p in o) StripNulls(p.Value);
        }
        else if (node is System.Text.Json.Nodes.JsonArray a)
            foreach (var i in a) StripNulls(i);
    }
}

public static partial class Fmt
{
    /// <summary>
    /// لینک‌هایی که در پنل وارد می‌شوند (شبکه‌های اجتماعی، سایت مشتریان و ...) فقط اگر http/https/mailto/tel یا آدرس داخلی باشند
    /// نمایش داده می‌شوند؛ آدرس‌هایی مثل javascript: به # تبدیل می‌شوند (جلوگیری از XSS).
    /// </summary>
    public static string SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "#";
        var u = url.Trim();
        if (u.StartsWith('/') && !u.StartsWith("//") && !u.StartsWith("/\\")) return u;
        if (Uri.TryCreate(u, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeMailto || abs.Scheme == "tel"))
            return abs.AbsoluteUri;
        // آدرس بدون http (مثل instagram.com/xxx)
        if (!u.Contains(':') && Uri.TryCreate("https://" + u, UriKind.Absolute, out var guess) && guess.Host.Contains('.'))
            return guess.AbsoluteUri;
        return "#";
    }
}
