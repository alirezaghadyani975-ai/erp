using System.Text.RegularExpressions;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Security.Domain;

/// <summary>
/// کاربر (sec.Users در دیتابیس مرکزی). رمز عبور فقط به‌صورت هش یک‌طرفه نگه داشته می‌شود؛
/// این کلاس هیچ‌وقت رمز خام را نمی‌بیند (هش را <c>PasswordHasher</c> می‌سازد).
/// </summary>
public partial class User : AggregateRoot<int>
{
    private readonly List<UserCompany> _companies = [];

    private User() { } // EF

    public User(string userName, string displayName, string passwordHash, bool isSystemAdmin, DateTime nowUtc)
    {
        UserName = NormalizeUserName(userName);
        DisplayName = Texts.Required(displayName, "نام کاربر", 150);
        IsSystemAdmin = isSystemAdmin;
        CreatedAt = nowUtc;
        SetPassword(passwordHash, mustChange: true, nowUtc);
    }

    public string UserName { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public Guid SecurityStamp { get; private set; } = Guid.NewGuid();
    public bool IsActive { get; private set; } = true;
    public bool IsSystemAdmin { get; private set; }
    public bool MustChangePassword { get; private set; } = true;
    public short FailedCount { get; private set; }
    public DateTime? LockoutUntil { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime? PasswordChangedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<UserCompany> Companies => _companies;

    [GeneratedRegex("^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex UserNamePattern();

    /// <summary>نام کاربری فقط حروف لاتین، رقم، نقطه، خط تیره و زیرخط (۳ تا ۵۰ نویسه)؛ بزرگی و کوچکی حروف یکی است.</summary>
    public static string NormalizeUserName(string? userName)
    {
        var v = PersianText.NormalizeOrNull(userName)?.ToLowerInvariant()
            ?? throw new DomainException("نام کاربری الزامی است.");
        return UserNamePattern().IsMatch(v)
            ? v
            : throw new DomainException("نام کاربری باید ۳ تا ۵۰ نویسه از حروف لاتین، رقم، نقطه، خط تیره یا زیرخط باشد.");
    }

    public void Update(string displayName, bool isActive, bool isSystemAdmin)
    {
        DisplayName = Texts.Required(displayName, "نام کاربر", 150);
        if (IsActive != isActive || IsSystemAdmin != isSystemAdmin) RenewStamp();
        IsActive = isActive;
        IsSystemAdmin = isSystemAdmin;
    }

    /// <summary>هش جدید رمز. mustChange = کاربر در ورود بعدی باید رمز را عوض کند (بعد از بازنشانی مدیر).</summary>
    public void SetPassword(string passwordHash, bool mustChange, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(passwordHash) || !passwordHash.StartsWith("PBKDF2$", StringComparison.Ordinal))
            throw new DomainException("رمز عبور باید هش شده باشد.");
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
        PasswordChangedAt = nowUtc;
        FailedCount = 0;
        LockoutUntil = null;
        RenewStamp();
    }

    /// <summary>به‌روزرسانی هش با پارامترهای قوی‌تر بدون تغییر رمز (نشست‌ها باطل نمی‌شوند).</summary>
    public void UpgradeHash(string passwordHash) => PasswordHash = passwordHash;

    public bool IsLockedOut(DateTime nowUtc) => LockoutUntil is { } until && until > nowUtc;

    /// <summary>ورود ناموفق؛ اگر به سقف برسد حساب قفل می‌شود. خروجی: قفل شد یا نه.</summary>
    public bool RegisterFailure(DateTime nowUtc, int maxFailures, int lockoutMinutes)
    {
        FailedCount++;
        if (maxFailures <= 0 || FailedCount < maxFailures) return false;
        LockoutUntil = nowUtc.AddMinutes(Math.Max(1, lockoutMinutes));
        FailedCount = 0;
        return true;
    }

    public void RegisterLogin(DateTime nowUtc)
    {
        FailedCount = 0;
        LockoutUntil = null;
        LastLoginAt = nowUtc;
    }

    public void Unlock()
    {
        FailedCount = 0;
        LockoutUntil = null;
    }

    /// <summary>با عوض شدن مهر امنیتی، نشست‌های باز کاربر (در مرورگرهای دیگر) باطل می‌شوند.</summary>
    public void RenewStamp() => SecurityStamp = Guid.NewGuid();

    public UserCompany GrantCompany(short companyId)
    {
        var existing = _companies.FirstOrDefault(c => c.CompanyId == companyId);
        if (existing is not null) return existing;
        var uc = new UserCompany(Id, companyId);
        _companies.Add(uc);
        return uc;
    }

    public void RevokeCompaniesExcept(IReadOnlyCollection<short> keep) =>
        _companies.RemoveAll(c => !keep.Contains(c.CompanyId));
}

/// <summary>دسترسی کاربر به یک شرکت: نقش‌ها، «فقط اسناد خودش» و محدوده داده.</summary>
public class UserCompany
{
    private readonly List<UserCompanyRole> _roles = [];
    private readonly List<DataScope> _scopes = [];

    private UserCompany() { } // EF

    internal UserCompany(int userId, short companyId)
    {
        UserId = userId;
        CompanyId = companyId;
    }

    public int UserId { get; private set; }
    public short CompanyId { get; private set; }
    public bool OwnDocsOnly { get; private set; }

    /// <summary>بیت هر نوع محدوده (۱ صندوق، ۲ بانک، ۴ تنخواه، ۸ سال مالی): روشن = فقط موارد DataScopes.</summary>
    public byte ScopeLimits { get; private set; }

    public IReadOnlyCollection<UserCompanyRole> Roles => _roles;
    public IReadOnlyCollection<DataScope> Scopes => _scopes;

    public void Set(bool ownDocsOnly, IEnumerable<int> roleIds, IReadOnlyDictionary<byte, IReadOnlyCollection<int>> limitedScopes)
    {
        OwnDocsOnly = ownDocsOnly;

        var roles = roleIds.Distinct().ToHashSet();
        _roles.RemoveAll(r => !roles.Contains(r.RoleId));
        foreach (var id in roles.Where(id => _roles.All(r => r.RoleId != id)))
            _roles.Add(new UserCompanyRole(UserId, CompanyId, id));

        byte limits = 0;
        var wanted = new HashSet<(byte, int)>();
        foreach (var (kind, items) in limitedScopes)
        {
            if (kind is < 1 or > 5) throw new DomainException("نوع محدوده داده معتبر نیست.");
            limits |= (byte)(1 << (kind - 1));
            foreach (var item in items) wanted.Add((kind, item));
        }
        ScopeLimits = limits;
        _scopes.RemoveAll(s => !wanted.Contains((s.Kind, s.ItemId)));
        foreach (var (kind, item) in wanted.Where(w => !_scopes.Any(s => s.Kind == w.Item1 && s.ItemId == w.Item2)))
            _scopes.Add(new DataScope(UserId, CompanyId, kind, item));
    }

    public bool IsLimited(byte kind) => (ScopeLimits & (1 << (kind - 1))) != 0;
}

public class UserCompanyRole
{
    private UserCompanyRole() { } // EF

    internal UserCompanyRole(int userId, short companyId, int roleId)
    {
        UserId = userId;
        CompanyId = companyId;
        RoleId = roleId;
    }

    public int UserId { get; private set; }
    public short CompanyId { get; private set; }
    public int RoleId { get; private set; }
}

public class DataScope
{
    private DataScope() { } // EF

    internal DataScope(int userId, short companyId, byte kind, int itemId)
    {
        UserId = userId;
        CompanyId = companyId;
        Kind = kind;
        ItemId = itemId;
    }

    public int UserId { get; private set; }
    public short CompanyId { get; private set; }
    public byte Kind { get; private set; }
    public int ItemId { get; private set; }
}

/// <summary>نقش: مجموعه‌ای از مجوزها (سراسری؛ برای هر شرکت جدا به کاربر داده می‌شود).</summary>
public class Role : AggregateRoot<int>
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { } // EF

    public Role(string title, string? description) => Update(title, description, true);

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public void Update(string title, string? description, bool isActive)
    {
        Title = Texts.Required(title, "عنوان نقش", 100);
        Description = Texts.Optional(description, "توضیحات", 300);
        IsActive = isActive;
    }

    public void SetPermissions(IEnumerable<string> keys)
    {
        var wanted = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToHashSet(StringComparer.Ordinal);
        if (wanted.Any(k => k.Length > 80)) throw new DomainException("کلید مجوز حداکثر ۸۰ نویسه است.");
        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionKey));
        foreach (var key in wanted.Where(k => _permissions.All(p => p.PermissionKey != k)))
            _permissions.Add(new RolePermission(Id, key));
    }
}

