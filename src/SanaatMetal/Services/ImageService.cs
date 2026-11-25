using SkiaSharp;
using SanaatMetal.Models;

namespace SanaatMetal.Services;

/// <summary>نحوه جا دادن عکس در اندازه هدف</summary>
public enum ImageFit
{
    /// <summary>فقط کوچک می‌شود و نسبت ابعاد خودش را نگه می‌دارد (لوگو، پس‌زمینه)</summary>
    Inside,
    /// <summary>کادر را کامل پر می‌کند و اضافه‌ها از وسط بریده می‌شوند</summary>
    Cover,
    /// <summary>کل عکس دیده می‌شود و دور آن حاشیه شفاف اضافه می‌شود</summary>
    Contain,
    /// <summary>کل عکس دیده می‌شود و دور آن حاشیه سفید اضافه می‌شود</summary>
    ContainWhite,
}

public enum ImageOutput { WebP, Png, Jpeg }

/// <summary>اندازه و قالب خروجی یک نوع عکس</summary>
public record ImageProfile(int Width, int Height, ImageFit Fit, ImageOutput Output = ImageOutput.WebP, int Quality = 82)
{
    /// <summary>لوگوی سایت و مشتریان: بدون برش، حداکثر ۱۰۰۰×۳۲۰</summary>
    public static readonly ImageProfile Logo = new(1000, 320, ImageFit.Inside);
    public static readonly ImageProfile ClientLogo = new(480, 240, ImageFit.Inside);
    public static readonly ImageProfile Favicon = new(192, 192, ImageFit.Contain, ImageOutput.Png);
    /// <summary>تصویر اشتراک‌گذاری در شبکه‌های اجتماعی؛ JPEG چون همه پیام‌رسان‌ها پشتیبانی می‌کنند</summary>
    public static readonly ImageProfile OpenGraph = new(1200, 630, ImageFit.Cover, ImageOutput.Jpeg, 85);
    public static readonly ImageProfile Hero = new(1920, 1080, ImageFit.Inside, ImageOutput.WebP, 80);
    public static readonly ImageProfile Background = new(1920, 1920, ImageFit.Inside, ImageOutput.WebP, 78);

    /// <summary>عکس محصول و دسته‌بندی با نسبت ابعاد و حالت انتخاب‌شده در «ظاهر سایت»</summary>
    public static ImageProfile Product(SiteSettings s)
    {
        var (w, h) = s.ProductImageRatio switch
        {
            "1:1" => (1000, 1000),
            "3:4" => (900, 1200),
            "16:9" => (1280, 720),
            _ => (1200, 900),
        };
        var fit = s.ProductImageFit switch { "cover" => ImageFit.Cover, "white" => ImageFit.ContainWhite, _ => ImageFit.Contain };
        return new ImageProfile(w, h, fit, fit == ImageFit.ContainWhite ? ImageOutput.Jpeg : ImageOutput.WebP, 84);
    }

    public string Extension => Output switch { ImageOutput.Png => ".png", ImageOutput.Jpeg => ".jpg", _ => ".webp" };
}

/// <summary>
/// تغییر اندازه، برش و فشرده‌سازی عکس‌های آپلودی در سرور تا همه عکس‌ها اندازه یکسان داشته باشند و چیدمان سایت به هم نریزد.
/// با SkiaSharp (MIT) کار می‌کند؛ چرخش EXIF عکس‌های موبایل هم اصلاح می‌شود.
/// </summary>
public static class ImageService
{
    /// <summary>عکس‌های بزرگ‌تر از این (پیکسل) پردازش نمی‌شوند تا حافظه سرور پر نشود</summary>
    private const long MaxPixels = 50_000_000;

