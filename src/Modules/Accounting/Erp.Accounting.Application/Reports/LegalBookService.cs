using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Reports;

/// <summary>یک ردیف دفتر روزنامه: جمع بدهکار یا بستانکار یک حساب کل در یک سند.</summary>
public sealed record JournalRow(int VoucherNo, string Date, string? VoucherDescription, string GeneralCode, string GeneralTitle,
    decimal Debit, decimal Credit);

public sealed record JournalDto(string FromDate, string ToDate, List<JournalRow> Rows, decimal TotalDebit, decimal TotalCredit, int VoucherCount);

/// <summary>یک ردیف دفتر کل: گردش یک حساب کل در یک سند، با مانده.</summary>
public sealed record GeneralLedgerRow(int VoucherNo, string Date, string? Description, decimal Debit, decimal Credit, decimal Balance);

public sealed record GeneralLedgerAccountDto(string Code, string Title, decimal Opening, List<GeneralLedgerRow> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Ending);

public sealed record GeneralLedgerDto(string FromDate, string ToDate, List<GeneralLedgerAccountDto> Accounts);

/// <summary>
/// دفاتر قانونی از اسناد دائم و به ترتیب شماره قطعی: دفتر روزنامه (هر سند به تفکیک حساب کل، اول بدهکارها بعد بستانکارها)
/// و دفتر کل (برای هر حساب کل، گردش هر سند با مانده). سند موقت و پیش‌نویس هرگز در دفاتر نمی‌آید.
/// </summary>
public class LegalBookService(IAccountingDbContext db, IAccessControl access)
{
    public const int MaxRows = 50_000;

    public async Task<JournalDto> JournalAsync(ReportFilter filter, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.Books);
        var (from, to) = await RangeAsync(filter, ct);
        var generals = await GeneralsAsync(ct);
        var rows = await Lines(filter, from, to)
            .Where(x => x.Voucher.VoucherDate >= from)
            .GroupBy(x => new { x.Voucher.Id, No = x.Voucher.VoucherNo!.Value, x.Voucher.VoucherDate, x.Voucher.Description, x.Line.AccountId })
            .Select(g => new { g.Key.Id, g.Key.No, g.Key.VoucherDate, g.Key.Description, g.Key.AccountId,
                Debit = g.Sum(x => x.Line.Debit), Credit = g.Sum(x => x.Line.Credit) })
            .ToListAsync(ct);
        if (rows.Count > MaxRows) throw new DomainException("بازه خیلی بزرگ است؛ دفتر را ماه به ماه بگیرید.");

