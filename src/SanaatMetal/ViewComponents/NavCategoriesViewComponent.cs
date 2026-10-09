using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SanaatMetal.Data;
using SanaatMetal.Models;

namespace SanaatMetal.ViewComponents;

public class NavCategoriesViewComponent(AppDbContext db, IMemoryCache cache) : ViewComponent
{
    public const string CacheKey = "nav-categories";

    public async Task<IViewComponentResult> InvokeAsync(bool plain = false)
    {
        var cats = await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await db.Categories.AsNoTracking().Where(c => c.IsActive && c.ParentId == null)
                .OrderBy(c => c.SortOrder).Take(12).ToListAsync();
        });
        ViewData["Plain"] = plain;
        return View(cats ?? new List<Category>());
    }
}
