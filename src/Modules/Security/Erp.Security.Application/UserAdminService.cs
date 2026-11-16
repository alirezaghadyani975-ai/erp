using Erp.SharedKernel.Abstractions;
using Erp.Security.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Security.Application;

/// <summary>
/// مدیریت کاربران: ایجاد، اصلاح، بازنشانی رمز، باز کردن قفل و تعیین دسترسی به شرکت‌ها.
/// فقط مدیر سیستم می‌تواند کسی را مدیر سیستم کند یا کاربر مدیر سیستم را تغییر دهد.
/// </summary>
public class UserAdminService(ISecurityDbContext db, IAccessControl access, IClientInfo client,
    SecuritySettingsService settings, TimeProvider clock)
{
    public async Task<List<UserListItemDto>> ListAsync(string? term, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        term = PersianText.NormalizeOrNull(term);
        var now = clock.GetUtcNow().UtcDateTime;

        var query = db.Users.AsNoTracking();
        if (term is not null) query = query.Where(u => u.UserName.Contains(term) || u.DisplayName.Contains(term));
        var users = await query.OrderBy(u => u.UserName).ToListAsync(ct);

        var companies = await db.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var links = await db.UserCompanies.AsNoTracking().Select(uc => new { uc.UserId, uc.CompanyId }).ToListAsync(ct);

        return users.Select(u => new UserListItemDto(u.Id, u.UserName, u.DisplayName, u.IsActive, u.IsSystemAdmin,
                u.IsLockedOut(now), u.LastLoginAt is { } t ? PersianDate.ToPersianDateTime(t) : null,
                u.IsSystemAdmin
                    ? "همه شرکت‌ها"
                    : string.Join("، ", links.Where(l => l.UserId == u.Id).Select(l => companies.GetValueOrDefault(l.CompanyId)))))
            .ToList();
    }

    public async Task<UserDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var u = await LoadAsync(id, ct, tracking: false);
        var now = clock.GetUtcNow().UtcDateTime;
        return new UserDto(u.Id, u.UserName, u.DisplayName, u.IsActive, u.IsSystemAdmin, u.MustChangePassword,
            u.IsLockedOut(now) ? PersianDate.ToPersianDateTime(u.LockoutUntil!.Value) : null,
            u.LastLoginAt is { } l ? PersianDate.ToPersianDateTime(l) : null,
            u.PasswordChangedAt is { } p ? PersianDate.ToPersianDateTime(p) : null,
            PersianDate.ToPersianDateTime(u.CreatedAt),
            u.Companies.OrderBy(c => c.CompanyId).Select(ToDto).ToList());
    }

    public async Task<int> CreateAsync(CreateUserRequest r, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        if (r.IsSystemAdmin) DemandSystemAdmin();
        var userName = User.NormalizeUserName(r.UserName);
        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
            throw new DomainException($"نام کاربری «{userName}» قبلاً ثبت شده است.");
        var cfg = await settings.GetAsync(ct);
        PasswordPolicy.Validate(r.Password, userName, cfg.PasswordMinLength);

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User(userName, r.DisplayName, PasswordHasher.Hash(r.Password), r.IsSystemAdmin, now);
        if (!r.IsActive) user.Update(user.DisplayName, false, r.IsSystemAdmin);
        db.Add(user);
        await db.SaveChangesAsync(ct);

        await LogAsync(SecurityEvent.UserCreated, user, r.IsSystemAdmin ? "مدیر سیستم" : null, ct);
        return user.Id;
    }

    public async Task UpdateAsync(int id, UpdateUserRequest r, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        GuardTarget(user);
        if (r.IsSystemAdmin != user.IsSystemAdmin) DemandSystemAdmin();
        if (user.Id == access.UserId && !r.IsActive) throw new DomainException("نمی‌توانید حساب خودتان را غیرفعال کنید.");
        if (user.Id == access.UserId && user.IsSystemAdmin && !r.IsSystemAdmin)
            throw new DomainException("نمی‌توانید مدیر سیستم بودن خودتان را بردارید.");
        if (user.IsSystemAdmin && (!r.IsSystemAdmin || !r.IsActive)) await EnsureAnotherAdminAsync(user.Id, ct);

        var changes = new List<string>();
        if (user.DisplayName != PersianText.NormalizeOrNull(r.DisplayName)) changes.Add("نام");
        if (user.IsActive != r.IsActive) changes.Add(r.IsActive ? "فعال شد" : "غیرفعال شد");
        if (user.IsSystemAdmin != r.IsSystemAdmin) changes.Add(r.IsSystemAdmin ? "مدیر سیستم شد" : "دیگر مدیر سیستم نیست");
        user.Update(r.DisplayName, r.IsActive, r.IsSystemAdmin);
        await db.SaveChangesAsync(ct);
        if (changes.Count > 0) await LogAsync(SecurityEvent.UserChanged, user, string.Join("، ", changes), ct);
    }

    /// <summary>بازنشانی رمز توسط مدیر؛ کاربر در ورود بعدی باید رمزش را عوض کند. رمز قبلی بازیابی‌شدنی نیست.</summary>
    public async Task ResetPasswordAsync(int id, string newPassword, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        GuardTarget(user);
        var cfg = await settings.GetAsync(ct);
        PasswordPolicy.Validate(newPassword, user.UserName, cfg.PasswordMinLength);
        user.SetPassword(PasswordHasher.Hash(newPassword), mustChange: true, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await LogAsync(SecurityEvent.PasswordReset, user, null, ct);
    }

    public async Task UnlockAsync(int id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        user.Unlock();
        await db.SaveChangesAsync(ct);
        await LogAsync(SecurityEvent.UserUnlocked, user, null, ct);
    }

    /// <summary>بستن همه نشست‌های باز کاربر (مثلاً بعد از گم شدن لپ‌تاپ).</summary>
    public async Task SignOutEverywhereAsync(int id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        GuardTarget(user);
        user.RenewStamp();
        await db.SaveChangesAsync(ct);
        await LogAsync(SecurityEvent.UserChanged, user, "خروج از همه نشست‌ها", ct);
    }

    /// <summary>دسترسی کاربر به شرکت‌ها؛ شرکتی که در فهرست نباشد گرفته می‌شود.</summary>
    public async Task SetAccessAsync(int id, List<UserCompanyAccessDto> companies, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        GuardTarget(user);

        var companyIds = companies.Select(c => c.CompanyId).ToList();
        if (companyIds.Count != companyIds.Distinct().Count()) throw new DomainException("یک شرکت دو بار آمده است.");
        var known = await db.Companies.Where(c => companyIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        if (known.Count != companyIds.Count) throw new DomainException("یکی از شرکت‌ها پیدا نشد.");

        var roleIds = companies.SelectMany(c => c.RoleIds).Distinct().ToList();
        var knownRoles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (knownRoles.Count != roleIds.Count) throw new DomainException("یکی از نقش‌ها پیدا نشد.");

        user.RevokeCompaniesExcept(companyIds);
        foreach (var c in companies)
        {
            var uc = user.GrantCompany(c.CompanyId);
            uc.Set(c.OwnDocsOnly, c.RoleIds,
                c.LimitedScopes.GroupBy(s => s.Kind).ToDictionary(
                    g => (byte)g.Key, g => (IReadOnlyCollection<int>)g.SelectMany(s => s.ItemIds).Distinct().ToList()));
        }
        await db.SaveChangesAsync(ct);

        var titles = await db.Companies.Where(c => companyIds.Contains(c.Id)).Select(c => c.Title).ToListAsync(ct);
        await LogAsync(SecurityEvent.UserAccessChanged, user,
            titles.Count == 0 ? "بدون دسترسی به شرکت" : "شرکت‌ها: " + string.Join("، ", titles), ct);
    }

    /// <summary>حذف فقط برای کاربری که هیچ‌وقت وارد نشده (ردپایی در اسناد ندارد)؛ بقیه غیرفعال شوند.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var user = await LoadAsync(id, ct);
        GuardTarget(user);
        if (user.Id == access.UserId) throw new DomainException("نمی‌توانید حساب خودتان را حذف کنید.");
        if (user.LastLoginAt is not null)
            throw new DomainException($"کاربر «{user.UserName}» قبلاً وارد سیستم شده و ممکن است فرم ثبت کرده باشد؛ به‌جای حذف، غیرفعالش کنید.");
        if (user.IsSystemAdmin) await EnsureAnotherAdminAsync(user.Id, ct);
        db.Remove(user);
        await db.SaveChangesAsync(ct);
        await LogAsync(SecurityEvent.UserDeleted, user, null, ct);
    }

    private async Task<User> LoadAsync(int id, CancellationToken ct, bool tracking = true)
    {
        IQueryable<User> query = db.Users.Include(u => u.Companies).ThenInclude(c => c.Roles)
            .Include(u => u.Companies).ThenInclude(c => c.Scopes);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("کاربر پیدا نشد.");
    }

    private void GuardTarget(User user)
    {
        if (user.IsSystemAdmin && user.Id != access.UserId) DemandSystemAdmin();
    }

    private void DemandSystemAdmin()
    {
        if (!access.IsSystemAdmin) throw new ForbiddenException("فقط مدیر سیستم می‌تواند مدیر سیستم را تعیین یا تغییر دهد.");
    }

    private async Task EnsureAnotherAdminAsync(int exceptId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id != exceptId && u.IsSystemAdmin && u.IsActive, ct))
            throw new DomainException("دست‌کم یک مدیر سیستم فعال باید بماند.");
    }

    private async Task LogAsync(SecurityEvent e, User user, string? details, CancellationToken ct)
    {
        db.Add(new SecurityLogEntry(e, user.Id, user.UserName, access.UserId, null, client.Ip, details, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }

    private static UserCompanyAccessDto ToDto(UserCompany c) =>
        new(c.CompanyId, c.OwnDocsOnly, c.Roles.Select(r => r.RoleId).Order().ToList(),
            Enum.GetValues<ScopeKind>().Where(k => c.IsLimited((byte)k))
                .Select(k => new ScopeLimitDto(k, c.Scopes.Where(s => s.Kind == (byte)k).Select(s => s.ItemId).Order().ToList()))
                .ToList());
}
