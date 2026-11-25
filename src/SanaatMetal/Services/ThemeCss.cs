using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

/// <summary>
/// ساخت فایل /theme.css از تنظیمات «ظاهر سایت»: رنگ‌ها، گردی گوشه‌ها، فونت تیتر، پس‌زمینه و اندازه عکس‌ها
/// به صورت متغیرهای CSS که site.css از آن‌ها استفاده می‌کند. سایه‌روشن‌ها و رنگ متن خوانا خودکار محاسبه می‌شوند.
/// </summary>
public static partial class ThemeCss
{
    public static readonly string[] Ratios = ["1:1", "4:3", "3:4", "16:9"];
    public static readonly string[] Fits = ["contain", "white", "cover"];
    public static readonly string[] Reveals = ["fade-up", "zoom", "flip", "blur", "slide", "none"];
    public static readonly string[] Hovers = ["zoom", "tilt", "shine", "lift", "none"];
    public static readonly string[] Modes = ["auto", "light", "dark"];
    public static readonly string[] TitleFonts = ["Lalezar", "Vazirmatn"];
    public static readonly string[] LogoShapes = ["circle", "rounded", "wide"];
    public static readonly string[] HeroDecors = ["blueprint", "grid", "none"];
    public static readonly string[] BackgroundModes = ["cover", "repeat"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex HexRx();

    /// <summary>رنگ معتبر #rrggbb یا null</summary>
    public static string? Hex(string? v)
    {
        v = v?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (!v.StartsWith('#')) v = "#" + v;
        if (v.Length == 4) v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        return HexRx().IsMatch(v) ? v.ToLowerInvariant() : null;
    }

    /// <summary>مقدار مجاز یا پیش‌فرض</summary>
    public static string Pick(string? v, string[] allowed) => v is not null && allowed.Contains(v) ? v : allowed[0];

    /// <summary>پاک‌سازی ورودی‌های ظاهر قبل از ذخیره</summary>
    public static void Normalize(SiteSettings s)
    {
        s.ThemePrimary = Hex(s.ThemePrimary) ?? "#22318a";
        s.ThemeAccent = Hex(s.ThemeAccent) ?? "#f5b301";
        s.ThemeBackground = Hex(s.ThemeBackground) ?? "#f3f4f8";
        s.ThemeNavBg = Hex(s.ThemeNavBg);
        s.ThemeNavText = Hex(s.ThemeNavText);
        s.ThemeTopbarBg = Hex(s.ThemeTopbarBg);
        s.ThemeFooterBg = Hex(s.ThemeFooterBg);
        s.ThemeHeroBg = Hex(s.ThemeHeroBg);
        s.ThemeRadius = Math.Clamp(s.ThemeRadius, 0, 32);
        s.ThemeMode = Pick(s.ThemeMode, Modes);
        s.ThemeTitleFont = Pick(s.ThemeTitleFont, TitleFonts);
        s.LogoHeight = Math.Clamp(s.LogoHeight == 0 ? 44 : s.LogoHeight, 28, 96);
        s.LogoShape = Pick(s.LogoShape, LogoShapes);
        s.HeroDecor = Pick(s.HeroDecor, HeroDecors);
        s.BackgroundMode = Pick(s.BackgroundMode, BackgroundModes);
        s.BackgroundVeil = Math.Clamp(s.BackgroundVeil, 0, 95);
        s.ProductImageRatio = s.ProductImageRatio is not null && Ratios.Contains(s.ProductImageRatio) ? s.ProductImageRatio : "4:3";
        s.ProductImageFit = Pick(s.ProductImageFit, Fits);
        s.RevealAnimation = Pick(s.RevealAnimation, Reveals);
        s.HoverEffect = Pick(s.HoverEffect, Hovers);
        s.YearsOfExperience = Math.Clamp(s.YearsOfExperience, 0, 200);
    }

    /// <summary>نسخه کوتاه برای ?v= تا با هر تغییر، مرورگرها فایل جدید را بگیرند</summary>
    public static string Version(SiteSettings s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Build(s))))[..10].ToLowerInvariant();

    public static string Build(SiteSettings s)
    {
        var primary = Rgb.Parse(Hex(s.ThemePrimary) ?? "#22318a");
        var accent = Rgb.Parse(Hex(s.ThemeAccent) ?? "#f5b301");
        var bg = Rgb.Parse(Hex(s.ThemeBackground) ?? "#f3f4f8");
        var white = new Rgb(255, 255, 255);
        var black = new Rgb(0, 0, 0);
        var navy = primary.Mix(black, .45);
        var hero = Hex(s.ThemeHeroBg) is { } hb ? Rgb.Parse(hb) : primary.Mix(black, .55);
        var footer = Hex(s.ThemeFooterBg) is { } fb ? Rgb.Parse(fb) : primary.Mix(black, .7);
        var topbar = Hex(s.ThemeTopbarBg) is { } tb ? Rgb.Parse(tb) : navy;

        // رنگ تیره‌شده تأکیدی برای متن روی زمینه روشن (حداقل کنتراست ۴.۵)
        var accentInk = accent;
        for (var i = 1; i <= 20 && accentInk.Contrast(white) < 4.5; i++) accentInk = accent.Mix(black, i * .05);
        var onAccent = accent.Contrast(white) >= 3 ? white : primary.Mix(black, .35).Contrast(accent) >= 4.5 ? primary.Mix(black, .35) : new Rgb(17, 17, 17);
        var onPrimary = primary.Contrast(white) >= 3 ? white : new Rgb(17, 17, 17);
        var darkPrimary = primary.WithLightness(Math.Max(primary.Lightness, .74));
        var titleFont = s.ThemeTitleFont == "Vazirmatn" ? "\"Vazirmatn\", Tahoma, sans-serif" : "\"Titr\", \"Lalezar\", \"Vazirmatn\", sans-serif";
        var (rw, rh) = s.ProductImageRatio switch { "1:1" => (1, 1), "3:4" => (3, 4), "16:9" => (16, 9), _ => (4, 3) };
        var fit = s.ProductImageFit;

        var sb = new StringBuilder();
        sb.Append("/* generated from admin > appearance */\n:root{");
        Var(sb, "--sm-primary", primary.ToString());
        Var(sb, "--sm-primary-2", primary.WithLightness(Math.Min(.9, primary.Lightness + .07)).ToString());
        Var(sb, "--sm-on-primary", onPrimary.ToString());
        Var(sb, "--sm-accent", accent.ToString());
        Var(sb, "--sm-accent-2", accent.Mix(white, .3).ToString());
        Var(sb, "--sm-accent-ink", accentInk.ToString());
        Var(sb, "--sm-on-accent", onAccent.ToString());
        Var(sb, "--sm-navy", navy.ToString());
        Var(sb, "--sm-bg", bg.ToString());
        Var(sb, "--sm-grid", primary.Mix(white, .6).Rgba(.09));
        Var(sb, "--sm-grid-2", primary.Mix(white, .6).Rgba(.045));
        Var(sb, "--sm-hero-base", hero.ToString());
        Var(sb, "--sm-hero-overlay", $"linear-gradient(var(--sm-hero-dir, 100deg), {hero.Rgba(.94)} 0%, {hero.Rgba(.78)} 45%, {hero.Rgba(.25)} 100%)");
        Var(sb, "--sm-topbar-bg", topbar.ToString());
        Var(sb, "--sm-topbar-text", topbar.Contrast(white) >= 3 ? "rgba(255,255,255,.8)" : "rgba(0,0,0,.7)");
        Var(sb, "--sm-topbar-hover", topbar.Contrast(white) >= 3 ? "#fff" : "#000");
        Var(sb, "--sm-footer-bg", footer.ToString());
        Var(sb, "--sm-footer-text", footer.Contrast(white) >= 3 ? "rgba(255,255,255,.68)" : "rgba(0,0,0,.68)");
        Var(sb, "--sm-footer-head", footer.Contrast(white) >= 3 ? "#fff" : "#111");
        Var(sb, "--sm-radius", s.ThemeRadius.ToString(CultureInfo.InvariantCulture) + "px");
        Var(sb, "--sm-btn-radius", Math.Round(s.ThemeRadius * .56).ToString(CultureInfo.InvariantCulture) + "px");
        Var(sb, "--sm-title", titleFont);
        Var(sb, "--sm-title-weight", s.ThemeTitleFont == "Vazirmatn" ? "900" : "400");
        Var(sb, "--sm-logo-h", s.LogoHeight.ToString(CultureInfo.InvariantCulture) + "px");
        Var(sb, "--sm-img-ratio", $"{rw} / {rh}");
        Var(sb, "--sm-img-fit", fit == "cover" ? "cover" : "contain");
        Var(sb, "--sm-img-size", fit == "contain" ? "84%" : "100%");
        Var(sb, "--sm-img-bg", fit == "white" ? "#fff" : "radial-gradient(circle at 50% 40%, var(--sm-surface) 0%, var(--sm-surface-2) 100%)");
        if (Hex(s.ThemeNavBg) is { } nb)
        {
            var nav = Rgb.Parse(nb);
            var navText = Hex(s.ThemeNavText) is { } nt ? Rgb.Parse(nt) : nav.Contrast(white) >= 3 ? white : new Rgb(22, 29, 51);
            Var(sb, "--sm-nav-bg", nav.Rgba(.92));
            Var(sb, "--sm-nav-solid", nav.ToString());
            Var(sb, "--sm-nav-text", navText.ToString());
            Var(sb, "--sm-nav-active", nav.Contrast(accent) >= 2.2 ? accent.ToString() : navText.ToString());
        }
        else if (Hex(s.ThemeNavText) is { } nt2)
        {
            Var(sb, "--sm-nav-text", nt2);
        }
        sb.Append("}\n");

        // تم تیره: رنگ اصلی روشن‌تر تا روی زمینه تیره خوانا باشد
        sb.Append("[data-bs-theme=\"dark\"]{");
        Var(sb, "--sm-primary", darkPrimary.ToString());
        Var(sb, "--sm-primary-2", primary.WithLightness(Math.Max(primary.Lightness, .66)).ToString());
        Var(sb, "--sm-accent-ink", accent.ToString());
        Var(sb, "--sm-bg", "#0b0f22");
        var btnDark = primary.WithLightness(Math.Clamp(primary.Lightness, .42, .5));
        Var(sb, "--sm-btn-primary", btnDark.ToString());
        Var(sb, "--sm-btn-primary-hover", btnDark.WithLightness(btnDark.Lightness + .06).ToString());
        Var(sb, "--sm-on-primary", btnDark.Contrast(white) >= 3 ? "#ffffff" : "#111111");
        sb.Append("}\n");

        if (!string.IsNullOrEmpty(s.BackgroundImagePath) && s.BackgroundImagePath.StartsWith("/uploads/"))
        {
            var url = s.BackgroundImagePath.Replace("\"", "").Replace(")", "");
            var veil = s.BackgroundVeil / 100.0;
            var size = s.BackgroundMode == "repeat" ? "auto, auto" : "cover, cover";
            var repeat = s.BackgroundMode == "repeat" ? "repeat" : "no-repeat";
            sb.Append($"body{{background-image:linear-gradient({bg.Rgba(veil)},{bg.Rgba(veil)}),url(\"{url}\");background-size:{size};background-repeat:{repeat};background-attachment:fixed;background-position:center}}\n");
            sb.Append($"[data-bs-theme=\"dark\"] body{{background-image:linear-gradient(rgba(11,15,34,{Math.Max(veil, .8).ToString("0.##", CultureInfo.InvariantCulture)}),rgba(11,15,34,{Math.Max(veil, .8).ToString("0.##", CultureInfo.InvariantCulture)})),url(\"{url}\")}}\n");
            sb.Append("@media (max-width:991.98px){body{background-attachment:scroll}}\n");
        }
        return sb.ToString();
    }

    private static void Var(StringBuilder sb, string name, string value) => sb.Append(name).Append(':').Append(value).Append(';');

    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb Parse(string hex) => new(
            int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

        public Rgb Mix(Rgb o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);

        private static double Lin(double c) { c /= 255; return c <= .03928 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        public double Luminance => .2126 * Lin(R) + .7152 * Lin(G) + .0722 * Lin(B);
        public double Contrast(Rgb o) { var (a, b) = (Luminance, o.Luminance); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05); }

        public double Lightness => (Math.Max(R, Math.Max(G, B)) + Math.Min(R, Math.Min(G, B))) / 510;

        /// <summary>همان رنگ (Hue و Saturation) با روشنایی دیگر</summary>
        public Rgb WithLightness(double l)
        {
            double r = R / 255, g = G / 255, b = B / 255;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, h = 0, sat = 0, l0 = (max + min) / 2;
            if (d > 0)
            {
                sat = d / (1 - Math.Abs(2 * l0 - 1));
                h = max == r ? ((g - b) / d % 6 + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            }
            l = Math.Clamp(l, 0, 1);
            var c = (1 - Math.Abs(2 * l - 1)) * Math.Min(1, sat);
            var x = c * (1 - Math.Abs(h % 2 - 1));
            var m = l - c / 2;
            var (r1, g1, b1) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
            return new Rgb((r1 + m) * 255, (g1 + m) * 255, (b1 + m) * 255);
        }

        private static int C(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));
        public override string ToString() => $"#{C(R):x2}{C(G):x2}{C(B):x2}";
        public string Rgba(double a) => $"rgba({C(R)},{C(G)},{C(B)},{a.ToString("0.###", CultureInfo.InvariantCulture)})";
    }
}
