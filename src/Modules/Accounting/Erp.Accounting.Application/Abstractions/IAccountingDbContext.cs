using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Erp.Accounting.Application.Abstractions;

public interface IAccountingDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<DetailType> DetailTypes { get; }
    DbSet<DetailAccount> DetailAccounts { get; }
    DbSet<Person> Persons { get; }
    DbSet<PersonRoleType> PersonRoleTypes { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<Unit> Units { get; }
    DbSet<FiscalYear> FiscalYears { get; }
    DbSet<Voucher> Vouchers { get; }
    DbSet<VoucherLine> VoucherLines { get; }

    DatabaseFacade Database { get; }

    /// <summary>ساخت سال مالی و ۱۲ دوره‌اش با روال fin.usp_CreateFiscalYear.</summary>
    Task<short> CreateFiscalYearAsync(int persianYear, byte startMonth, CancellationToken ct);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
