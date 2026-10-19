using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Security;

namespace SanaatMetal.Areas.Admin.Controllers;

[Area("Admin"), Authorize, TypeFilter(typeof(SanaatMetal.Infrastructure.AdminErrorFilter))]
public abstract class AdminControllerBase : Controller
{
    protected HashSet<string> Perms { get; private set; } = [];

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        Perms = await svc.GetForUserAsync(User);
        ViewBag.Perms = Perms;
        if (User.Identity?.IsAuthenticated == true)
        {
            var db = HttpContext.RequestServices.GetRequiredService<SanaatMetal.Data.AppDbContext>();
            if (Perms.Contains(Permissions.OrdersView))
                ViewBag.NewOrders = await db.Orders.CountAsync(o => o.Status == SanaatMetal.Models.OrderStatus.New);
            if (Perms.Contains(Permissions.MessagesView))
                ViewBag.UnreadMessages = await db.ContactMessages.CountAsync(m => !m.IsRead && !m.IsArchived);
        }
        await next();
    }

    protected bool Can(string permission) => Perms.Contains(permission);

    protected void Success(string msg) => TempData["Flash.Success"] = msg;
    protected void Error(string msg) => TempData["Flash.Error"] = msg;

    protected IActionResult BackTo(string? returnUrl, string fallbackAction) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(fallbackAction);
}
