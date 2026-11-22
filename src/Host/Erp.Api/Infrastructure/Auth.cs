using Erp.SharedKernel.Abstractions;
using System.Security.Claims;
using Erp.Security.Application;
using Erp.Security.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Erp.Api.Infrastructure;

/// <summary>
/// احراز هویت با کوکی. بعد از ورود موفق کوکی (HttpOnly) با شناسه کاربر، شرکت انتخاب‌شده و مهر امنیتی صادر می‌شود.
/// مسیرهای /account برای ورود، تغییر رمز و انتخاب شرکت‌اند؛ API (/api) به‌جای انتقال به صفحه ورود 401/403 برمی‌گرداند.
/// </summary>
public static class Auth
{
    public const string LoginPath = "/account/login";
    public const string CompanyPath = "/account/company";
    public const string ChangePasswordPath = "/account/change-password";
    public const string LogoutPath = "/account/logout";

    public static IServiceCollection AddErpAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "erp.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.LoginPath = LoginPath;
                o.LogoutPath = LogoutPath;
                o.AccessDeniedPath = LoginPath;
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(SecuritySettingsService.Defaults.IdleMinutes);
                o.Events.OnRedirectToLogin = ctx => ApiOr(ctx, 401);
                o.Events.OnRedirectToAccessDenied = ctx => ApiOr(ctx, 403);
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<SessionCache>();
        services.AddScoped<AppSession>();
        services.AddScoped<Erp.SharedKernel.Abstractions.ICurrentUser>(sp => sp.GetRequiredService<AppSession>());
        services.AddScoped<Erp.SharedKernel.Abstractions.ICompanyContext>(sp => sp.GetRequiredService<AppSession>());
        services.AddScoped<Erp.SharedKernel.Security.IAccessControl>(sp => sp.GetRequiredService<AppSession>());
        services.AddScoped<IClientInfo>(sp => sp.GetRequiredService<AppSession>());
        return services;
    }

    /// <summary>مدت اعتبار کوکی (لغزان) = زمان خروج خودکار تنظیمات امنیتی.</summary>
    public static void ApplyIdleTimeout(IServiceProvider services, int minutes) =>
        services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(5, minutes));

    private static Task ApiOr(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    /// <summary>کوکی ورود برای کاربر و شرکت (company خالی = هنوز شرکت انتخاب نشده).</summary>
    public static ClaimsPrincipal Principal(int userId, string userName, string displayName, Guid stamp, string? company,
        bool mustChangePassword, string? ip)
    {
        var claims = new List<Claim>
        {
            new(ErpClaims.UserId, userId.ToString()),
            new(ErpClaims.UserName, userName),
            new(ErpClaims.DisplayName, displayName),
            new(ErpClaims.Stamp, stamp.ToString()),
        };
        if (!string.IsNullOrEmpty(company)) claims.Add(new Claim(ErpClaims.Company, company));
        if (mustChangePassword) claims.Add(new Claim(ErpClaims.MustChangePassword, "1"));
        if (!string.IsNullOrEmpty(ip)) claims.Add(new Claim(ErpClaims.Ip, ip));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Task SignInAsync(HttpContext http, ClaimsPrincipal principal) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

    public static string? IpOf(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// بعد از احراز هویت: کاربری که باید رمز را عوض کند یا شرکت انتخاب نکرده به صفحه مربوط فرستاده می‌شود،
    /// و نشست (مجوزها و رشته اتصال شرکت) برای این درخواست بارگذاری می‌شود.
    /// </summary>
    public static IApplicationBuilder UseErpSession(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path;
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true || IsInfrastructure(path))
        {
            await next();
            return;
        }

        var isApi = path.StartsWithSegments("/api");
        var isAccount = path.StartsWithSegments("/account");
        if (!isAccount && user.HasClaim(c => c.Type == ErpClaims.MustChangePassword))
        {
            await Block(http, isApi, ChangePasswordPath, "ابتدا رمز عبور را عوض کنید.");
            return;
        }
        if (!isAccount && !user.HasClaim(c => c.Type == ErpClaims.Company))
        {
            await Block(http, isApi, CompanyPath, "ابتدا شرکت را انتخاب کنید.");
            return;
        }

        if (!isAccount)
        {
            var session = http.RequestServices.GetRequiredService<AppSession>();
            try
            {
                await session.LoadAsync(user, IpOf(http), http.RequestAborted);
            }
            catch (Exception ex) when (ex is SessionExpiredException or Erp.SharedKernel.Security.ForbiddenException)
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (isApi)
                {
                    http.Response.StatusCode = 401;
                    await http.Response.WriteAsJsonAsync(new { detail = ex.Message });
                }
                else http.Response.Redirect($"{LoginPath}?expired=1");
                return;
            }
        }
        await next();
    });

    private static bool IsInfrastructure(PathString path) =>
        path.StartsWithSegments("/_framework") || path.StartsWithSegments("/_content") || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/css") || path.StartsWithSegments("/fonts") || path.StartsWithSegments("/favicon.ico")
        // فایل‌های ثابت دیگر (فونت، تصویر، استایل): کاربری که هنوز رمز عوض نکرده یا شرکت انتخاب نکرده هم باید بگیردشان
        || (!path.StartsWithSegments("/api") && Path.HasExtension(path.Value));

    private static async Task Block(HttpContext http, bool isApi, string page, string message)
    {
        if (isApi)
        {
            http.Response.StatusCode = 403;
            await http.Response.WriteAsJsonAsync(new { detail = message });
        }
        else http.Response.Redirect(page);
    }

    /// <summary>خروج، نگه‌داشتن کوکی (ping) و ورود/خروج API.</summary>
    public static void MapErpAuthEndpoints(this WebApplication app)
    {
        app.MapGet(LogoutPath, async (HttpContext http, AuthService auth, bool? idle) =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
            {
                await auth.LogAsync(idle == true ? SecurityEvent.IdleLogout : SecurityEvent.Logout, ErpClaims.UserIdOf(http.User),
                    http.User.Identity.Name, http.User.FindFirstValue(ErpClaims.Company), IpOf(http), null, http.RequestAborted);
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            return Results.Redirect(idle == true ? $"{LoginPath}?idle=true" : LoginPath);
        }).AllowAnonymous();

        // تمدید کوکی در زمان کار با برنامه (اتصال Blazor درخواست HTTP نمی‌فرستد)
        app.MapGet("/account/ping", () => Results.NoContent()).RequireAuthorization();

        app.MapPost("/api/auth/login", async (HttpContext http, AuthService auth, ApiLoginRequest r) =>
        {
            var ip = IpOf(http);
            var result = await auth.LoginAsync(r.UserName, r.Password, ip, http.RequestAborted);
            if (result.Status != LoginStatus.Ok)
                return Results.Json(new { detail = result.Message }, statusCode: 401);
            if (result.MustChangePassword)
                return Results.Json(new { detail = "رمز عبور باید عوض شود؛ یک بار از طریق رابط کاربری وارد شوید." }, statusCode: 403);
            var companies = result.Companies ?? [];
            var company = string.IsNullOrWhiteSpace(r.Company) ? companies.FirstOrDefault() : companies.FirstOrDefault(c => c.Key == r.Company);
            if (company is null)
                return Results.Json(new { detail = "شرکت پیدا نشد یا به آن دسترسی ندارید." }, statusCode: 403);
            await SignInAsync(http, Principal(result.UserId, result.UserName!, result.DisplayName!, result.Stamp, company.Key, false, ip));
            await auth.LogAsync(SecurityEvent.CompanySelected, result.UserId, result.UserName, company.Key, ip, "ورود از API", http.RequestAborted);
            return Results.Ok(new { user = result.UserName, company = company.Key });
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/api/auth/logout", async (HttpContext http, AuthService auth) =>
        {
            await auth.LogAsync(SecurityEvent.Logout, ErpClaims.UserIdOf(http.User), http.User.Identity?.Name,
                http.User.FindFirstValue(ErpClaims.Company), IpOf(http), "API", http.RequestAborted);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization().DisableAntiforgery();
    }
}

public sealed record ApiLoginRequest(string UserName, string Password, string? Company);
