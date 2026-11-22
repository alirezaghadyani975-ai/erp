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
    public IQueryable<ItemCategory> ItemCategories => Set<ItemCategory>();
    public IQueryable<UnitRef> Units => Set<UnitRef>().AsNoTracking();
    public IQueryable<AccountRef> Accounts => Set<AccountRef>().AsNoTracking();
    public IQueryable<Warehouse> Warehouses => Set<Warehouse>();
    public IQueryable<StockDoc> StockDocs => Set<StockDoc>();
    public IQueryable<PartyRef> Parties => Set<PartyRef>().AsNoTracking();
    public IQueryable<FactorRef> Factors => Set<FactorRef>().AsNoTracking();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();
    public IQueryable<FiscalYearRef> FiscalYears => Set<FiscalYearRef>().AsNoTracking();
    public IQueryable<FiscalPeriodRef> FiscalPeriods => Set<FiscalPeriodRef>().AsNoTracking();
    public IQueryable<CostRun> CostRuns => Set<CostRun>();
    public IQueryable<CostBalance> CostBalances => Set<CostBalance>();

    public async Task<Dictionary<long, decimal>> ReceiptInvoiceAmountsAsync(IReadOnlyCollection<long> receiptLineIds, CancellationToken ct)
    {
        if (receiptLineIds.Count == 0) return [];
        var json = System.Text.Json.JsonSerializer.Serialize(receiptLineIds.Distinct());
        var rows = await Database.SqlQuery<IdAmount>($"""
            SELECT l.ReceiptLineId AS Id, l.Amount
            FROM pur.PurchaseInvoiceLines l JOIN OPENJSON({json}) j ON CAST(j.[value] AS BIGINT) = l.ReceiptLineId
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Amount);
    }

    public async Task WriteCostsAsync(IReadOnlyCollection<(long LineId, decimal UnitCost, decimal Amount)> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var json = System.Text.Json.JsonSerializer.Serialize(rows.Select(r => new { r.LineId, r.UnitCost, r.Amount }));
        await Database.ExecuteSqlInterpolatedAsync($"""
            WITH c AS (SELECT * FROM OPENJSON({json}) WITH (LineId BIGINT, UnitCost DECIMAL(24,4), Amount DECIMAL(24,4)))
            UPDATE l SET UnitCost = c.UnitCost, Amount = c.Amount FROM inv.StockDocLines l JOIN c ON c.LineId = l.Id;
            DELETE s FROM inv.StockLineCosts s JOIN OPENJSON({json}) WITH (LineId BIGINT) c ON c.LineId = s.LineId;
            INSERT inv.StockLineCosts (LineId, Element, Amount)
            SELECT LineId, 1, Amount FROM OPENJSON({json}) WITH (LineId BIGINT, Amount DECIMAL(24,4));
            """, ct);
    }

    public Task MarkStaleAfterAsync(DateOnly date, CancellationToken ct) =>
        Database.ExecuteSqlInterpolatedAsync($"UPDATE inv.CostRuns SET [Status] = 2 WHERE StartDate > {date} AND [Status] = 1", ct);

    private sealed class IdAmount
    {
        public long Id { get; init; }
        public decimal Amount { get; init; }
    }

    public async Task<bool> IsItemUsedAsync(int itemId, CancellationToken ct) =>
        await Database.SqlQuery<int>($"""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM pur.PurchaseInvoiceLines WHERE ItemId = {itemId})
                          OR EXISTS (SELECT 1 FROM sls.SalesInvoiceLines WHERE ItemId = {itemId})
                          OR EXISTS (SELECT 1 FROM inv.StockDocLines WHERE ItemId = {itemId}) THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct) == 1;

    public async Task<Dictionary<int, decimal>> LedgerBalancesAsync(IReadOnlyCollection<int> accountIds, DateOnly toDate, CancellationToken ct)
    {
        if (accountIds.Count == 0) return [];
        var json = System.Text.Json.JsonSerializer.Serialize(accountIds.Distinct());
        var rows = await Database.SqlQuery<LedgerRow>($"""
            WITH ids AS (SELECT CAST([value] AS INT) AS Id FROM OPENJSON({json}))
            SELECT l.AccountId, SUM(l.Debit - l.Credit) AS Balance
              FROM fin.VoucherLines l JOIN fin.Vouchers v ON v.Id = l.VoucherId JOIN ids ON ids.Id = l.AccountId
             WHERE v.Status IN (2, 3) AND v.VoucherDate <= {toDate}
             GROUP BY l.AccountId
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.AccountId, r => r.Balance);
    }

    private sealed class LedgerRow
    {
        public int AccountId { get; init; }
        public decimal Balance { get; init; }
    }

    public async Task<Dictionary<long, InvoiceLink>> InvoiceLinksAsync(IReadOnlyCollection<long> stockLineIds, CancellationToken ct)
    {
        if (stockLineIds.Count == 0) return [];
        var json = System.Text.Json.JsonSerializer.Serialize(stockLineIds.Distinct());
        var rows = await Database.SqlQuery<InvoiceLinkRow>($"""
            WITH ids AS (SELECT CAST([value] AS BIGINT) AS Id FROM OPENJSON({json}))
            SELECT l.ReceiptLineId AS StockLineId, CAST(1 AS TINYINT) AS Kind, d.Id AS DocId, d.DocNo
              FROM pur.PurchaseInvoiceLines l JOIN pur.PurchaseInvoices d ON d.Id = l.InvoiceId JOIN ids ON ids.Id = l.ReceiptLineId
            UNION ALL
            SELECT l.IssueLineId, CAST(2 AS TINYINT), d.Id, d.DocNo
              FROM pur.PurchaseReturnLines l JOIN pur.PurchaseReturns d ON d.Id = l.ReturnId JOIN ids ON ids.Id = l.IssueLineId
            UNION ALL
            SELECT l.IssueLineId, CAST(3 AS TINYINT), d.Id, d.DocNo
              FROM sls.SalesInvoiceLines l JOIN sls.SalesInvoices d ON d.Id = l.InvoiceId JOIN ids ON ids.Id = l.IssueLineId
            UNION ALL
            SELECT l.ReceiptLineId, CAST(4 AS TINYINT), d.Id, d.DocNo
              FROM sls.SalesReturnLines l JOIN sls.SalesReturns d ON d.Id = l.ReturnId JOIN ids ON ids.Id = l.ReceiptLineId
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.StockLineId, r => new InvoiceLink(r.StockLineId, (InvoiceLinkKind)r.Kind, r.DocId, r.DocNo));
    }

    private sealed class InvoiceLinkRow
    {
        public long StockLineId { get; init; }
        public byte Kind { get; init; }
        public long DocId { get; init; }
        public int DocNo { get; init; }
    }

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

