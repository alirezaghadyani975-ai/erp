using Erp.Security.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Security.Application;

/// <summary>
/// ساخت نشست کاربر در یک شرکت از دیتابیس مرکزی: شرکت و رشته اتصالش، مجوزها (اجتماع نقش‌های فعال کاربر
/// در آن شرکت)، محدوده داده و «فقط اسناد خودش». مدیر سیستم همه مجوزها و همه شرکت‌های فعال را دارد.
/// </summary>
public class SessionLoader(ISecurityDbContext db, SecuritySettingsService settings)
{
    public async Task<SessionInfo> LoadAsync(int userId, string companyKey, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.IsActive)
            throw new ForbiddenException("حساب کاربری شما غیرفعال شده است؛ دوباره وارد شوید.");

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Key == companyKey && c.IsActive, ct)
            ?? throw new ForbiddenException($"شرکت «{companyKey}» پیدا نشد یا غیرفعال است.");
        var idle = (await settings.GetAsync(ct)).IdleMinutes;

        if (user.IsSystemAdmin)
            return new SessionInfo(user.Id, user.UserName, user.DisplayName, true, user.SecurityStamp, company.Id, company.Key,
                company.Title, company.ConnectionString, new HashSet<string>(), new Dictionary<ScopeKind, IReadOnlySet<int>>(), false, idle);

        var access = await db.UserCompanies.AsNoTracking()
            .Include(uc => uc.Roles)
            .Include(uc => uc.Scopes)
            .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.CompanyId == company.Id, ct)
            ?? throw new ForbiddenException($"شما به شرکت «{company.Title}» دسترسی ندارید.");

        var roleIds = access.Roles.Select(r => r.RoleId).ToList();
        var permissions = await db.Roles.AsNoTracking()
            .Where(r => r.IsActive && roleIds.Contains(r.Id))
            .SelectMany(r => r.Permissions.Select(p => p.PermissionKey))
            .Distinct()
            .ToListAsync(ct);

        return new SessionInfo(user.Id, user.UserName, user.DisplayName, false, user.SecurityStamp, company.Id, company.Key,
            company.Title, company.ConnectionString, permissions.ToHashSet(StringComparer.Ordinal), Scopes(access),
            access.OwnDocsOnly, idle);
    }

    internal static Dictionary<ScopeKind, IReadOnlySet<int>> Scopes(UserCompany access)
    {
        var result = new Dictionary<ScopeKind, IReadOnlySet<int>>();
        foreach (var kind in Enum.GetValues<ScopeKind>())
            if (access.IsLimited((byte)kind))
                result[kind] = access.Scopes.Where(s => s.Kind == (byte)kind).Select(s => s.ItemId).ToHashSet();
        return result;
    }
}
