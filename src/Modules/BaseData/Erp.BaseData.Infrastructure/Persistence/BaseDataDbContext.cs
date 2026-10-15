using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Domain.Banks;
using Erp.BaseData.Domain.Lookups;
using Erp.BaseData.Domain.Persons;
using Erp.BaseData.Domain.Taxes;
using Erp.Database;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Infrastructure.Persistence;

/// <summary>
/// نگاشت به جدول‌های schema base (اشخاص، جداول پایه، بانک‌ها). ساختار فقط با اسکریپت‌های پوشه database.
/// روی اتصال مشترک شرکت (CompanyConnection) کار می‌کند تا با حسابداری در یک تراکنش باشد.
/// </summary>
public class BaseDataDbContext : DbContext, IBaseDataDbContext
{
    public BaseDataDbContext(DbContextOptions<BaseDataDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public DbSet<Person> Persons => Set<Person>();
    public DbSet<PersonRoleType> PersonRoleTypes => Set<PersonRoleType>();
    public DbSet<PersonAttachment> PersonAttachments => Set<PersonAttachment>();
    public DbSet<LookupCategory> LookupCategories => Set<LookupCategory>();
    public DbSet<LookupItem> LookupItems => Set<LookupItem>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<VatRate> VatRates => Set<VatRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BaseDataDbContext).Assembly);
}
