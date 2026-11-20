using Erp.Database;
using Erp.Inventory.Application;
using Erp.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Infrastructure.Persistence;

/// <summary>نگاشت به جدول‌های schema inv (ساختار فقط با اسکریپت‌های پوشه database).</summary>
public class InventoryDbContext : DbContext, IInventoryDbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<ItemGroup> ItemGroups => Set<ItemGroup>();
    public IQueryable<Item> Items => Set<Item>();
    public IQueryable<UnitRef> Units => Set<UnitRef>().AsNoTracking();
    public IQueryable<AccountRef> Accounts => Set<AccountRef>().AsNoTracking();

    public async Task<bool> IsItemUsedAsync(int itemId, CancellationToken ct) =>
        await Database.SqlQuery<int>($"""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM pur.PurchaseInvoiceLines WHERE ItemId = {itemId})
                          OR EXISTS (SELECT 1 FROM sls.SalesInvoiceLines WHERE ItemId = {itemId}) THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct) == 1;

    void IInventoryDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IInventoryDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IInventoryDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
}

internal class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroup>
{
    public void Configure(EntityTypeBuilder<ItemGroup> b)
    {
        b.ToTable("ItemGroups", "inv", t =>
        {
            t.HasTrigger("trg_ItemGroups_Accounts");
            t.HasTrigger("trg_ItemGroups_Kind");
        });
        b.HasKey(g => g.Id);
        b.Property(g => g.Title).HasMaxLength(100);
        b.Ignore(g => g.DomainEvents);
    }
}

internal class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("Items", "inv", t => t.HasTrigger("trg_Items_Kind"));
        b.HasKey(i => i.Id);
        b.Property(i => i.Code).HasMaxLength(30);
        b.Property(i => i.Title).HasMaxLength(200);
        b.Property(i => i.TaxItemId).HasMaxLength(13).IsUnicode(false);
        b.Property(i => i.Barcode).HasMaxLength(50).IsUnicode(false);
        b.Property(i => i.SalesPrice).HasPrecision(24, 4);
        b.Property(i => i.Notes).HasMaxLength(500);
        b.Property(i => i.CreatedAt).HasColumnType("datetime2(0)");
        b.Ignore(i => i.DomainEvents);
    }
}

internal class UnitRefConfiguration : IEntityTypeConfiguration<UnitRef>
{
    public void Configure(EntityTypeBuilder<UnitRef> b)
    {
        b.ToTable("Units", "base", t => t.ExcludeFromMigrations());
        b.HasKey(u => u.Id);
    }
}

internal class AccountRefConfiguration : IEntityTypeConfiguration<AccountRef>
{
    public void Configure(EntityTypeBuilder<AccountRef> b)
    {
        b.HasKey(a => a.Id);
        b.ToSqlQuery("SELECT Id, Code, Title, IsActive FROM fin.Accounts WHERE [Level] = 3");
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول کالا و خدمات (گروه‌ها و کالاها؛ بعداً انبار).</summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddInventoryApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<InventoryDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<IInventoryDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());
        return services;
    }
}