        var result = new List<JournalRow>();
        foreach (var voucher in rows.GroupBy(r => (r.No, r.VoucherDate, r.Description)).OrderBy(g => g.Key.No))
        {
            // در هر سند ردیف‌های یک کل جمع می‌شوند؛ اول بدهکارها و بعد بستانکارها
            var byGeneral = voucher.GroupBy(r => generals[r.AccountId])
                .Select(g => (General: g.Key, Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit))).ToList();
            foreach (var (g, d, _) in byGeneral.Where(x => x.Debit > 0).OrderBy(x => x.General.Code, StringComparer.Ordinal))
                result.Add(new JournalRow(voucher.Key.No, PersianDate.ToPersian(voucher.Key.VoucherDate), voucher.Key.Description, g.Code, g.Title, d, 0));
            foreach (var (g, _, c) in byGeneral.Where(x => x.Credit > 0).OrderBy(x => x.General.Code, StringComparer.Ordinal))
                result.Add(new JournalRow(voucher.Key.No, PersianDate.ToPersian(voucher.Key.VoucherDate), voucher.Key.Description, g.Code, g.Title, 0, c));
        }
        return new JournalDto(PersianDate.ToPersian(from), PersianDate.ToPersian(to), result,
            result.Sum(r => r.Debit), result.Sum(r => r.Credit), result.Select(r => r.VoucherNo).Distinct().Count());
    }

    public async Task<GeneralLedgerDto> GeneralLedgerAsync(ReportFilter filter, string? fromCode, string? toCode, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.Books);
        var (from, to) = await RangeAsync(filter, ct);
        var generals = await GeneralsAsync(ct);
        var rows = await Lines(filter, from, to)
            .GroupBy(x => new { No = x.Voucher.VoucherNo!.Value, x.Voucher.VoucherDate, x.Voucher.Description, x.Line.AccountId,
                Prior = x.Voucher.VoucherDate < from })
            .Select(g => new { g.Key.No, g.Key.VoucherDate, g.Key.Description, g.Key.AccountId, g.Key.Prior,
                Debit = g.Sum(x => x.Line.Debit), Credit = g.Sum(x => x.Line.Credit) })
            .ToListAsync(ct);
        if (rows.Count > MaxRows) throw new DomainException("بازه خیلی بزرگ است؛ دفتر را ماه به ماه یا برای چند حساب بگیرید.");

        fromCode = PersianText.NormalizeOrNull(fromCode);
        toCode = PersianText.NormalizeOrNull(toCode);
        var accounts = new List<GeneralLedgerAccountDto>();
        foreach (var account in rows.GroupBy(r => generals[r.AccountId]).OrderBy(g => g.Key.Code, StringComparer.Ordinal))
        {
            var g = account.Key;
            if (fromCode is not null && string.CompareOrdinal(g.Code, fromCode) < 0) continue;
            if (toCode is not null && string.CompareOrdinal(g.Code, toCode) > 0) continue;
            var opening = account.Where(r => r.Prior).Sum(r => r.Debit - r.Credit);
            var balance = opening;
            var lines = new List<GeneralLedgerRow>();
            foreach (var v in account.Where(r => !r.Prior).GroupBy(r => (r.No, r.VoucherDate, r.Description)).OrderBy(x => x.Key.No))
            {
                var d = v.Sum(x => x.Debit);
                var c = v.Sum(x => x.Credit);
                balance += d - c;
                lines.Add(new GeneralLedgerRow(v.Key.No, PersianDate.ToPersian(v.Key.VoucherDate), v.Key.Description, d, c, balance));
            }
            if (lines.Count == 0 && opening == 0) continue;
            accounts.Add(new GeneralLedgerAccountDto(g.Code, g.Title, opening, lines, lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), balance));
        }
        return new GeneralLedgerDto(PersianDate.ToPersian(from), PersianDate.ToPersian(to), accounts);
    }

    // ------------------------------------------------------------------

    private sealed class Posting
    {
        public VoucherLine Line { get; init; } = null!;
        public Voucher Voucher { get; init; } = null!;
    }

    private IQueryable<Posting> Lines(ReportFilter filter, DateOnly from, DateOnly to)
    {
        var q = from l in db.VoucherLines.AsNoTracking()
                join v in db.Vouchers.AsNoTracking() on l.VoucherId equals v.Id
                where v.FiscalYearId == filter.FiscalYearId && v.Status == VoucherStatus.Permanent && v.VoucherDate <= to
                select new Posting { Line = l, Voucher = v };
        if (filter.ExcludeClosing)
            q = q.Where(p => p.Voucher.Type != VoucherType.ClosingTemporary && p.Voucher.Type != VoucherType.Closing);
        if (filter.FromVoucherNo is int a) q = q.Where(p => p.Voucher.VoucherNo >= a);
        if (filter.ToVoucherNo is int b) q = q.Where(p => p.Voucher.VoucherNo <= b);
        return q;
    }

    private sealed record General(string Code, string Title);

    /// <summary>برای هر معین، حساب کل آن.</summary>
    private async Task<Dictionary<int, General>> GeneralsAsync(CancellationToken ct)
    {
        var all = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.ParentId, a.Level, a.Code, a.Title }).ToDictionaryAsync(a => a.Id, ct);
        return all.Values.Where(a => a.Level == AccountLevel.Subsidiary).ToDictionary(a => a.Id, a =>
        {
            var g = a.ParentId is int p && all.TryGetValue(p, out var parent) ? parent : a;
            return new General(g.Code, g.Title);
        });
    }

    private async Task<(DateOnly From, DateOnly To)> RangeAsync(ReportFilter filter, CancellationToken ct)
    {
        access.DemandScope(ScopeKind.FiscalYear, filter.FiscalYearId);
        var year = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId, ct)
                   ?? throw new NotFoundException("سال مالی پیدا نشد.");
        DateOnly? Parse(string? v, string label) => string.IsNullOrWhiteSpace(v) ? null
            : PersianDate.TryParse(PersianText.Normalize(v), out var d) ? d : throw new DomainException($"{label} نامعتبر است: {v}");
        var from = Parse(filter.FromDate, "از تاریخ") ?? year.StartDate;
        var to = Parse(filter.ToDate, "تا تاریخ") ?? year.EndDate;
        if (from < year.StartDate || to > year.EndDate) throw new DomainException($"بازه باید داخل سال مالی {year.Title} باشد.");
        if (from > to) throw new DomainException("«از تاریخ» بعد از «تا تاریخ» است.");
        return (from, to);
    }
}
