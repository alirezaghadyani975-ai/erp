using Erp.Costing.Application.Abstractions;
using Erp.Costing.Domain;
using Erp.Database;
using Microsoft.EntityFrameworkCore;

namespace Erp.Costing.Infrastructure.Persistence;

/// <summary>
/// نگاشت به جدول‌های schema cost. ساختار فقط با اسکریپت‌های پوشه database.
/// روی اتصال مشترک شرکت (CompanyConnection) کار می‌کند تا با حسابداری در یک تراکنش باشد.
/// </summary>
public class CostingDbContext : DbContext, ICostingDbContext
{
    public CostingDbContext(DbContextOptions<CostingDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public IQueryable<CostFactor> CostFactors => Set<CostFactor>();
    public IQueryable<CostCenterType> CostCenterTypes => Set<CostCenterType>();
    public IQueryable<CostCenter> CostCenters => Set<CostCenter>();
    public IQueryable<LookupItemRef> LookupItems => Set<LookupItemRef>();
    public IQueryable<PersonRef> Persons => Set<PersonRef>();

    void ICostingDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void ICostingDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> ICostingDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CostingDbContext).Assembly);
}
