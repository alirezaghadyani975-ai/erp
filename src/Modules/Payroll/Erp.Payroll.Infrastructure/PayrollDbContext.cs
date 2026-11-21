using Erp.Database;
using Erp.Payroll.Application;
using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Payroll.Infrastructure;

/// <summary>نگاشت به جدول‌های schema pay (ساختار فقط با اسکریپت‌های پوشه database).</summary>
public class PayrollDbContext : DbContext, IPayrollDbContext
{
    public PayrollDbContext(DbContextOptions<PayrollDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<PayItem> PayItems => Set<PayItem>();
    public IQueryable<YearParam> YearParams => Set<YearParam>();
    public IQueryable<PaySettings> Settings => Set<PaySettings>();
    public IQueryable<Decree> Decrees => Set<Decree>();
    public IQueryable<EmployeeRef> Employees => Set<EmployeeRef>().AsNoTracking();
    public IQueryable<FactorRef> Factors => Set<FactorRef>().AsNoTracking();
    public IQueryable<AccountRef> Accounts => Set<AccountRef>().AsNoTracking();

    void IPayrollDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IPayrollDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IPayrollDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PayItem>(e =>
        {
            e.ToTable("PayItems", "pay");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Formula).HasMaxLength(1000);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<YearParam>(e =>
        {
            e.ToTable("YearParams", "pay");
            e.HasKey(x => new { x.Year, x.Code });
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Value).HasPrecision(24, 6);
        });
        b.Entity<PaySettings>(e =>
        {
            e.ToTable("Settings", "pay");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
        b.Entity<Decree>(e =>
        {
            e.ToTable("Decrees", "pay");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.DecreeId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<DecreeItem>(e =>
        {
            e.ToTable("DecreeItems", "pay", t => t.HasTrigger("trg_DecreeItems_Source"));
            e.HasKey(x => new { x.DecreeId, x.PayItemId });
            e.Property(x => x.Amount).HasPrecision(24, 4);
        });
        b.Entity<EmployeeRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery(EmployeeRef.Sql);
            e.Property(x => x.EmployeeRate).HasPrecision(5, 2);
            e.Property(x => x.EmployerRate).HasPrecision(5, 2);
            e.Property(x => x.UnemploymentRate).HasPrecision(5, 2);
        });
        b.Entity<FactorRef>(e =>
        {
            e.ToTable("CostFactors", "cost", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });
        b.Entity<AccountRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery("SELECT Id, Code, Title, IsActive FROM fin.Accounts WHERE [Level] = 3");
        });
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول حقوق و دستمزد.</summary>
    public static IServiceCollection AddPayrollModule(this IServiceCollection services)
    {
        services.AddPayrollApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<PayrollDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<IPayrollDbContext>(sp => sp.GetRequiredService<PayrollDbContext>());
        return services;
    }
}
