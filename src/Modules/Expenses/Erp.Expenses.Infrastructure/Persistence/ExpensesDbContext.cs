using Erp.Database;
using Erp.Expenses.Application;
using Erp.Expenses.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Expenses.Infrastructure.Persistence;

/// <summary>
/// نگاشت به جدول‌های schema exp (ساختار فقط با اسکریپت‌های پوشه database).
/// روی اتصال مشترک شرکت کار می‌کند تا فرم و سند حسابداری‌اش در یک تراکنش ثبت شوند.
/// </summary>
public class ExpensesDbContext : DbContext, IExpensesDbContext
{
    public ExpensesDbContext(DbContextOptions<ExpensesDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<ExpenseInvoice> ExpenseInvoices => Set<ExpenseInvoice>();
    public IQueryable<SupplierRef> Suppliers => Set<SupplierRef>().AsNoTracking();
    public IQueryable<FactorRef> Factors => Set<FactorRef>().AsNoTracking();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();

    void IExpensesDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IExpensesDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IExpensesDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpensesDbContext).Assembly);
}

internal class ExpenseInvoiceConfiguration : IEntityTypeConfiguration<ExpenseInvoice>
{
    public void Configure(EntityTypeBuilder<ExpenseInvoice> b)
    {
        b.ToTable("ExpenseInvoices", "exp");
        b.HasKey(i => i.Id);
        b.Property(i => i.InvoiceNo).HasMaxLength(50);
        b.Property(i => i.VatAmount).HasPrecision(24, 4);
        b.Property(i => i.Description).HasMaxLength(500);
        b.Property(i => i.CreatedAt).HasColumnType("datetime2(0)");
        b.Property(i => i.UpdatedAt).HasColumnType("datetime2(0)");
        b.Property(i => i.RowVer).IsRowVersion();
        b.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(i => i.DomainEvents);
        b.Ignore(i => i.LinesTotal);
        b.Ignore(i => i.Total);
    }
}

internal class ExpenseInvoiceLineConfiguration : IEntityTypeConfiguration<ExpenseInvoiceLine>
{
    public void Configure(EntityTypeBuilder<ExpenseInvoiceLine> b)
    {
        b.ToTable("ExpenseInvoiceLines", "exp");
        b.HasKey(l => l.Id);
        b.Property(l => l.Amount).HasPrecision(24, 4);
        b.Property(l => l.Description).HasMaxLength(500);
        b.HasIndex(l => new { l.InvoiceId, l.RowNo }).IsUnique();
    }
}

internal class SupplierRefConfiguration : IEntityTypeConfiguration<SupplierRef>
{
    public void Configure(EntityTypeBuilder<SupplierRef> b)
    {
        b.ToTable("Persons", "base", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);
    }
}

internal class FactorRefConfiguration : IEntityTypeConfiguration<FactorRef>
{
    public void Configure(EntityTypeBuilder<FactorRef> b)
    {
        b.ToTable("CostFactors", "cost", t => t.ExcludeFromMigrations());
        b.HasKey(f => f.Id);
    }
}

internal class CenterRefConfiguration : IEntityTypeConfiguration<CenterRef>
{
    public void Configure(EntityTypeBuilder<CenterRef> b)
    {
        b.HasKey(c => c.Id);
        b.ToSqlQuery("SELECT c.Id, c.Title, t.[Function] AS [Function], c.IsActive " +
                     "FROM cost.CostCenters c JOIN cost.CostCenterTypes t ON t.Id = c.CenterTypeId");
    }
}
