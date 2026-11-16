using Erp.Security.Domain;
using Erp.SharedKernel.Security;

namespace Erp.Security.Application;

/// <summary>نشست کاربر در یک شرکت: هویت، شرکت و مجوزها (مجوز = اجتماع مجوزهای نقش‌های کاربر در این شرکت).</summary>
public sealed record SessionInfo(
    int UserId,
    string UserName,
    string DisplayName,
    bool IsSystemAdmin,
    Guid Stamp,
    short CompanyId,
    string CompanyKey,
    string CompanyTitle,
    string ConnectionString,
    IReadOnlySet<string> Permissions,
    IReadOnlyDictionary<ScopeKind, IReadOnlySet<int>> Scopes,
    bool OwnDocsOnly,
    int IdleMinutes);

public sealed record CompanyOption(short Id, string Key, string Title);

public enum LoginStatus
{
    Ok,
    Invalid,
    LockedOut,
    Inactive,
}

public sealed record LoginResult(
    LoginStatus Status,
    string? Message,
    int UserId = 0,
    string? UserName = null,
    string? DisplayName = null,
    Guid Stamp = default,
    bool MustChangePassword = false,
    IReadOnlyList<CompanyOption>? Companies = null);

public sealed record SecuritySettings(int MaxFailedLogins, int LockoutMinutes, int PasswordMinLength, int IdleMinutes);

public sealed record SettingDto(string Key, string Title, int Value);

// ----- کاربران -----

public sealed record UserListItemDto(
    int Id, string UserName, string DisplayName, bool IsActive, bool IsSystemAdmin, bool IsLocked,
    string? LastLogin, string Companies);

public sealed record UserDto(
    int Id, string UserName, string DisplayName, bool IsActive, bool IsSystemAdmin, bool MustChangePassword,
    string? LockedUntil, string? LastLogin, string? PasswordChanged, string CreatedAt,
    List<UserCompanyAccessDto> Companies);

/// <summary>دسترسی کاربر به یک شرکت. LimitedScopes: فقط نوع‌هایی که محدودند (فهرست خالی = هیچ‌کدام).</summary>
public sealed record UserCompanyAccessDto(short CompanyId, bool OwnDocsOnly, List<int> RoleIds, List<ScopeLimitDto> LimitedScopes);

public sealed record ScopeLimitDto(ScopeKind Kind, List<int> ItemIds);

public sealed record CreateUserRequest(string UserName, string DisplayName, string Password, bool IsSystemAdmin = false, bool IsActive = true);

public sealed record UpdateUserRequest(string DisplayName, bool IsActive, bool IsSystemAdmin);

// ----- نقش‌ها -----

public sealed record RoleDto(int Id, string Title, string? Description, bool IsActive, int PermissionCount, int UserCount);

public sealed record RoleDetailsDto(int Id, string Title, string? Description, bool IsActive, List<string> Permissions);

public sealed record SaveRoleRequest(string Title, string? Description, bool IsActive, List<string> Permissions);

// ----- شرکت‌ها -----

public sealed record CompanyDto(short Id, string Key, string Title, string ConnectionString, bool IsActive, int UserCount);

public sealed record SaveCompanyRequest(string Key, string Title, string ConnectionString, bool IsActive);

// ----- لاگ امنیتی -----

public sealed record SecurityLogFilter(string? FromDate, string? ToDate, int? UserId, SecurityEvent? EventType, string? Term, int Take = 500);

public sealed record SecurityLogDto(long Id, string At, SecurityEvent EventType, string EventTitle, int? UserId, string? UserName,
    string? ActorName, string? CompanyKey, string? Ip, string? Details);

public static class SecurityEventTitles
{
    public static string Of(SecurityEvent e) => e switch
    {
        SecurityEvent.LoginSucceeded => "ورود موفق",
        SecurityEvent.LoginFailed => "ورود ناموفق",
        SecurityEvent.Logout => "خروج",
        SecurityEvent.LockedOut => "قفل شدن حساب",
        SecurityEvent.PasswordChanged => "تغییر رمز",
        SecurityEvent.PasswordReset => "بازنشانی رمز",
        SecurityEvent.CompanySelected => "انتخاب شرکت",
        SecurityEvent.UserCreated => "ایجاد کاربر",
        SecurityEvent.UserChanged => "اصلاح کاربر",
        SecurityEvent.UserAccessChanged => "تغییر دسترسی کاربر",
        SecurityEvent.UserUnlocked => "باز کردن قفل",
        SecurityEvent.UserDeleted => "حذف کاربر",
        SecurityEvent.RoleChanged => "تغییر نقش",
        SecurityEvent.RoleDeleted => "حذف نقش",
        SecurityEvent.CompanyChanged => "تغییر شرکت‌ها",
        SecurityEvent.SettingsChanged => "تغییر تنظیمات امنیتی",
        SecurityEvent.AccessDenied => "تلاش بدون مجوز",
        SecurityEvent.IdleLogout => "خروج خودکار (بی‌کاری)",
        _ => e.ToString(),
    };
}