public class RolePermission
{
    private RolePermission() { } // EF

    internal RolePermission(int roleId, string key)
    {
        RoleId = roleId;
        PermissionKey = key;
    }

    public int RoleId { get; private set; }
    public string PermissionKey { get; private set; } = null!;
}

/// <summary>شرکت در کاتالوگ مرکزی؛ هر شرکت دیتابیس جدا دارد.</summary>
public partial class Company : Entity<short>
{
    private Company() { } // EF

    public Company(string key, string title, string connectionString)
    {
        Key = NormalizeKey(key);
        Update(title, connectionString, true);
    }

    public string Key { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex KeyPattern();

    public static string NormalizeKey(string? key)
    {
        var v = key?.Trim() ?? "";
        return KeyPattern().IsMatch(v)
            ? v
            : throw new DomainException("کلید شرکت باید ۱ تا ۵۰ نویسه از حروف لاتین، رقم، خط تیره یا زیرخط باشد.");
    }

    public void Update(string title, string connectionString, bool isActive)
    {
        Title = Texts.Required(title, "نام شرکت", 150);
        ConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new DomainException("رشته اتصال دیتابیس شرکت الزامی است.")
            : connectionString.Trim();
        if (ConnectionString.Length > 500) throw new DomainException("رشته اتصال حداکثر ۵۰۰ نویسه است.");
        IsActive = isActive;
    }
}

/// <summary>نوع رویداد لاگ امنیتی.</summary>
public enum SecurityEvent : byte
{
    LoginSucceeded = 1,
    LoginFailed = 2,
    Logout = 3,
    LockedOut = 4,
    PasswordChanged = 5,
    PasswordReset = 6,
    CompanySelected = 7,
    UserCreated = 8,
    UserChanged = 9,
    UserAccessChanged = 10,
    UserUnlocked = 11,
    UserDeleted = 12,
    RoleChanged = 13,
    RoleDeleted = 14,
    CompanyChanged = 15,
    SettingsChanged = 16,
    AccessDenied = 17,
    IdleLogout = 18,
    Export = 19,
}

/// <summary>یک ردیف لاگ امنیتی (فقط درج).</summary>
public class SecurityLogEntry
{
    private SecurityLogEntry() { } // EF

