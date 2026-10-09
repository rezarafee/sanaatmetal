using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SanaatMetal.Models;

namespace SanaatMetal.Security;

public class AppClaimsFactory(UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim("FullName", user.FullName ?? user.UserName ?? ""));
        return id;
    }
}

/// <summary>هدرهای امنیتی استاندارد (CSP، ضد کلیک‌جکینگ، ضد MIME sniffing ...)</summary>
public class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self' https://www.google.com https://maps.google.com https://www.openstreetmap.org https://neshan.org; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Content-Security-Policy"] = env.IsDevelopment() ? Csp : Csp + "; upgrade-insecure-requests";
        if (ctx.Request.Path.StartsWithSegments("/admin"))
        {
            h["Cache-Control"] = "no-store";
            h["X-Robots-Tag"] = "noindex, nofollow";
        }
        return next(ctx);
    }
}
