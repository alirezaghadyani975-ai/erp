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
    /// <summary>فقط‌خواندنی: نقش اشخاص (مالک: ماژول اطلاعات پایه) برای فیلتر فهرست تفصیلی.</summary>
    IQueryable<PersonRoleLink> PersonRoleLinks { get; }

    /// <summary>فقط‌خواندنی: انواع نقش (مالک: ماژول اطلاعات پایه).</summary>
    IQueryable<RoleTypeRef> RoleTypeRefs { get; }
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

/// <summary>نقش یک شخص (base.PersonRoles)؛ نمای فقط‌خواندنی در حسابداری.</summary>
public class PersonRoleLink
{
    public int PersonId { get; private set; }
    public short RoleId { get; private set; }
}

/// <summary>نوع نقش (base.PersonRoleTypes)؛ نمای فقط‌خواندنی در حسابداری.</summary>
public class RoleTypeRef
{
    public short Id { get; private set; }
    public string Title { get; private set; } = null!;
    public bool IsActive { get; private set; }
}
