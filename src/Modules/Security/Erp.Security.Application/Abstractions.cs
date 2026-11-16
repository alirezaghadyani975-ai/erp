using Erp.Security.Domain;
using Erp.SharedKernel.Security;

namespace Erp.Security.Application;

/// <summary>دیتابیس مرکزی (schema sec).</summary>
public interface ISecurityDbContext
{
    IQueryable<User> Users { get; }
    IQueryable<UserCompany> UserCompanies { get; }
    IQueryable<Role> Roles { get; }
    IQueryable<Company> Companies { get; }
    IQueryable<SecurityLogEntry> SecurityLog { get; }
    IQueryable<SecuritySetting> Settings { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

/// <summary>ساخت/به‌روزرسانی دیتابیس یک شرکت با اسکریپت‌ها (پیاده‌سازی در Infrastructure).</summary>
public interface ICompanyProvisioner
{
    Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken ct);
}

/// <summary>فهرست صندوق‌ها، حساب‌های بانکی، تنخواه‌گردان‌ها و سال‌های مالی یک شرکت (برای تعیین محدوده داده).</summary>
public interface IScopeItemSource
{
    Task<IReadOnlyDictionary<ScopeKind, IReadOnlyList<ScopeItem>>> GetAsync(string connectionString, CancellationToken ct);
}

public sealed record ScopeItem(int Id, string Title, bool IsActive);
