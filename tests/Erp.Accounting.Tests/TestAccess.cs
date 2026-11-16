using Erp.SharedKernel.Security;

namespace Erp.Accounting.Tests;

/// <summary>
/// دسترسی قابل تعویض در تست: پیش‌فرض دسترسی کامل؛ با <see cref="As"/> برای همان جریان async
/// (همان تست) دسترسی محدود جایگزین می‌شود.
/// </summary>
public sealed class TestAccess : IAccessControl
{
    public static readonly TestAccess Instance = new();
    private static readonly AsyncLocal<IAccessControl?> Current = new();
    private static readonly FullAccess Full = new();

    private static IAccessControl Inner => Current.Value ?? Full;

    public static IDisposable As(IAccessControl access)
    {
        Current.Value = access;
        return new Reset();
    }

    public bool IsSystemAdmin => Inner.IsSystemAdmin;
    public bool Has(string permission) => Inner.Has(permission);
    public void Demand(string permission) => Inner.Demand(permission);
    public IReadOnlySet<int>? Allowed(ScopeKind kind) => Inner.Allowed(kind);
    public bool OwnDocsOnly => Inner.OwnDocsOnly;
    public int UserId => Inner.UserId;

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

/// <summary>کاربر محدود برای تست: فقط مجوزهای داده‌شده، محدوده داده و «فقط اسناد خودش».</summary>
public sealed class Restricted(int userId, params string[] permissions) : IAccessControl
{
    private readonly HashSet<string> _permissions = [.. permissions];
    private readonly Dictionary<ScopeKind, IReadOnlySet<int>> _scopes = [];

    public bool OwnDocsOnly { get; init; }

    public Restricted Limit(ScopeKind kind, params int[] ids)
    {
        _scopes[kind] = ids.ToHashSet();
        return this;
    }

    public bool IsSystemAdmin => false;
    public bool Has(string permission) => _permissions.Contains(permission);

    public void Demand(string permission)
    {
        if (!Has(permission)) throw new ForbiddenException($"شما مجوز «{permission}» را ندارید.");
    }

    public IReadOnlySet<int>? Allowed(ScopeKind kind) => _scopes.GetValueOrDefault(kind);
    public int UserId => userId;
}
