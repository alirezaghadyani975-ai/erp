using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Vouchers;

/// <param name="Temporary">اسناد موقت تا تاریخ انتخابی (آماده دائم شدن).</param>
/// <param name="OutOfOrder">اسناد دائمی که شماره‌شان با ترتیب تاریخ نمی‌خواند.</param>
public sealed record NumberingStatusDto(short YearId, string YearTitle, bool YearOpen, int Permanent, int Temporary, int OutOfOrder,
    int? FirstOutOfOrderNo);

/// <summary>
/// ترتیب شماره‌های قطعی: دائم کردن گروهی به ترتیب تاریخ، و شماره‌گذاری مجدد اسناد دائم (فقط در سال باز) تا شماره قطعی
/// با ترتیب تاریخ بخواند — پیش از تحریر دفاتر قانونی. ترتیب: افتتاحیه، بعد تاریخ، در هر روز اسناد عادی و آخر بستن و اختتامیه.
/// </summary>
public class VoucherNumberingService(IAccountingDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock, IUnitOfWork uow)
{
    public async Task<NumberingStatusDto> StatusAsync(short yearId, string? toDate, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherView);
        var year = await YearAsync(yearId, false, ct);
        var to = Date(toDate) ?? year.EndDate;
        var permanent = await db.Vouchers.AsNoTracking().Where(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Permanent)
            .Select(v => new Row(v.Id, v.VoucherNo!.Value, v.VoucherDate, v.Type)).ToListAsync(ct);
        var moved = Plan(permanent).Where(x => x.New != x.Row.No).Select(x => x.Row.No).ToList();
        var temporary = await db.Vouchers.CountAsync(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Temporary
            && v.VoucherDate <= to && !(v.SourceModule == Closing.ClosingService.Module && v.Type != VoucherType.Opening), ct);
        return new NumberingStatusDto(year.Id, year.Title, year.Status == PeriodStatus.Open, permanent.Count, temporary, moved.Count,
            moved.Count == 0 ? null : moved.Min());
    }

    /// <summary>
    /// دائم کردن همه اسناد موقت تا تاریخ، به ترتیب تاریخ (تا شماره قطعی به ترتیب تاریخ داده شود).
    /// اسناد بستن و اختتامیه از اینجا دائم نمی‌شوند (با «قطعی کردن بستن سال»)؛ افتتاحیه حاصل از بستن سال قبل دائم می‌شود.
    /// </summary>
    public async Task<int> MakePermanentAsync(short yearId, string? toDate, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherPermanent);
        var year = await YearAsync(yearId, true, ct);
        var to = Date(toDate) ?? year.EndDate;
        var query = db.Vouchers.Include(v => v.Lines).Where(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Temporary
            && v.VoucherDate <= to && !(v.SourceModule == Closing.ClosingService.Module && v.Type != VoucherType.Opening));
        if (access.OwnDocsOnly) query = query.Where(v => v.CreatedBy == access.UserId);
        var vouchers = await query.ToListAsync(ct);
        if (vouchers.Count == 0) return 0;

        await using var tx = await uow.BeginAsync(ct);
        var lastNo = await db.Vouchers.Where(v => v.FiscalYearId == year.Id).MaxAsync(v => v.VoucherNo, ct) ?? 0;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var v in vouchers.OrderBy(v => v.VoucherDate).ThenBy(v => TypeOrder(v.Type)).ThenBy(v => v.TempNo))
            v.MarkPermanent(year, ++lastNo, user.UserId, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return vouchers.Count;
    }

    /// <summary>شماره‌گذاری مجدد اسناد دائم سال به ترتیب تاریخ. خروجی: تعداد اسنادی که شماره‌شان عوض شد.</summary>
    public async Task<int> RenumberAsync(short yearId, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherRenumber);
        var year = await YearAsync(yearId, true, ct);
        if (year.Status != PeriodStatus.Open)
            throw new DomainException($"سال مالی {year.Title} باز نیست؛ شماره اسناد سال قفل یا بسته عوض نمی‌شود.");
        var vouchers = await db.Vouchers.Where(v => v.FiscalYearId == year.Id && v.Status == VoucherStatus.Permanent).ToListAsync(ct);
        var plan = Plan(vouchers.Select(v => new Row(v.Id, v.VoucherNo!.Value, v.VoucherDate, v.Type)).ToList())
            .Where(x => x.New != x.Row.No).ToList();
        if (plan.Count == 0) return 0;

        var byId = vouchers.ToDictionary(v => v.Id);
        await using var tx = await uow.BeginAsync(ct);
        // دو مرحله: اول شماره منفی موقت تا یکتایی شماره در هیچ لحظه‌ای نشکند
        foreach (var x in plan) byId[x.Row.Id].Renumber(-x.New);
        await db.SaveChangesAsync(ct);
        foreach (var x in plan) byId[x.Row.Id].Renumber(x.New);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return plan.Count;
    }

    private sealed record Row(long Id, int No, DateOnly Date, VoucherType Type);

    private static List<(Row Row, int New)> Plan(List<Row> rows) =>
        rows.OrderBy(r => r.Type == VoucherType.Opening ? 0 : 1).ThenBy(r => r.Date).ThenBy(r => TypeOrder(r.Type)).ThenBy(r => r.No)
            .Select((r, i) => (r, i + 1)).ToList();

    /// <summary>در یک روز: افتتاحیه، عادی، بستن حساب‌های موقت، اختتامیه.</summary>
    private static int TypeOrder(VoucherType t) => t switch
    {
        VoucherType.Opening => 0,
        VoucherType.Normal => 1,
        VoucherType.ClosingTemporary => 2,
        _ => 3
    };

    private async Task<FiscalYear> YearAsync(short id, bool tracking, CancellationToken ct)
    {
        access.DemandScope(ScopeKind.FiscalYear, id);
        var q = db.FiscalYears.Include(f => f.Periods).AsQueryable();
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("سال مالی پیدا نشد.");
    }

    private static DateOnly? Date(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : PersianDate.TryParse(PersianText.Normalize(value), out var d) ? d : throw new DomainException($"تاریخ نامعتبر است: {value}");
}
