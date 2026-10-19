using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using SanaatMetal.Data;

namespace SanaatMetal.Infrastructure;

public static class StoredUploads
{
    /// <summary>
    /// فایل‌هایی که روی دیسک نیستند (چون هاست اجازه نوشتن نداده) از دیتابیس با همان آدرس /uploads/... فرستاده می‌شوند.
    /// فایل‌های روی دیسک قبل از این مسیر توسط UseStaticFiles فرستاده می‌شوند.
    /// </summary>
    public static void MapStoredUploads(this WebApplication app)
    {
        app.MapGet("/uploads/{**rest}", async (string rest, HttpContext ctx, AppDbContext db) =>
        {
            var path = "/uploads/" + rest;
            var f = await db.StoredFiles.AsNoTracking().Where(x => x.Path == path)
                .Select(x => new { x.Id, x.ContentType, x.CreatedAt }).FirstOrDefaultAsync();
            if (f is null) return Results.NotFound();
            var etag = new EntityTagHeaderValue($"\"sf{f.Id}\"");
            // نام فایل‌ها تصادفی و تغییرناپذیر است، پس مرورگر می‌تواند طولانی نگه دارد
            ctx.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag.Tag.Value) return Results.StatusCode(StatusCodes.Status304NotModified);
            var data = await db.StoredFiles.AsNoTracking().Where(x => x.Id == f.Id).Select(x => x.Data).FirstAsync();
            return Results.File(data, f.ContentType, lastModified: f.CreatedAt, entityTag: etag);
        });
    }
}
