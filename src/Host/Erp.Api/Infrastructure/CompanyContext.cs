using Erp.SharedKernel.Abstractions;

namespace Erp.Api.Infrastructure;

/// <summary>
/// تعیین شرکت جاری از هدر X-Company. اگر هدر نباشد شرکت پیش‌فرض استفاده می‌شود.
/// فهرست شرکت‌ها و رشته اتصال هرکدام در appsettings بخش Companies است.
/// (بعداً به دیتابیس مرکزی «کاتالوگ شرکت‌ها» منتقل می‌شود.)
/// </summary>
public class CompanyContext(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : ICompanyContext
{
    public const string HeaderName = "X-Company";
    public const string DefaultCompany = "Default";

    public string CompanyKey =>
        httpContextAccessor.HttpContext?.Request.Headers[HeaderName].FirstOrDefault() is { Length: > 0 } key
            ? key
            : DefaultCompany;

    public string ConnectionString =>
        configuration.GetSection("Companies")[CompanyKey]
        ?? throw new InvalidOperationException($"شرکت «{CompanyKey}» در تنظیمات تعریف نشده است.");
}

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    // تا پیاده‌سازی احراز هویت و دیتابیس مرکزی کاربران، همه ثبت‌ها با کاربر سیستمی (شناسه ۱) انجام می‌شود.
    public int UserId => 1;
    public string? UserName => httpContextAccessor.HttpContext?.User.Identity?.Name ?? "system";
}
