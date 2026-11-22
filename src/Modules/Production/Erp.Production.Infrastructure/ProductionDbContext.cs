using Erp.Database;
using Erp.Personnel.Contracts;
using Erp.Production.Application;
using Erp.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Production.Infrastructure;

/// <summary>نگاشت به جدول‌های schema prd (ساختار فقط با اسکریپت‌های پوشه database).</summary>
public class ProductionDbContext : DbContext, IProductionDbContext
{
    public ProductionDbContext(DbContextOptions<ProductionDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<Bom> Boms => Set<Bom>();
    public IQueryable<ProductionReport> Reports => Set<ProductionReport>();
    public IQueryable<ProductionItemRef> Items => Set<ProductionItemRef>().AsNoTracking();
    public IQueryable<WarehouseRef> Warehouses => Set<WarehouseRef>().AsNoTracking();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();
    public IQueryable<EmployeeRef> Employees => Set<EmployeeRef>().AsNoTracking();
    public IQueryable<StockDocRef> StockDocs => Set<StockDocRef>().AsNoTracking();

    void IProductionDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IProductionDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IProductionDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Bom>(e =>
        {
            e.ToTable("Boms", "prd", t => t.HasTrigger("trg_Boms_Product"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.BaseQuantity).HasPrecision(18, 6);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.HasMany(x => x.Materials).WithOne().HasForeignKey(m => m.BomId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Materials).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Scraps).WithOne().HasForeignKey(m => m.BomId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Scraps).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Operations).WithOne().HasForeignKey(m => m.BomId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Operations).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<BomMaterial>(e =>
        {
            e.ToTable("BomMaterials", "prd", t => t.HasTrigger("trg_BomMaterials_Item"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasPrecision(18, 6);
            e.Property(x => x.LossPercent).HasPrecision(5, 2);
            e.Property(x => x.Notes).HasMaxLength(200);
            e.HasMany(x => x.Substitutes).WithOne().HasForeignKey(s => s.MaterialId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Substitutes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<BomSubstitute>(e =>
        {
            e.ToTable("BomSubstitutes", "prd");
            e.HasKey(x => x.Id);
            e.Property(x => x.Factor).HasPrecision(18, 6);
        });
        b.Entity<BomScrap>(e =>
        {
            e.ToTable("BomScraps", "prd", t => t.HasTrigger("trg_BomScraps_Item"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasPrecision(18, 6);
        });
        b.Entity<BomOperation>(e =>
        {
            e.ToTable("BomOperations", "prd");
            e.HasKey(x => x.Id);
            e.Property(x => x.ManSeconds).HasPrecision(18, 2);
            e.Property(x => x.MachineSeconds).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(200);
        });

        b.Entity<ProductionReport>(e =>
        {
            e.ToTable("ProductionReports", "prd");
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasPrecision(18, 6);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");
            e.HasMany(x => x.Materials).WithOne().HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Materials).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Scraps).WithOne().HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Scraps).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Operations).WithOne().HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Operations).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Operators).WithOne().HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Operators).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.StockDocs).WithOne().HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.NoAction);
            e.Navigation(x => x.StockDocs).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<ProductionMaterial>(e =>
        {
            e.ToTable("ProductionMaterials", "prd");
            e.HasKey(x => x.Id);
            e.Property(x => x.StandardQty).HasPrecision(18, 6);
            e.Property(x => x.ActualQty).HasPrecision(18, 6);
        });
        b.Entity<ProductionScrap>(e =>
        {
            e.ToTable("ProductionScraps", "prd");
            e.HasKey(x => x.Id);
            e.Property(x => x.StandardQty).HasPrecision(18, 6);
            e.Property(x => x.ActualQty).HasPrecision(18, 6);
        });
        b.Entity<ProductionOperation>(e =>
        {
            e.ToTable("ProductionOperations", "prd");
            e.HasKey(x => new { x.ReportId, x.CostCenterId });
            e.Property(x => x.StdManSeconds).HasPrecision(18, 2);
            e.Property(x => x.StdMachineSeconds).HasPrecision(18, 2);
            e.Property(x => x.ActualManSeconds).HasPrecision(18, 2);
            e.Property(x => x.ActualMachineSeconds).HasPrecision(18, 2);
        });
        b.Entity<ProductionOperator>(e =>
        {
            e.ToTable("ProductionOperators", "prd");
            e.HasKey(x => new { x.ReportId, x.EmployeeId });
            e.Property(x => x.Hours).HasPrecision(8, 2);
        });
        b.Entity<ProductionStockDoc>(e =>
        {
            e.ToTable("ProductionStockDocs", "prd");
            e.HasKey(x => new { x.ReportId, x.StockDocId });
        });

        b.Entity<ProductionItemRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery("""
                SELECT i.Id, i.Code, i.Title, u.Title AS Unit, i.CategoryId, c.Title AS Category, i.IsActive,
                       ISNULL(c.CanProductionInput, CAST(0 AS BIT)) AS CanInput, ISNULL(c.CanProductionOutput, CAST(0 AS BIT)) AS CanOutput,
                       ISNULL(c.IsScrap, CAST(0 AS BIT)) AS IsScrap
                FROM inv.Items i JOIN base.Units u ON u.Id = i.UnitId LEFT JOIN inv.ItemCategories c ON c.Id = i.CategoryId
                WHERE i.Kind = 1
                """);
        });
        b.Entity<WarehouseRef>(e =>
        {
            e.ToTable("Warehouses", "inv", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });
        b.Entity<CenterRef>(e =>
        {
            e.ToTable("CostCenters", "cost", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });
        b.Entity<EmployeeRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery(EmployeeRef.Sql);
            e.Property(x => x.EmployeeRate).HasPrecision(5, 2);
            e.Property(x => x.EmployerRate).HasPrecision(5, 2);
            e.Property(x => x.UnemploymentRate).HasPrecision(5, 2);
        });
        b.Entity<StockDocRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery("SELECT Id, DocType, DocNo, WarehouseId FROM inv.StockDocs");
        });
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول تولید (فرمول ساخت و گزارش تولید).</summary>
    public static IServiceCollection AddProductionModule(this IServiceCollection services)
    {
        services.AddProductionApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<ProductionDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<IProductionDbContext>(sp => sp.GetRequiredService<ProductionDbContext>());
        return services;
    }
}
