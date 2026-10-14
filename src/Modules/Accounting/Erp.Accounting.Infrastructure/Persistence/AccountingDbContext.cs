using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.Database;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Infrastructure.Persistence;

/// <summary>
/// نگاشت به جدول‌های موجود (schema های base و fin).
/// ساختار دیتابیس فقط با اسکریپت‌های پوشه database ساخته و تغییر می‌کند، نه با EF Migrations.
/// </summary>
public class AccountingDbContext : DbContext, IAccountingDbContext
{
    public AccountingDbContext(DbContextOptions<AccountingDbContext> options, CompanyConnection connection) : base(options) =>
        connection.Enlist((tx, ct) => Database.UseTransactionAsync(tx, ct), tx => Database.UseTransaction(tx));

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<DetailType> DetailTypes => Set<DetailType>();
    public DbSet<DetailAccount> DetailAccounts => Set<DetailAccount>();
    public IQueryable<PersonRoleLink> PersonRoleLinks => Set<PersonRoleLink>().AsNoTracking();
    public IQueryable<RoleTypeRef> RoleTypeRefs => Set<RoleTypeRef>().AsNoTracking();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherLine> VoucherLines => Set<VoucherLine>();

    public async Task<short> CreateFiscalYearAsync(int persianYear, byte startMonth, CancellationToken ct)
    {
        var rows = await Database
            .SqlQuery<CreatedFiscalYear>($"EXEC fin.usp_CreateFiscalYear @PersianYear = {(short)persianYear}, @StartMonth = {startMonth}")
            .ToListAsync(ct);
        return rows.Single().FiscalYearId;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountingDbContext).Assembly);

    private sealed record CreatedFiscalYear(short FiscalYearId, string Title, DateOnly StartDate, DateOnly EndDate);
}
