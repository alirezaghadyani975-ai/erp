using System.Collections.Concurrent;
using System.Security.Claims;
using Erp.Security.Application;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace Erp.Api.Infrastructure;

/// <summary>نام claimهای کوکی ورود.</summary>
public static class ErpClaims
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string UserName = ClaimTypes.Name;
    public const string DisplayName = "erp:display";
    public const string Stamp = "erp:stamp";
    public const string Company = "erp:company";
    public const string MustChangePassword = "erp:pwchange";
    public const string Ip = "erp:ip";
    public const string Admin = "erp:admin";

    public static int? UserIdOf(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(UserId), out var id) ? id : null;
}

/// <summary>نشست نامعتبر شد (رمز عوض شد، کاربر غیرفعال شد یا از همه نشست‌ها خارج شد)؛ باید دوباره وارد شود.</summary>
public sealed class SessionExpiredException() : Exception("نشست شما منقضی شده است؛ دوباره وارد شوید.");

/// <summary>
/// نشست کاربر جاری در شرکت جاری: کاربر، شرکت (و رشته اتصالش از کاتالوگ مرکزی) و مجوزها.
/// همزمان <see cref="ICurrentUser"/>، <see cref="ICompanyContext"/> و <see cref="IAccessControl"/> است.
/// در درخواست HTTP (API و رندر اولیه صفحه) از کوکی پر می‌شود؛ در Blazor هر عملیات در scope جدا اجرا می‌شود
/// و <c>Backend</c> نشست اتصال را به آن scope کپی می‌کند. مجوزها هر دقیقه از دیتابیس مرکزی تازه می‌شوند.
/// </summary>
public sealed class AppSession(IServiceScopeFactory scopes, PermissionCatalog catalog, SessionCache cache, IServiceProvider services)
    : ICurrentUser, ICompanyContext, IAccessControl, IClientInfo
{
    private static readonly IReadOnlySet<int> Nothing = new HashSet<int>();
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);

    private DateTime _loadedAt;
    private (int UserId, string Company, Guid Stamp)? _identity;

    public SessionInfo? Info { get; private set; }

    public bool IsAuthenticated => Info is not null;

    public string? Ip { get; private set; }

    // ----- ICurrentUser
    public int UserId => Info?.UserId ?? 0;
    public string? UserName => Info?.UserName;

    // ----- ICompanyContext
    public string CompanyKey => Info?.CompanyKey ?? throw NotSignedIn();
    public string ConnectionString => Info?.ConnectionString ?? throw NotSignedIn();

    // ----- IAccessControl
    public bool IsSystemAdmin => Info?.IsSystemAdmin == true;

    public bool Has(string permission) => Info is { } i && (i.IsSystemAdmin || i.Permissions.Contains(permission));

    public void Demand(string permission)
    {
        if (Has(permission)) return;
        throw Info is null ? NotSignedIn() : new ForbiddenException($"شما مجوز «{catalog.Title(permission)}» را ندارید.");
    }

    public IReadOnlySet<int>? Allowed(ScopeKind kind) =>
        Info is null ? Nothing : Info.Scopes.TryGetValue(kind, out var set) ? set : null;

    public bool OwnDocsOnly => Info?.OwnDocsOnly ?? false;

    /// <summary>پر کردن نشست از کوکی (درخواست HTTP). نشست نامعتبر: <see cref="SessionExpiredException"/>.</summary>
    public async Task LoadAsync(ClaimsPrincipal user, string? ip, CancellationToken ct = default)
    {
        Ip = ip ?? user.FindFirstValue(ErpClaims.Ip);
        if (ErpClaims.UserIdOf(user) is not int userId || user.FindFirstValue(ErpClaims.Company) is not { Length: > 0 } company
            || !Guid.TryParse(user.FindFirstValue(ErpClaims.Stamp), out var stamp))
        {
            Info = null;
            _identity = null;
            return;
        }

        if (_identity == (userId, company, stamp) && DateTime.UtcNow - _loadedAt < RefreshEvery) return;
        _identity = (userId, company, stamp);
        await RefreshAsync(ct);
    }

    /// <summary>
    /// در اتصال Blazor: اگر نشست خالی یا قدیمی است از وضعیت احراز هویت اتصال پر/تازه می‌شود.
    /// </summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (_identity is not null && DateTime.UtcNow - _loadedAt < RefreshEvery) return;
        if (_identity is null)
        {
            var auth = services.GetService<AuthenticationStateProvider>();
            if (auth is null) return;
            var state = await auth.GetAuthenticationStateAsync();
            await LoadAsync(state.User, null, ct);
            return;
        }
        await RefreshAsync(ct);
    }

    /// <summary>کپی نشست اتصال Blazor به scope یک عملیات.</summary>
    public void Adopt(AppSession source)
    {
        Info = source.Info;
        Ip = source.Ip;
        _identity = source._identity;
        _loadedAt = source._loadedAt;
    }

    public void Clear()
    {
        Info = null;
        _identity = null;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var (userId, company, stamp) = _identity!.Value;
        var info = cache.Get(userId, company);
        if (info is null)
        {
            await using var scope = scopes.CreateAsyncScope();
            info = await scope.ServiceProvider.GetRequiredService<SessionLoader>().LoadAsync(userId, company, ct);
            cache.Set(info);
        }
        if (info.Stamp != stamp)
        {
            // شاید نسخه حافظه موقت قدیمی باشد (کاربر همین الان رمزش را عوض کرده)؛ یک بار از دیتابیس تازه می‌شود
            await using var scope = scopes.CreateAsyncScope();
            info = await scope.ServiceProvider.GetRequiredService<SessionLoader>().LoadAsync(userId, company, ct);
            cache.Set(info);
        }
        if (info.Stamp != stamp)
        {
            Clear();
            throw new SessionExpiredException();
        }
        Info = info;
        _loadedAt = DateTime.UtcNow;
    }

    private static ForbiddenException NotSignedIn() => new("ابتدا وارد سیستم شوید.");
}

/// <summary>نگهداری کوتاه‌مدت نشست‌ها (۳۰ ثانیه) تا هر درخواست API به دیتابیس مرکزی نرود.</summary>
public sealed class SessionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(int, string), (SessionInfo Info, DateTime At)> _items = new();

    public SessionInfo? Get(int userId, string company) =>
        _items.TryGetValue((userId, company), out var e) && DateTime.UtcNow - e.At < Ttl ? e.Info : null;

    public void Set(SessionInfo info) => _items[(info.UserId, info.CompanyKey)] = (info, DateTime.UtcNow);

    /// <summary>بعد از تغییر کاربر/نقش/رمز، نشست‌های ذخیره‌شده دور ریخته می‌شوند.</summary>
    public void Clear() => _items.Clear();
}
