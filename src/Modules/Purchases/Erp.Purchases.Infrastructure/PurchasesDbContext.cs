using Erp.Database;
using Erp.Purchases.Application;
using Erp.Purchases.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Purchases.Infrastructure;

/// <summary>
/// نگاشت به جدول‌های schema pur (ساختار فقط با اسکریپت‌های پوشه database).
/// روی اتصال مشترک شرکت کار می‌کند تا فرم و سند حسابداری‌اش در یک تراکنش ثبت شوند.
/// </summary>
public class PurchasesDbContext : DbContext, IPurchasesDbContext
{
    public PurchasesDbContext(DbContextOptions<PurchasesDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public IQueryable<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public IQueryable<PartyRef> Parties => Set<PartyRef>().AsNoTracking();
    public IQueryable<ItemRef> Items => Set<ItemRef>().AsNoTracking();

    void IPurchasesDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IPurchasesDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IPurchasesDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PurchaseInvoice>(e =>
        {
            e.ToTable("PurchaseInvoices", "pur");
            e.HasKey(i => i.Id);
            e.Property(i => i.InvoiceNo).HasMaxLength(50);
            e.Property(i => i.SellerTaxId).HasMaxLength(22).IsUnicode(false);
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
        b.Entity<PurchaseInvoiceLine>(e =>
        {
            e.ToTable("PurchaseInvoiceLines", "pur");
            e.HasKey(l => l.Id);
            e.Property(l => l.Quantity).HasPrecision(24, 6);
            e.Property(l => l.UnitPrice).HasPrecision(24, 4);
            e.Property(l => l.Discount).HasPrecision(24, 4);
            e.Property(l => l.Amount).HasPrecision(24, 4);
            e.Property(l => l.VatAmount).HasPrecision(24, 4);
            e.Property(l => l.Description).HasMaxLength(500);
        });
        b.Entity<PurchaseReturn>(e =>
        {
            e.ToTable("PurchaseReturns", "pur");
            e.HasKey(r => r.Id);
            e.Property(r => r.Amount).HasPrecision(24, 4);
            e.Property(r => r.VatAmount).HasPrecision(24, 4);
            e.Property(r => r.Description).HasMaxLength(500);
            e.Property(r => r.CreatedAt).HasColumnType("datetime2(0)");
            e.Property(r => r.UpdatedAt).HasColumnType("datetime2(0)");
            e.Property(r => r.RowVer).IsRowVersion();
            e.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReturnId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(r => r.DomainEvents);
            e.Ignore(r => r.Total);
        });
        b.Entity<PurchaseReturnLine>(e =>
        {
            e.ToTable("PurchaseReturnLines", "pur", t => t.HasTrigger("trg_PurchaseReturnLines_Quantity"));
            e.HasKey(l => l.Id);
            e.Property(l => l.Quantity).HasPrecision(24, 6);
            e.Property(l => l.Amount).HasPrecision(24, 4);
            e.Property(l => l.VatAmount).HasPrecision(24, 4);
        });
        b.Entity<PartyRef>(e =>
        {
            e.ToTable("Persons", "base", t => t.ExcludeFromMigrations());
            e.HasKey(p => p.Id);
        });
        b.Entity<ItemRef>(e =>
        {
            e.HasKey(i => i.Id);
            e.ToSqlQuery("""
                SELECT i.Id, i.Code, i.Title, i.Kind, i.IsActive, i.VatExempt, u.Title AS UnitTitle, g.PurchaseAccountId
                FROM inv.Items i JOIN inv.ItemGroups g ON g.Id = i.GroupId JOIN base.Units u ON u.Id = i.UnitId
                """);
        });
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول خرید (فاکتور خرید و برگشت). صدور سند از طریق IVoucherPosting ماژول حسابداری.</summary>
    public static IServiceCollection AddPurchasesModule(this IServiceCollection services)
    {
        services.AddPurchasesApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<PurchasesDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<IPurchasesDbContext>(sp => sp.GetRequiredService<PurchasesDbContext>());
        return services;
    }
}
