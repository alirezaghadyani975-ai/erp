using Erp.Database;
using Erp.Inventory.Contracts;
using Erp.Sales.Application;
using Erp.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Sales.Infrastructure;

/// <summary>
/// نگاشت به جدول‌های schema sls (ساختار فقط با اسکریپت‌های پوشه database).
/// روی اتصال مشترک شرکت کار می‌کند تا فرم و سند حسابداری‌اش در یک تراکنش ثبت شوند.
/// </summary>
public class SalesDbContext : DbContext, ISalesDbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public IQueryable<SalesReturn> SalesReturns => Set<SalesReturn>();
    public IQueryable<CustomerRef> Customers => Set<CustomerRef>().AsNoTracking();
    public IQueryable<SalesItemRef> Items => Set<SalesItemRef>().AsNoTracking();
    public IQueryable<StockLineRef> StockLines => Set<StockLineRef>().AsNoTracking();
    public IQueryable<DetailRef> Details => Set<DetailRef>().AsNoTracking();

    void ISalesDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void ISalesDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> ISalesDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SalesInvoice>(e =>
        {
            e.ToTable("SalesInvoices", "sls");
            e.HasKey(i => i.Id);
            e.Property(i => i.CashAmount).HasPrecision(24, 4);
            e.Property(i => i.GrossAmount).HasPrecision(24, 4);
            e.Property(i => i.DiscountAmount).HasPrecision(24, 4);
            e.Property(i => i.VatAmount).HasPrecision(24, 4);
            e.Property(i => i.Description).HasMaxLength(500);
            e.Property(i => i.CreatedAt).HasColumnType("datetime2(0)");
            e.Property(i => i.UpdatedAt).HasColumnType("datetime2(0)");
            e.Property(i => i.RowVer).IsRowVersion();
            e.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(i => i.DomainEvents);
            e.Ignore(i => i.NetAmount);
            e.Ignore(i => i.Total);
        });
        b.Entity<SalesInvoiceLine>(e =>
        {
            e.ToTable("SalesInvoiceLines", "sls", t => t.HasTrigger("trg_SalesInvoiceLines_Stock"));
            e.HasKey(l => l.Id);
            e.Property(l => l.Quantity).HasPrecision(24, 6);
            e.Property(l => l.UnitPrice).HasPrecision(24, 4);
            e.Property(l => l.Discount).HasPrecision(24, 4);
            e.Property(l => l.Amount).HasPrecision(24, 4);
            e.Property(l => l.VatAmount).HasPrecision(24, 4);
            e.Property(l => l.Description).HasMaxLength(500);
            e.Ignore(l => l.GrossAmount);
        });
        b.Entity<SalesReturn>(e =>
        {
            e.ToTable("SalesReturns", "sls");
            e.HasKey(r => r.Id);
            e.Property(r => r.GrossAmount).HasPrecision(24, 4);
            e.Property(r => r.DiscountAmount).HasPrecision(24, 4);
            e.Property(r => r.VatAmount).HasPrecision(24, 4);
            e.Property(r => r.Description).HasMaxLength(500);
            e.Property(r => r.CreatedAt).HasColumnType("datetime2(0)");
            e.Property(r => r.UpdatedAt).HasColumnType("datetime2(0)");
            e.Property(r => r.RowVer).IsRowVersion();
            e.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReturnId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(r => r.DomainEvents);
            e.Ignore(r => r.NetAmount);
            e.Ignore(r => r.Total);
        });
        b.Entity<SalesReturnLine>(e =>
        {
            e.ToTable("SalesReturnLines", "sls", t =>
            {
                t.HasTrigger("trg_SalesReturnLines_Quantity");
                t.HasTrigger("trg_SalesReturnLines_Stock");
            });
            e.HasKey(l => l.Id);
            e.Property(l => l.Quantity).HasPrecision(24, 6);
            e.Property(l => l.GrossAmount).HasPrecision(24, 4);
            e.Property(l => l.Discount).HasPrecision(24, 4);
            e.Property(l => l.Amount).HasPrecision(24, 4);
            e.Property(l => l.VatAmount).HasPrecision(24, 4);
        });
        b.Entity<StockLineRef>(e =>
        {
            e.HasKey(l => l.Id);
            e.Ignore(l => l.Title);
            e.Property(l => l.Quantity).HasPrecision(24, 6);
            e.Property(l => l.EnteredPrice).HasPrecision(24, 4);
            e.ToSqlQuery(StockLineRef.Sql);
        });
        b.Entity<CustomerRef>(e =>
        {
            e.ToTable("Persons", "base", t => t.ExcludeFromMigrations());
            e.HasKey(p => p.Id);
        });
        b.Entity<SalesItemRef>(e =>
        {
            e.Property(i => i.SalesPrice).HasPrecision(24, 4);
        });
        b.Entity<SalesItemRef>(e =>
        {
            e.HasKey(i => i.Id);
            e.ToSqlQuery("""
                SELECT i.Id, i.Code, i.Title, i.Kind, i.IsActive, i.VatExempt, u.Title AS UnitTitle, g.SalesAccountId, g.SalesReturnAccountId,
                       i.SalesPrice
                FROM inv.Items i JOIN inv.ItemGroups g ON g.Id = i.GroupId JOIN base.Units u ON u.Id = i.UnitId
                """);
        });
        b.Entity<DetailRef>(e =>
        {
            e.HasKey(d => d.Id);
            e.ToSqlQuery("SELECT Id, Code, Title, IsActive FROM fin.DetailAccounts");
        });
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول فروش (فاکتور فروش و برگشت). صدور سند از طریق IVoucherPosting ماژول حسابداری.</summary>
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddSalesApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<SalesDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<ISalesDbContext>(sp => sp.GetRequiredService<SalesDbContext>());
        return services;
    }
}
