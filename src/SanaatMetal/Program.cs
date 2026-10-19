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
var root = builder.Environment.ContentRootPath;

// ---------- Log خطاها در فایل (logs/errors-تاریخ.log) ----------
var webRoot = builder.Environment.WebRootPath ?? Path.Combine(root, "wwwroot");
var privateRoot = Path.Combine(webRoot, WritableDir.PrivateSegment);   // برای هاست‌هایی که فقط wwwroot قابل نوشتن است
var logDir = WritableDir.Pick(config["Diagnostics:LogPath"], Path.Combine(root, "logs"), Path.Combine(root, "App_Data", "logs"), Path.Combine(privateRoot, "logs"));
var fileLog = new FileLoggerProvider(logDir,
    Enum.TryParse<LogLevel>(config["Diagnostics:LogLevel"], true, out var lvl) ? lvl : LogLevel.Warning);
builder.Logging.AddProvider(fileLog);
// هشدارهای بی‌خطر و تکراری در فایل لاگ نوشته نشوند تا خطاهای واقعی گم نشوند
foreach (var noisy in new[] { "Microsoft.AspNetCore.Antiforgery", "Microsoft.AspNetCore.HttpsPolicy", "Microsoft.EntityFrameworkCore.Query",
                              "Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", "Microsoft.EntityFrameworkCore.Model.Validation" })
    builder.Logging.AddFilter<FileLoggerProvider>(noisy, LogLevel.Error);

// ---------- پوشه کلیدهای امنیتی (فرم‌ها، ورود و کوکی‌ها به آن نیاز دارند) ----------
// اگر مسیر تنظیم‌شده روی هاست وجود نداشته باشد یا اجازه نوشتن نداشته باشد، صفحه‌های دارای فرم خطای ۵۰۰ می‌دهند؛
// پس اولین پوشه قابل‌نوشتن انتخاب می‌شود.
var configuredKeys = config["DataProtection:KeysPath"];
var keysDir = WritableDir.Pick(configuredKeys, Path.Combine(root, "keys"), Path.Combine(root, "App_Data", "keys"), Path.Combine(privateRoot, "keys"));
Diag.LogDir = logDir ?? "(none writable)"; Diag.KeysDir = keysDir ?? "(none writable: keys kept in memory)"; Diag.KeysConfigured = configuredKeys;

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
    // اگر مهلت ورود هنگام ارسال فرم تمام شده باشد، بعد از ورود به همان صفحه فرم برگردد (نه آدرس POST که صفحه ندارد)
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Headers.XRequestedWith == "XMLHttpRequest") { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        if (HttpMethods.IsPost(ctx.Request.Method))
            ctx.RedirectUri = $"{o.LoginPath}?ReturnUrl={Uri.EscapeDataString(AdminErrorFilter.BackUrl(ctx.HttpContext))}";
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
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
var dp = builder.Services.AddDataProtection().SetApplicationName("SanaatMetal");
if (keysDir is not null) dp.PersistKeysToFileSystem(new DirectoryInfo(keysDir));
else builder.Services.Configure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(o => o.XmlRepository = new InMemoryXmlRepository());
    // هیچ پوشه‌ای قابل نوشتن نیست: کلیدها فقط در حافظه (با ری‌استارت سایت، کاربران پنل دوباره وارد می‌شوند)
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

var startLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startLog.LogWarning("Site starting. Environment={Env}, ContentRoot={Root}, Logs={Logs}, Keys={Keys}, Database={Provider}",
    app.Environment.EnvironmentName, root, logDir, keysDir, provider);
if (keysDir is null)
    startLog.LogError("No writable folder for data-protection keys (tried keys, App_Data/keys, wwwroot/_private/keys, temp). Keys are kept in memory only.");
else if (!string.IsNullOrWhiteSpace(configuredKeys) && !string.Equals(Path.GetFullPath(configuredKeys), keysDir, StringComparison.OrdinalIgnoreCase))
    startLog.LogError("DataProtection:KeysPath '{Configured}' is missing or not writable; using '{Used}' instead. Fix the path or remove it from appsettings.Production.json.",
        configuredKeys, keysDir);

try
{
    ExportService.EnsureFonts(root);
    using var scope = app.Services.CreateScope();
    await SeedData.InitializeAsync(scope.ServiceProvider, provider);
}
catch (Exception ex)
{
    startLog.LogCritical(ex, "Startup failed (database connection, migrations or seed). Check ConnectionStrings:DefaultConnection in appsettings.Production.json.");
    throw;
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    // Diagnostics:ShowDetailedErrors فقط برای عیب‌یابی موقت است؛ متن کامل خطا را به همه نشان می‌دهد و بعد از رفع مشکل باید false شود
    if (config.GetValue<bool>("Diagnostics:ShowDetailedErrors")) app.UseDeveloperExceptionPage();
    else app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRequestLocalization();
app.UseMiddleware<PrivateDataMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDiag();
app.MapStoredUploads();
app.MapAreaControllerRoute("admin", "Admin", "admin/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
