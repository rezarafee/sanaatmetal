using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SanaatMetal.Infrastructure;

/// <summary>
/// خطاهای پنل مدیریت داخل خود پنل نمایش داده می‌شوند و کاربر به صفحه خطای سایت عمومی نمی‌رود.
/// فرم‌ها (POST) به همان صفحه قبل برمی‌گردند با پیام خطا و کد پیگیری؛ خطا در فایل لاگ هم ثبت می‌شود.
/// </summary>
public sealed class AdminErrorFilter(ILogger<AdminErrorFilter> log, ITempDataDictionaryFactory tempData) : IAsyncExceptionFilter, IAlwaysRunResultFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    {
        var http = context.HttpContext;
        var refId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        log.LogError(context.Exception, "Admin error [ref {Ref}] on {Method} {Path}", refId, http.Request.Method, http.Request.Path + http.Request.QueryString);
        var msg = $"{Describe(context.Exception)} (کد پیگیری: {refId})";

        if (HttpMethods.IsPost(http.Request.Method))
        {
            SetFlash(http, msg);
            context.Result = new RedirectResult(BackUrl(http));
        }
        else
        {
            context.Result = AdminErrorPage(context, msg);
        }
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }

    /// <summary>اگر توکن فرم نامعتبر بود (صفحه مدت زیادی باز مانده یا برنامه روی هاست ری‌استارت شده) به جای خطای 400 به همان صفحه برمی‌گردد.</summary>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not IAntiforgeryValidationFailedResult) return;
        var http = context.HttpContext;
        log.LogWarning("Admin antiforgery validation failed on {Method} {Path}", http.Request.Method, http.Request.Path);
        SetFlash(http, "صفحه منقضی شده بود و تغییرات ذخیره نشد. صفحه دوباره باز شد؛ لطفاً دوباره ذخیره کنید.");
        context.Result = new RedirectResult(BackUrl(http));
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    // در این مسیرها فیلتر عادی ذخیره TempData اجرا نمی‌شود، پس پیام همین‌جا ذخیره می‌شود
    private void SetFlash(HttpContext http, string msg)
    {
        var td = tempData.GetTempData(http);
        td["Flash.Error"] = msg;
        td.Save();
    }

    public static string Describe(Exception ex) => ex switch
    {
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException
            => "حجم فایل‌های انتخاب‌شده بیشتر از حد مجاز است. فایل‌ها را کم‌حجم‌تر یا تعدادشان را کمتر کنید.",
        UnauthorizedAccessException or IOException
            => "ذخیره یا حذف فایل ممکن نشد. دوباره تلاش کنید؛ اگر تکرار شد کد پیگیری را بفرستید.",
        Microsoft.EntityFrameworkCore.DbUpdateException
            => "ذخیره در دیتابیس ممکن نشد. اطلاعات را بررسی کنید و دوباره تلاش کنید.",
        _ => "خطایی رخ داد و عملیات انجام نشد.",
    };

    /// <summary>آدرس صفحه قبلی (فقط اگر از همین سایت باشد)، وگرنه داشبورد پنل</summary>
    public static string BackUrl(HttpContext http)
    {
        if (Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var r)
            && string.Equals(r.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
            && r.AbsolutePath.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            return r.PathAndQuery;
        return "/admin";
    }

    private static ViewResult AdminErrorPage(ActionContext context, string message)
    {
        var view = new ViewResult
        {
            ViewName = AdminErrorView,
            StatusCode = StatusCodes.Status500InternalServerError,
            ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState),
        };
        view.ViewData["Message"] = message;
        view.ViewData["Back"] = BackUrl(context.HttpContext);
        return view;
    }

    public const string AdminErrorView = "~/Areas/Admin/Views/Shared/AdminError.cshtml";
}
