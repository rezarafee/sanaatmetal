using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SanaatMetal.Services;

/// <summary>
/// تنظیمات اتصال به پنل Plesk (فقط در appsettings.Production.json روی هاست، نه در دیتابیس):
/// "Plesk": { "Url": "https://localhost:8443", "ApiKey": "" یا "Login"/"Password", "Domain": "sanaatmetal.ir", "AllowInvalidCertificate": true }
/// </summary>
public class PleskOptions
{
    public string? Url { get; set; }
    public string? ApiKey { get; set; }
    public string? Login { get; set; }
    public string? Password { get; set; }
    public string? Domain { get; set; }
    public bool AllowInvalidCertificate { get; set; }

    public bool Configured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Domain)
        && (!string.IsNullOrWhiteSpace(ApiKey) || (!string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password)));

    public string DomainName => (Domain ?? "").Trim().ToLowerInvariant();
}

public record PleskResult(bool Ok, string? Error = null);

/// <summary>
/// ساخت صندوق ایمیل و تغییر رمز آن در Plesk از طریق XML API پنل (/enterprise/control/agent.php).
/// با حساب مشتری (Customer) پنل هم کار می‌کند، به شرط این‌که هاستینگ دسترسی API را برای حساب باز گذاشته باشد.
/// </summary>
public class PleskMailService(IHttpClientFactory httpFactory, Microsoft.Extensions.Options.IOptions<PleskOptions> options, ILogger<PleskMailService> log)
{
    public const string HttpClientName = "plesk";
    public PleskOptions Options => options.Value;
    public bool Configured => Options.Configured;

    /// <summary>بخش قبل از @ ایمیل: فقط حروف و اعداد انگلیسی، نقطه، خط تیره و زیرخط</summary>
    public static bool ValidLocalPart(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 64 &&
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9]([a-z0-9._-]*[a-z0-9])?$") && !name.Contains("..");

