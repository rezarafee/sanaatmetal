using Microsoft.AspNetCore.Mvc.Razor;
using SanaatMetal.Services;

namespace SanaatMetal.Areas.Admin.Models;

/// <summary>کلاس پایه ویوهای پنل ادمین با توابع کمکی</summary>
public abstract class AdminPage<TModel> : RazorPage<TModel>
{
    public HashSet<string> Perms => ViewBag.Perms as HashSet<string> ?? [];
    public bool Can(string permission) => Perms.Contains(permission);
    public static string N(decimal? v) => Fmt.Number(v);
    public static string D(DateTime? d, bool withTime = false) => Fmt.Jalali(d, withTime);
}
