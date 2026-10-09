using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Data;
using SanaatMetal.Infrastructure;
using SanaatMetal.Models;
using SanaatMetal.Security;
using SanaatMetal.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Database (SQL Server؛ برای اجرای سریع محلی می‌توان Sqlite را انتخاب کرد) ----------
var provider = config["Database:Provider"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=sanaatmetal.db");
    else
        o.UseSqlServer(config.GetConnectionString("DefaultConnection"),
            sql => sql.EnableRetryOnFailure(5));
});

// ---------- Identity ----------
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 10;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsFactory>()
    .AddErrorDescriber<PersianIdentityErrorDescriber>();

// تغییر نقش یا غیرفعال‌سازی کاربر حداکثر بعد از ۵ دقیقه اعمال می‌شود
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "sm.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.LoginPath = "/admin/account/login";
    o.LogoutPath = "/admin/account/logout";
    o.AccessDeniedPath = "/admin/account/denied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "sm.af";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddScoped<IPermissionService, PermissionService>();

// ---------- App services ----------
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection()
    .SetApplicationName("SanaatMetal")
    .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys")));
builder.Services.AddScoped<Lang>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ExportService>();

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.ModelBinderProviders.Insert(0, new FlexibleDecimalBinderProvider());
});

// ---------- Rate limiting (فرم تماس، سبد خرید، ورود) ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("forms", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    // فرمت اعداد همیشه invariant است تا ورود اعداد اعشاری در فرم‌ها خراب نشود؛ فقط زبان رابط تغییر می‌کند
    var en = new CultureInfo("en-US");
    var fa = new CultureInfo("fa-IR");
    o.DefaultRequestCulture = new RequestCulture(en, fa);
    o.SupportedCultures = [en];
    o.SupportedUICultures = [fa, en];
    o.RequestCultureProviders = [new SiteCultureProvider()];
});

builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

var app = builder.Build();

ExportService.EnsureFonts(app.Environment.ContentRootPath);

using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider, provider);
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRequestLocalization();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAreaControllerRoute("admin", "Admin", "admin/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