    /// <summary>اگر ایمیل روی دامنه Plesk باشد، بخش قبل از @ را برمی‌گرداند</summary>
    public string? LocalPartOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !Configured) return null;
        var at = email.LastIndexOf('@');
        if (at <= 0) return null;
        return email[(at + 1)..].Trim().Equals(Options.DomainName, StringComparison.OrdinalIgnoreCase) ? email[..at].Trim().ToLowerInvariant() : null;
    }

    /// <summary>رمز تصادفی قوی (حرف بزرگ، کوچک، عدد و نماد) که شرایط Plesk را هم دارد</summary>
    public static string GeneratePassword(int length = 14)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "@#%*+=!?";
        var all = upper + lower + digits + symbols;
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
        while (chars.Count < length) chars.Add(Pick(all));
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
        static char Pick(string s) => s[RandomNumberGenerator.GetInt32(s.Length)];
    }

    public async Task<PleskResult> TestAsync()
    {
        var (id, err) = await SiteIdAsync();
        return id is null ? new(false, err) : new(true);
    }

    /// <summary>آیا صندوق ایمیل name@domain در Plesk وجود دارد؟ (null یعنی خطا)</summary>
    public async Task<(bool? Exists, string? Error)> ExistsAsync(string name)
    {
        var (siteId, err) = await SiteIdAsync();
        if (siteId is null) return (null, err);
        var (res, e2) = await CallAsync(new XElement("mail", new XElement("get_info",
            new XElement("filter", new XElement("site-id", siteId), new XElement("name", name)), new XElement("mailbox"))));
        if (res is null) return (null, e2);
        var r = res.Descendants("result").FirstOrDefault();
        if (r is null) return (false, null);
        if (Status(r) == "ok") return (r.Descendants("mailname").Any(), null);
        // errcode 1013 = چنین ایمیلی وجود ندارد
        return ErrCode(r) == "1013" ? (false, null) : (null, ErrText(r));
    }

    public async Task<PleskResult> CreateMailboxAsync(string name, string password)
    {
        if (!ValidLocalPart(name)) return new(false, "نام ایمیل نامعتبر است.");
        var (siteId, err) = await SiteIdAsync();
        if (siteId is null) return new(false, err);
        var (res, e2) = await CallAsync(new XElement("mail", new XElement("create", new XElement("filter",
            new XElement("site-id", siteId),
            new XElement("mailname",
                new XElement("name", name),
                new XElement("mailbox", new XElement("enabled", "true")),
                new XElement("password", new XElement("value", password), new XElement("type", "plain")))))));
        return ResultOf(res, e2);
    }

    public async Task<PleskResult> SetPasswordAsync(string name, string password)
    {
        if (!ValidLocalPart(name)) return new(false, "نام ایمیل نامعتبر است.");
        var (siteId, err) = await SiteIdAsync();
        if (siteId is null) return new(false, err);
        var (res, e2) = await CallAsync(new XElement("mail", new XElement("update", new XElement("set", new XElement("filter",
            new XElement("site-id", siteId),
            new XElement("mailname",
                new XElement("name", name),
                new XElement("password", new XElement("value", password), new XElement("type", "plain"))))))));
        return ResultOf(res, e2);
    }

    // ---------- داخلی ----------

    private string? _siteId;

    private async Task<(string? Id, string? Error)> SiteIdAsync()
    {
        if (_siteId is not null) return (_siteId, null);
        if (!Configured) return (null, "اتصال به Plesk تنظیم نشده است (بخش Plesk در appsettings.Production.json).");
        // دامنه اصلی اشتراک در Plesk «webspace» است و دامنه‌های اضافه «site»؛ هر دو امتحان می‌شوند
        string? lastErr = null;
        foreach (var op in new[] { "site", "webspace" })
        {
            var (res, err) = await CallAsync(new XElement(op, new XElement("get",
                new XElement("filter", new XElement("name", Options.DomainName)), new XElement("dataset", new XElement("gen_info")))));
            if (res is null) return (null, err);
            var r = res.Descendants("result").FirstOrDefault();
            if (r is not null && Status(r) == "ok" && r.Element("id")?.Value is { Length: > 0 } id) return (_siteId = id, null);
            lastErr = r is null ? "پاسخ نامعتبر از Plesk" : ErrText(r);
        }
        return (null, $"دامنه {Options.DomainName} در Plesk پیدا نشد یا دسترسی ندارید: {lastErr}");
    }

    private async Task<(XDocument? Doc, string? Error)> CallAsync(XElement operation)
    {
        var o = Options;
        try
        {
            var url = o.Url!.TrimEnd('/') + "/enterprise/control/agent.php";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            var body = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("packet", operation));
            req.Content = new StringContent(body.Declaration + body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "UTF-8" };
            if (!string.IsNullOrWhiteSpace(o.ApiKey)) req.Headers.TryAddWithoutValidation("KEY", o.ApiKey.Trim());
            else
            {
                req.Headers.TryAddWithoutValidation("HTTP_AUTH_LOGIN", o.Login!.Trim());
                req.Headers.TryAddWithoutValidation("HTTP_AUTH_PASSWD", o.Password);
            }
            req.Headers.TryAddWithoutValidation("HTTP_PRETTY_PRINT", "TRUE");

            using var resp = await httpFactory.CreateClient(HttpClientName).SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (null, $"Plesk HTTP {(int)resp.StatusCode}");
            var doc = XDocument.Parse(text);
            // خطای سطح بسته (مثلاً نام کاربری/رمز پنل اشتباه یا API بسته است)
            var system = doc.Root?.Element("system");
            if (system is not null && Status(system) != "ok") return (null, "Plesk: " + ErrText(system));
            return (doc, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or UriFormatException)
        {
            log.LogWarning(ex, "Plesk API call failed");
            return (null, "اتصال به Plesk ممکن نشد: " + ex.Message);
        }
    }

    private static PleskResult ResultOf(XDocument? res, string? err)
    {
        if (res is null) return new(false, err);
        var r = res.Descendants("result").FirstOrDefault();
        if (r is null) return new(false, "پاسخ نامعتبر از Plesk");
        return Status(r) == "ok" ? new(true) : new(false, ErrText(r));
    }

    private static string? Status(XElement e) => e.Element("status")?.Value?.Trim();
    private static string? ErrCode(XElement e) => e.Element("errcode")?.Value?.Trim();
    private static string ErrText(XElement e) => $"{e.Element("errtext")?.Value?.Trim()} (کد {ErrCode(e)})";
}