internal class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("ItemCategories", "inv");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Title).HasMaxLength(50);
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

internal class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("Warehouses", "inv");
        b.HasKey(w => w.Id);
        b.Property(w => w.Code).HasMaxLength(10);
        b.Property(w => w.Title).HasMaxLength(100);
        b.Property(w => w.Notes).HasMaxLength(500);
        b.Ignore(w => w.DomainEvents);
    }
}

internal class StockDocConfiguration : IEntityTypeConfiguration<StockDoc>
{
    public void Configure(EntityTypeBuilder<StockDoc> b)
    {
        b.ToTable("StockDocs", "inv", t => { t.HasTrigger("trg_StockDocs_CostRuns"); t.HasTrigger("trg_StockDocs_Transfer"); });
        b.HasKey(d => d.Id);
        b.Property(d => d.Description).HasMaxLength(500);
        b.Property(d => d.CreatedAt).HasColumnType("datetime2(0)");
        b.Property(d => d.UpdatedAt).HasColumnType("datetime2(0)");
        b.Property(d => d.RowVer).IsRowVersion();
        b.Property(d => d.SourceModule).HasMaxLength(20).IsUnicode(false);
        b.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(d => d.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(d => d.DomainEvents);
        b.Ignore(d => d.IsReceipt);
    }
}

internal class StockDocLineConfiguration : IEntityTypeConfiguration<StockDocLine>
{
    public void Configure(EntityTypeBuilder<StockDocLine> b)
    {
        b.ToTable("StockDocLines", "inv", t =>
        {
            t.HasTrigger("trg_StockDocLines_Check");
            t.HasTrigger("trg_StockDocLines_CostRuns");
            t.HasTrigger("trg_StockDocLines_Category");
        });
        b.HasKey(l => l.Id);
        b.Property(l => l.Quantity).HasPrecision(24, 6);
        b.Property(l => l.EnteredPrice).HasPrecision(24, 4);
        b.Property(l => l.UnitCost).HasPrecision(24, 4);
        b.Property(l => l.Amount).HasPrecision(24, 4);
        b.Property(l => l.Description).HasMaxLength(500);
    }
}

internal class PartyRefConfiguration : IEntityTypeConfiguration<PartyRef>
{
    public void Configure(EntityTypeBuilder<PartyRef> b)
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
        b.ToTable("CostCenters", "cost", t => t.ExcludeFromMigrations());
        b.HasKey(c => c.Id);
    }
}

internal class FiscalPeriodRefConfiguration : IEntityTypeConfiguration<FiscalPeriodRef>
{
    public void Configure(EntityTypeBuilder<FiscalPeriodRef> b)
    {
        b.HasKey(p => p.Id);
        b.ToSqlQuery("SELECT Id, FiscalYearId, PeriodNo, Title, StartDate, EndDate FROM fin.FiscalPeriods");
    }
}

internal class CostRunConfiguration : IEntityTypeConfiguration<CostRun>
{
    public void Configure(EntityTypeBuilder<CostRun> b)
    {
        b.ToTable("CostRuns", "inv");
        b.HasKey(r => r.Id);
        b.Property(r => r.RunAt).HasColumnType("datetime2(0)");
    }
}

internal class CostBalanceConfiguration : IEntityTypeConfiguration<CostBalance>
{
    public void Configure(EntityTypeBuilder<CostBalance> b)
    {
        b.ToTable("CostBalances", "inv");
        b.HasKey(x => new { x.RunId, x.ItemId, x.WarehouseId });
        b.Property(x => x.Quantity).HasPrecision(24, 6);
        b.Property(x => x.Amount).HasPrecision(24, 4);
    }
}

internal class FiscalYearRefConfiguration : IEntityTypeConfiguration<FiscalYearRef>
{
    public void Configure(EntityTypeBuilder<FiscalYearRef> b)
    {
        b.HasKey(y => y.Id);
        b.ToSqlQuery("SELECT Id, StartDate, EndDate FROM fin.FiscalYears");
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
