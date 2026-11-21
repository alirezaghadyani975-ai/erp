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
    public IQueryable<PayRun> Runs => Set<PayRun>();
    public IQueryable<Loan> Loans => Set<Loan>();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();
    public IQueryable<WorkshopRef> Workshops => Set<WorkshopRef>().AsNoTracking();
    public IQueryable<PersonInfoRef> PersonInfos => Set<PersonInfoRef>().AsNoTracking();
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
        b.Entity<PayRun>(e =>
        {
            e.ToTable("Runs", "pay");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.Property(x => x.CalculatedAt).HasColumnType("datetime2(0)");
            e.Ignore(x => x.Title);
            e.HasMany(x => x.Attendances).WithOne().HasForeignKey(a => a.RunId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Attendances).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RunId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.LoanDeductions).WithOne().HasForeignKey(l => l.RunId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.LoanDeductions).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<Attendance>(e =>
        {
            e.ToTable("Attendance", "pay", t =>
            {
                t.HasTrigger("trg_Attendance_Final");
                t.HasTrigger("trg_Attendance_Workshop");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkDays).HasPrecision(5, 2);
            e.Property(x => x.OtHours).HasPrecision(6, 2);
            e.Property(x => x.NightHours).HasPrecision(6, 2);
            e.Property(x => x.HolidayHours).HasPrecision(6, 2);
            e.Property(x => x.AbsentDays).HasPrecision(5, 2);
            e.HasMany(x => x.Centers).WithOne().HasForeignKey(c => c.AttendanceId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Centers).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Inputs).WithOne().HasForeignKey(i => i.AttendanceId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Inputs).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<AttendanceCenter>(e =>
        {
            e.ToTable("AttendanceCenters", "pay");
            e.HasKey(x => new { x.AttendanceId, x.CostCenterId });
            e.Property(x => x.Percent).HasPrecision(5, 2);
        });
        b.Entity<AttendanceInput>(e =>
        {
            e.ToTable("AttendanceInputs", "pay", t => t.HasTrigger("trg_AttendanceInputs_Source"));
            e.HasKey(x => new { x.AttendanceId, x.PayItemId });
            e.Property(x => x.Amount).HasPrecision(24, 0);
        });
        b.Entity<RunLine>(e =>
        {
            e.ToTable("RunLines", "pay", t => t.HasTrigger("trg_RunLines_Final"));
            e.HasKey(x => new { x.RunId, x.EmployeeId, x.PayItemId });
            e.Property(x => x.Amount).HasPrecision(24, 4);
        });
        b.Entity<Loan>(e =>
        {
            e.ToTable("Loans", "pay", t => t.HasTrigger("trg_Loans_Protect"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(24, 0);
            e.Property(x => x.InstallmentAmount).HasPrecision(24, 0);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.Ignore(x => x.ItemCode);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<RunLoanDeduction>(e =>
        {
            e.ToTable("RunLoanDeductions", "pay", t => t.HasTrigger("trg_RunLoanDeductions"));
            e.HasKey(x => new { x.RunId, x.LoanId });
            e.Property(x => x.Amount).HasPrecision(24, 0);
        });
        b.Entity<CenterRef>(e =>
        {
            e.ToTable("CostCenters", "cost", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });
        b.Entity<WorkshopRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery(WorkshopRef.Sql);
        });
        b.Entity<PersonInfoRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery(PersonInfoRef.Sql);
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
            e.ToSqlQuery("""
                SELECT a.Id, a.Code, a.Title, a.IsActive,
                       CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.AccountDetailLevels l WHERE l.AccountId = a.Id) THEN 1 ELSE 0 END AS BIT) AS HasDetails
                FROM fin.Accounts a WHERE a.[Level] = 3
                """);
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
