using Erp.Database;
using Erp.Personnel.Application;
using Erp.Personnel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Personnel.Infrastructure;

/// <summary>نگاشت به جدول‌های schema hr (ساختار فقط با اسکریپت‌های پوشه database).</summary>
public class PersonnelDbContext : DbContext, IPersonnelDbContext
{
    public PersonnelDbContext(DbContextOptions<PersonnelDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public IQueryable<Job> Jobs => Set<Job>();
    public IQueryable<Workshop> Workshops => Set<Workshop>();
    public IQueryable<Employee> Employees => Set<Employee>();
    public IQueryable<PersonRef> Persons => Set<PersonRef>().AsNoTracking();
    public IQueryable<CenterRef> Centers => Set<CenterRef>().AsNoTracking();

    public async Task<bool> IsEmployeeUsedAsync(int employeeId, CancellationToken ct) =>
        await Database.SqlQuery<int>($"""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM pay.Decrees WHERE EmployeeId = {employeeId})
                          OR EXISTS (SELECT 1 FROM pay.Attendance WHERE EmployeeId = {employeeId}) THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct) == 1;

    void IPersonnelDbContext.Add<T>(T entity) => Set<T>().Add(entity);
    void IPersonnelDbContext.Remove<T>(T entity) => Set<T>().Remove(entity);
    Task<int> IPersonnelDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Job>(e =>
        {
            e.ToTable("Jobs", "hr");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.InsuranceJobCode).HasMaxLength(6).IsUnicode(false);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<Workshop>(e =>
        {
            e.ToTable("Workshops", "hr");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.ContractRow).HasMaxLength(3).IsUnicode(false);
            e.Property(x => x.EmployeeRate).HasPrecision(5, 2);
            e.Property(x => x.EmployerRate).HasPrecision(5, 2);
            e.Property(x => x.UnemploymentRate).HasPrecision(5, 2);
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<Employee>(e =>
        {
            e.ToTable("Employees", "hr", t => t.HasTrigger("trg_Employees_Person"));
            e.HasKey(x => x.Id);
            e.Property(x => x.PersonnelNo).HasMaxLength(20);
            e.Property(x => x.InsuranceNo).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.Iban).HasMaxLength(26).IsUnicode(false);
            e.Property(x => x.TaxExemptPercent).HasPrecision(5, 2);
            e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            e.Ignore(x => x.DomainEvents);
        });
        b.Entity<PersonRef>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToSqlQuery("""
                SELECT p.Id, p.DisplayName, p.NationalId, p.IsActive,
                       CAST(CASE WHEN p.PersonKind = 1 THEN 1 ELSE 0 END AS BIT) AS IsNatural,
                       CAST(CASE WHEN EXISTS (SELECT 1 FROM base.PersonRoles r JOIN base.PersonRoleTypes t ON t.Id = r.RoleId
                                              WHERE r.PersonId = p.Id AND t.Code = 'employee') THEN 1 ELSE 0 END AS BIT) AS IsEmployee
                FROM base.Persons p
                """);
        });
        b.Entity<CenterRef>(e =>
        {
            e.ToTable("CostCenters", "cost", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });
    }
}

public static class DependencyInjection
{
    /// <summary>ثبت ماژول کارگزینی پایه (مشاغل، کارگاه‌ها، پرونده پرسنلی).</summary>
    public static IServiceCollection AddPersonnelModule(this IServiceCollection services)
    {
        services.AddPersonnelApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<PersonnelDbContext>((sp, options) => options.UseCompanyDatabase(sp));
        services.AddScoped<IPersonnelDbContext>(sp => sp.GetRequiredService<PersonnelDbContext>());
        return services;
    }
}
