using Erp.SharedKernel.Abstractions;
using Erp.Security.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Security.Application;

/// <summary>نقش‌ها: مجموعه مجوز سراسری که برای هر شرکت جدا به کاربر داده می‌شود.</summary>
public class RoleService(ISecurityDbContext db, IAccessControl access, IClientInfo client, PermissionCatalog catalog, TimeProvider clock)
{
    public IReadOnlyList<PermissionDef> Catalog => catalog.All;

    public async Task<List<RoleDto>> ListAsync(CancellationToken ct)
    {
        access.DemandAny(SecurityPermissions.Roles, SecurityPermissions.Users);
        var users = await db.UserCompanies.SelectMany(uc => uc.Roles)
            .GroupBy(r => r.RoleId).Select(g => new { g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Title)
            .Select(r => new { r.Id, r.Title, r.Description, r.IsActive, Count = r.Permissions.Count })
            .ToListAsync(ct);
        return roles.Select(r => new RoleDto(r.Id, r.Title, r.Description, r.IsActive, r.Count, users.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDetailsDto> GetAsync(int id, CancellationToken ct)
    {
        access.DemandAny(SecurityPermissions.Roles, SecurityPermissions.Users);
        var role = await db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("نقش پیدا نشد.");
        return new RoleDetailsDto(role.Id, role.Title, role.Description, role.IsActive,
            role.Permissions.Select(p => p.PermissionKey).Order().ToList());
    }

    public async Task<int> SaveAsync(int? id, SaveRoleRequest r, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Roles);
        var unknown = r.Permissions.Where(k => !catalog.Contains(k)).ToList();
        if (unknown.Count > 0) throw new DomainException($"مجوز ناشناخته: {string.Join("، ", unknown)}");
        var wanted = catalog.WithImplied(r.Permissions);

        Role role;
        if (id is null)
        {
            role = new Role(r.Title, r.Description);
            db.Add(role);
        }
        else
        {
            role = await db.Roles.Include(x => x.Permissions).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("نقش پیدا نشد.");
        }

        var before = role.Permissions.Select(p => p.PermissionKey).ToHashSet();
        role.Update(r.Title, r.Description, r.IsActive);
        var title = role.Title;
        if (await db.Roles.AnyAsync(x => x.Title == title && x.Id != role.Id, ct))
            throw new DomainException($"نقش «{title}» قبلاً تعریف شده است.");
        if (id is null) await db.SaveChangesAsync(ct);
        role.SetPermissions(wanted);
        await db.SaveChangesAsync(ct);

        // در لاگ امنیتی خود مجوزهای اضافه/حذف‌شده می‌آید (نه فقط تعدادشان)
        var added = wanted.Where(k => !before.Contains(k)).Select(catalog.Title).Order().ToList();
        var removed = before.Where(k => !wanted.Contains(k)).Select(catalog.Title).Order().ToList();
        var details = $"{(id is null ? "ایجاد" : "اصلاح")} نقش «{role.Title}»؛ {role.Permissions.Count} مجوز";
        if (added.Count > 0) details += $" | اضافه شد ({added.Count}): {string.Join("، ", added)}";
        if (removed.Count > 0) details += $" | حذف شد ({removed.Count}): {string.Join("، ", removed)}";
        await LogAsync(SecurityEvent.RoleChanged, details, ct);
        return role.Id;
    }

    public async Task<int> CopyAsync(int id, string newTitle, CancellationToken ct)
    {
        var source = await GetAsync(id, ct);
        return await SaveAsync(null, new SaveRoleRequest(newTitle, source.Description, true, source.Permissions), ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Roles);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("نقش پیدا نشد.");
        var users = await db.UserCompanies.CountAsync(uc => uc.Roles.Any(r => r.RoleId == id), ct);
        if (users > 0)
            throw new DomainException($"نقش «{role.Title}» به {users} دسترسی کاربر داده شده است؛ اول از کاربران بگیرید یا غیرفعالش کنید.");
        db.Remove(role);
        await db.SaveChangesAsync(ct);
        await LogAsync(SecurityEvent.RoleDeleted, $"نقش «{role.Title}»", ct);
    }

    private async Task LogAsync(SecurityEvent e, string details, CancellationToken ct)
    {
        db.Add(new SecurityLogEntry(e, null, null, access.UserId, null, client.Ip, details, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>کاتالوگ شرکت‌ها: هر شرکت دیتابیس جدا دارد که با اسکریپت‌ها ساخته/به‌روز می‌شود.</summary>
public class CompanyAdminService(ISecurityDbContext db, IAccessControl access, IClientInfo client,
    ICompanyProvisioner provisioner, IScopeItemSource scopeItems, TimeProvider clock)
{
    public async Task<List<CompanyDto>> ListAsync(CancellationToken ct)
    {
        access.DemandAny(SecurityPermissions.Companies, SecurityPermissions.Users);
        var users = await db.UserCompanies.GroupBy(uc => uc.CompanyId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rows = await db.Companies.AsNoTracking().OrderBy(c => c.Title).ToListAsync(ct);
        var canSeeConnection = access.Has(SecurityPermissions.Companies);
        return rows.Select(c => new CompanyDto(c.Id, c.Key, c.Title, canSeeConnection ? c.ConnectionString : "", c.IsActive,
            users.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<short> SaveAsync(short? id, SaveCompanyRequest r, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Companies);
        Company company;
        if (id is null)
        {
            var key = Company.NormalizeKey(r.Key);
            if (await db.Companies.AnyAsync(c => c.Key == key, ct)) throw new DomainException($"کلید شرکت «{key}» تکراری است.");
            company = new Company(key, r.Title, r.ConnectionString);
            if (!r.IsActive) company.Update(r.Title, r.ConnectionString, false);
            db.Add(company);
        }
        else
        {
            company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("شرکت پیدا نشد.");
            company.Update(r.Title, r.ConnectionString, r.IsActive);
        }
        await db.SaveChangesAsync(ct);
        db.Add(new SecurityLogEntry(SecurityEvent.CompanyChanged, null, null, access.UserId, company.Key, client.Ip,
            $"{(id is null ? "ایجاد" : "اصلاح")} شرکت «{company.Title}»{(company.IsActive ? "" : " (غیرفعال)")}", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return company.Id;
    }

    /// <summary>ساخت یا به‌روزرسانی دیتابیس شرکت (اسکریپت‌های اجرانشده). خروجی: نام اسکریپت‌های اجراشده.</summary>
    public async Task<IReadOnlyList<string>> MigrateAsync(short id, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Companies);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("شرکت پیدا نشد.");
        var applied = await provisioner.MigrateAsync(company.ConnectionString, ct);
        db.Add(new SecurityLogEntry(SecurityEvent.CompanyChanged, null, null, access.UserId, company.Key, client.Ip,
            applied.Count == 0 ? "دیتابیس به‌روز بود" : "اجرای اسکریپت‌ها: " + string.Join("، ", applied), clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return applied;
    }

    /// <summary>صندوق‌ها، حساب‌های بانکی، تنخواه‌گردان‌ها و سال‌های مالی یک شرکت برای تعیین محدوده داده کاربر.</summary>
    public async Task<IReadOnlyDictionary<ScopeKind, IReadOnlyList<ScopeItem>>> ScopeItemsAsync(short companyId, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Users);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("شرکت پیدا نشد.");
        return await scopeItems.GetAsync(company.ConnectionString, ct);
    }
}

/// <summary>گزارش لاگ امنیتی.</summary>
public class SecurityLogService(ISecurityDbContext db, IAccessControl access)
{
    public async Task<List<SecurityLogDto>> QueryAsync(SecurityLogFilter f, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Log);
        var query = db.SecurityLog.AsNoTracking();
        if (PersianDate.TryParse(f.FromDate, out var from))
        {
            var start = PersianDate.IranDayStartUtc(from);
            query = query.Where(l => l.At >= start);
        }
        if (PersianDate.TryParse(f.ToDate, out var to))
        {
            var end = PersianDate.IranDayStartUtc(to.AddDays(1));
            query = query.Where(l => l.At < end);
        }
        if (f.UserId is { } uid) query = query.Where(l => l.UserId == uid || l.ActorId == uid);
        if (f.EventType is { } e) query = query.Where(l => l.EventType == e);
        if (PersianText.NormalizeOrNull(f.Term) is { } term)
            query = query.Where(l => (l.Details != null && l.Details.Contains(term)) || (l.UserName != null && l.UserName.Contains(term))
                || (l.Ip != null && l.Ip.Contains(term)));

        var rows = await query.OrderByDescending(l => l.Id).Take(Math.Clamp(f.Take, 1, 5000)).ToListAsync(ct);
        var actorIds = rows.Where(r => r.ActorId is not null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var actors = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        return rows.Select(l => new SecurityLogDto(l.Id, PersianDate.ToPersianDateTime(l.At), l.EventType, SecurityEventTitles.Of(l.EventType),
            l.UserId, l.UserName, l.ActorId is { } a ? actors.GetValueOrDefault(a) : null, l.CompanyKey, l.Ip, l.Details)).ToList();
    }

}