    public static (byte[]? Data, string? Error) Process(byte[] input, ImageProfile profile)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null) return (null, "فایل عکس قابل خواندن نیست یا خراب است.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) return (null, "ابعاد عکس خیلی بزرگ است؛ عکس کوچک‌تری انتخاب کنید.");

        using var decoded = SKBitmap.Decode(codec, codec.Info.WithColorType(SKImageInfo.PlatformColorType).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null) return (null, "فایل عکس قابل خواندن نیست یا خراب است.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var src = oriented ?? decoded;

        int w = src.Width, h = src.Height;
        int canvasW, canvasH;
        SKRect dest;
        SKRect from = new(0, 0, w, h);
        switch (profile.Fit)
        {
            case ImageFit.Cover:
            {
                var s = Math.Max(profile.Width / (double)w, profile.Height / (double)h);
                // عکس کوچک بزرگ نمی‌شود؛ فقط با همان نسبت هدف بریده می‌شود
                double tw = profile.Width, th = profile.Height;
                if (s > 1) { tw /= s; th /= s; s = 1; }
                canvasW = Math.Max(1, (int)Math.Round(tw)); canvasH = Math.Max(1, (int)Math.Round(th));
                var cw = canvasW / s; var ch = canvasH / s;
                from = SKRect.Create((float)((w - cw) / 2), (float)((h - ch) / 2), (float)cw, (float)ch);
                dest = new SKRect(0, 0, canvasW, canvasH);
                break;
            }
            case ImageFit.Contain or ImageFit.ContainWhite:
            {
                var s = Math.Min(profile.Width / (double)w, profile.Height / (double)h);
                double cw = profile.Width, ch = profile.Height;
                if (s > 1) { cw /= s; ch /= s; s = 1; }
                canvasW = Math.Max(1, (int)Math.Round(cw)); canvasH = Math.Max(1, (int)Math.Round(ch));
                var dw = (float)(w * s); var dh = (float)(h * s);
                dest = SKRect.Create((canvasW - dw) / 2, (canvasH - dh) / 2, dw, dh);
                break;
            }
            default:
            {
                var s = Math.Min(1, Math.Min(profile.Width / (double)w, profile.Height / (double)h));
                canvasW = Math.Max(1, (int)Math.Round(w * s)); canvasH = Math.Max(1, (int)Math.Round(h * s));
                dest = new SKRect(0, 0, canvasW, canvasH);
                break;
            }
        }

        var info = new SKImageInfo(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null) return (null, "پردازش عکس ممکن نشد.");
        var canvas = surface.Canvas;
        var opaque = profile.Fit == ImageFit.ContainWhite || profile.Output == ImageOutput.Jpeg;
        canvas.Clear(opaque ? SKColors.White : SKColors.Transparent);
        using (var img = SKImage.FromBitmap(src))
        using (var paint = new SKPaint { IsAntialias = true })
            canvas.DrawImage(img, from, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        var format = profile.Output switch { ImageOutput.Png => SKEncodedImageFormat.Png, ImageOutput.Jpeg => SKEncodedImageFormat.Jpeg, _ => SKEncodedImageFormat.Webp };
        using var encoded = snapshot.Encode(format, profile.Quality);
        return encoded is null ? (null, "ذخیره عکس پردازش‌شده ممکن نشد.") : (encoded.ToArray(), null);
    }

    /// <summary>تبدیل هر عکس (مثلاً WebP) به PNG؛ برای لوگوی فاکتورهای PDF</summary>
    public static byte[]? ToPng(byte[] input, int maxSide = 512) =>
        Process(input, new ImageProfile(maxSide, maxSide, ImageFit.Inside, ImageOutput.Png)).Data;

    /// <summary>چرخاندن عکس طبق EXIF (عکس‌های گوشی معمولاً چرخیده ذخیره می‌شوند)</summary>
    private static SKBitmap? Orient(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return null;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height, bmp.ColorType, bmp.AlphaType);
        using var c = new SKCanvas(result);
        float w = bmp.Width, h = bmp.Height;
        // x' = ScaleX·x + SkewX·y + TransX ، y' = SkewY·x + ScaleY·y + TransY
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1), // LeftBottom
        };
        c.SetMatrix(m);
        c.DrawBitmap(bmp, 0, 0);
        return result;
    }
}