    public SecurityLogEntry(SecurityEvent eventType, int? userId, string? userName, int? actorId, string? companyKey,
        string? ip, string? details, DateTime atUtc)
    {
        EventType = eventType;
        UserId = userId;
        UserName = Cut(userName, 50);
        ActorId = actorId;
        CompanyKey = Cut(companyKey, 50);
        Ip = Cut(ip, 45);
        Details = Cut(details, 1000);
        At = atUtc;
    }

    public long Id { get; private set; }
    public DateTime At { get; private set; }
    public SecurityEvent EventType { get; private set; }
    public int? UserId { get; private set; }
    public string? UserName { get; private set; }
    public int? ActorId { get; private set; }
    public string? CompanyKey { get; private set; }
    public string? Ip { get; private set; }
    public string? Details { get; private set; }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>تنظیم امنیتی (sec.Settings).</summary>
public class SecuritySetting
{
    private SecuritySetting() { } // EF

    public SecuritySetting(string key, int value, string title)
    {
        Key = key;
        Value = value;
        Title = title;
    }

    public string Key { get; private set; } = null!;
    public int Value { get; private set; }
    public string Title { get; private set; } = null!;

    public void Set(int value) => Value = value;
}

internal static class Texts
{
    public static string Required(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");
        return v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }

    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}
