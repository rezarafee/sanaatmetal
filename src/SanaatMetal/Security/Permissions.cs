using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SanaatMetal.Data;

namespace SanaatMetal.Security;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string SalesManager = "SalesManager";
    public const string Secretary = "Secretary";

    public static readonly string[] All = [SuperAdmin, Admin, SalesManager, Secretary];

    public static string Title(string role) => role switch
    {
        SuperAdmin => "سوپر ادمین",
        Admin => "ادمین",
        SalesManager => "مسئول فروش",
        Secretary => "حسابدار / منشی",
        _ => role,
    };
}

public static class Permissions
{
    public const string Dashboard = "Dashboard.View";
    public const string CatalogView = "Catalog.View";
    public const string CatalogManage = "Catalog.Manage";
    public const string PricingView = "Pricing.View";
    public const string PricingManage = "Pricing.Manage";
    public const string OrdersView = "Orders.View";
    public const string OrdersManage = "Orders.Manage";
    public const string InvoicesView = "Invoices.View";
    public const string InvoicesCreate = "Invoices.Create";
    public const string InvoicesEdit = "Invoices.Edit";
    public const string InvoicesDelete = "Invoices.Delete";
    public const string Customers = "Customers.Manage";
    public const string Clients = "Clients.Manage";
    public const string MessagesView = "Messages.View";
    public const string MessagesManage = "Messages.Manage";
    public const string Reports = "Reports.View";
    public const string Settings = "Settings.Manage";
    public const string Users = "Users.Manage";
    public const string Audit = "Audit.View";

    public static readonly (string Group, (string Key, string Title)[] Items)[] Groups =
    [
        ("عمومی", [(Dashboard, "مشاهده داشبورد")]),
        ("محصولات", [(CatalogView, "مشاهده محصولات"), (CatalogManage, "مدیریت محصولات، عکس‌ها، گواهی‌ها و دیتاشیت‌ها")]),
        ("قیمت‌گذاری", [(PricingView, "مشاهده قیمت خرید و جدول قیمت"), (PricingManage, "قیمت‌گذاری و تخفیف")]),
        ("سفارش‌ها", [(OrdersView, "مشاهده سفارش‌های سایت"), (OrdersManage, "پیگیری و تغییر وضعیت سفارش")]),
        ("فاکتور", [(InvoicesView, "مشاهده فاکتورها"), (InvoicesCreate, "ثبت فاکتور"), (InvoicesEdit, "ویرایش کامل فاکتور"), (InvoicesDelete, "حذف فاکتور")]),
        ("اشخاص", [(Customers, "مدیریت خریداران"), (Clients, "مدیریت مشتریان ما (اسلایدر لوگو)")]),
        ("پیام‌ها", [(MessagesView, "خواندن پیام‌ها"), (MessagesManage, "مدیریت و حذف پیام‌ها")]),
        ("گزارش", [(Reports, "گزارش‌گیری و خروجی Excel / PDF")]),
        ("سیستم", [(Settings, "تنظیمات سایت و ایمیل"), (Users, "مدیریت کاربران و دسترسی‌ها"), (Audit, "مشاهده گزارش فعالیت کاربران")]),
    ];

    public static IEnumerable<string> All => Groups.SelectMany(g => g.Items.Select(i => i.Key));

    /// <summary>دسترسی‌های پیش‌فرض هر نقش (سوپرادمین همیشه همه دسترسی‌ها را دارد)</summary>
    public static IReadOnlyDictionary<string, string[]> Defaults { get; } = new Dictionary<string, string[]>
    {
        [Roles.Admin] = All.Where(p => p is not Users).ToArray(),
        [Roles.SalesManager] =
        [
            Dashboard, CatalogView, CatalogManage, PricingView, PricingManage, OrdersView, OrdersManage,
            InvoicesView, InvoicesCreate, InvoicesEdit, InvoicesDelete, Customers, Clients, MessagesView, Reports,
        ],
        // حسابدار/منشی: بدون قیمت‌گذاری و بدون ویرایش فاکتور؛ فقط ثبت و حذف فاکتور
        [Roles.Secretary] =
        [
            Dashboard, CatalogView, OrdersView, InvoicesCreate, InvoicesDelete, Customers, MessagesView, MessagesManage,
        ],
    };
}

public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>ساخت پالیسی پویا: [Authorize(Policy = "perm:Catalog.Manage")]</summary>
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) : base(PermissionPolicyProvider.Prefix + permission) { }
}

public interface IPermissionService
{
    Task<bool> HasAsync(ClaimsPrincipal user, string permission);
    Task<HashSet<string>> GetForUserAsync(ClaimsPrincipal user);
    void Invalidate();
}

public class PermissionService(AppDbContext db, IMemoryCache cache) : IPermissionService
{
    private const string CacheKey = "role-permissions";

    private async Task<Dictionary<string, HashSet<string>>> LoadAsync()
    {
        return (await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var rows = await db.RolePermissions.AsNoTracking().ToListAsync();
            return rows.GroupBy(r => r.RoleName)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Permission).ToHashSet());
        }))!;
    }

    public async Task<HashSet<string>> GetForUserAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return [];
        if (user.IsInRole(Roles.SuperAdmin)) return Permissions.All.ToHashSet();
        var map = await LoadAsync();
        var result = new HashSet<string>();
        foreach (var role in user.FindAll(ClaimTypes.Role).Select(c => c.Value))
            if (map.TryGetValue(role, out var perms)) result.UnionWith(perms);
        return result;
    }

    public async Task<bool> HasAsync(ClaimsPrincipal user, string permission) =>
        (await GetForUserAsync(user)).Contains(permission);

    public void Invalidate() => cache.Remove(CacheKey);
}

public class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(context.User, requirement.Permission))
            context.Succeed(requirement);
    }
}
