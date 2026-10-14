using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Reports;

/// <summary>
/// گزارش‌های پایه: تراز آزمایشی (گروه، کل، معین، تفصیلی) و دفتر حساب با مانده جاری.
/// فقط اسناد موقت و دائم در گزارش می‌آیند (پیش‌نویس و باطل هرگز).
/// جمع‌زدن در دیتابیس انجام می‌شود و فقط نتیجه گروه‌بندی‌شده به برنامه می‌آید.
/// </summary>
public class ReportService(IAccountingDbContext db)
{
    public async Task<TrialBalanceDto> TrialBalanceAsync(TrialBalanceRequest request, CancellationToken ct)
    {
        var range = await ResolveRangeAsync(request.Filter, ct);
        var chart = await LoadChartAsync(ct);
        var scope = ResolveScope(chart, request.AccountCode);
        var postings = Postings(request.Filter, range, scope, request.DetailId);

        var rows = request.Level == TrialBalanceLevel.Detail
            ? await DetailRowsAsync(postings, range, request, ct)
            : await AccountRowsAsync(postings, range, chart, scope, request, ct);

        rows = rows.OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
        if (request.HideZero)
            rows = rows.Where(r => r.TotalDebit != 0 || r.TotalCredit != 0 || r.OpeningDebit != 0 || r.OpeningCredit != 0).ToList();

        var totals = new TrialBalanceRow(request.Level, "", "جمع", null, null, null,
            rows.Sum(r => r.OpeningDebit), rows.Sum(r => r.OpeningCredit),
            rows.Sum(r => r.PeriodDebit), rows.Sum(r => r.PeriodCredit),
            rows.Sum(r => r.TotalDebit), rows.Sum(r => r.TotalCredit),
            rows.Sum(r => r.EndingDebit), rows.Sum(r => r.EndingCredit));

        return new TrialBalanceDto(request.Level, PersianDate.ToPersian(range.From), PersianDate.ToPersian(range.To), rows, totals);
    }

    public async Task<LedgerDto> LedgerAsync(LedgerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AccountCode) && request.DetailId is null)
            throw new DomainException("برای دفتر، حساب یا تفصیلی را انتخاب کنید.");

        var range = await ResolveRangeAsync(request.Filter, ct);
        var chart = await LoadChartAsync(ct);
        var scope = ResolveScope(chart, request.AccountCode);
        var postings = Postings(request.Filter, range, scope, request.DetailId);

        var opening = await postings.Where(p => p.Voucher.VoucherDate < range.From)
            .SumAsync(p => (decimal?)(p.Line.Debit - p.Line.Credit), ct) ?? 0;

        var lines = await postings.Where(p => p.Voucher.VoucherDate >= range.From)
            .OrderBy(p => p.Voucher.VoucherDate)
            .ThenBy(p => p.Voucher.VoucherNo == null) // در هر روز اول اسناد دائم، بعد موقت
            .ThenBy(p => p.Voucher.VoucherNo)
            .ThenBy(p => p.Voucher.TempNo)
            .ThenBy(p => p.Line.RowNo)
            .Select(p => new
            {
                p.Voucher.Id, p.Voucher.VoucherDate, p.Voucher.TempNo, p.Voucher.VoucherNo, p.Voucher.Status,
                p.Voucher.Description,
                p.Line.RowNo, p.Line.AccountId,
                p.Line.Detail1Id, p.Line.Detail2Id, p.Line.Detail3Id, p.Line.Detail4Id, p.Line.Detail5Id, p.Line.Detail6Id,
                LineDescription = p.Line.Description,
                p.Line.Debit, p.Line.Credit
            })
            .ToListAsync(ct);

        var detailIds = lines.SelectMany(l => new[] { l.Detail1Id, l.Detail2Id, l.Detail3Id, l.Detail4Id, l.Detail5Id, l.Detail6Id })
            .OfType<int>().Append(request.DetailId ?? 0).Where(id => id != 0).Distinct().ToList();
        var details = await db.DetailAccounts.AsNoTracking().Where(d => detailIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => $"{d.Code} {d.Title}", ct);

        var balance = opening;
        var rows = new List<LedgerRow>(lines.Count);
        foreach (var l in lines)
        {
            balance += l.Debit - l.Credit;
            var account = chart[l.AccountId];
            var lineDetails = new[] { l.Detail1Id, l.Detail2Id, l.Detail3Id, l.Detail4Id, l.Detail5Id, l.Detail6Id }
                .OfType<int>().Select(id => details.GetValueOrDefault(id, "?")).ToList();
            rows.Add(new LedgerRow(l.Id, PersianDate.ToPersian(l.VoucherDate), l.TempNo, l.VoucherNo, l.Status, l.RowNo,
                account.Code, account.Title, lineDetails.Count == 0 ? null : string.Join(" / ", lineDetails),
                l.LineDescription ?? l.Description, l.Debit, l.Credit, balance));
        }

        var titleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.AccountCode))
        {
            var account = chart.Values.First(a => a.Code == request.AccountCode.Trim());
            titleParts.Add($"{Level(account.Level)} {account.Code} - {account.Title}");
        }
        if (request.DetailId is int detailId)
            titleParts.Add($"تفصیلی {details.GetValueOrDefault(detailId) ?? throw new DomainException("تفصیلی انتخاب‌شده وجود ندارد.")}");

        return new LedgerDto(string.Join("، ", titleParts), PersianDate.ToPersian(range.From), PersianDate.ToPersian(range.To),
            opening, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), balance);
    }

    // ------------------------------------------------------------------ تراز در سطح حساب

    private static async Task<List<TrialBalanceRow>> AccountRowsAsync(IQueryable<Posting> postings, DateRange range,
        Dictionary<int, ChartAccount> chart, HashSet<int>? scope, TrialBalanceRequest request, CancellationToken ct)
    {
        var buckets = await postings
            .GroupBy(p => new { p.Line.AccountId, Prior = p.Voucher.VoucherDate < range.From || p.Voucher.Type == VoucherType.Opening })
            .Select(g => new Bucket(g.Key.AccountId, g.Key.Prior, g.Sum(x => x.Line.Debit), g.Sum(x => x.Line.Credit)))
            .ToListAsync(ct);

        var target = request.Level switch
        {
            TrialBalanceLevel.Group => AccountLevel.Group,
            TrialBalanceLevel.General => AccountLevel.General,
            _ => AccountLevel.Subsidiary
        };

        var sums = new Dictionary<int, Sums>();
        foreach (var b in buckets)
        {
            var key = AncestorAt(chart, b.Key, target);
            sums.TryGetValue(key, out var s);
            sums[key] = s.Add(b);
        }

        if (!request.HideZero)
        {
            // حساب‌های بدون گردش هم نمایش داده شوند
            foreach (var a in chart.Values.Where(a => a.Level == target && a.IsActive))
                if (scope is null || chart.Values.Any(s => s.Level == AccountLevel.Subsidiary && scope.Contains(s.Id) && AncestorAt(chart, s.Id, target) == a.Id))
                    sums.TryAdd(a.Id, default);
        }

        return sums.Select(kv =>
        {
            var a = chart[kv.Key];
            return kv.Value.ToRow(request.Level, a.Code, a.Title, a.Id, null, null);
        }).ToList();
    }

    private static int AncestorAt(Dictionary<int, ChartAccount> chart, int accountId, AccountLevel level)
    {
        var a = chart[accountId];
        while (a.Level > level && a.ParentId is int parent)
            a = chart[parent];
        return a.Id;
    }

    // ------------------------------------------------------------------ تراز تفصیلی

    private async Task<List<TrialBalanceRow>> DetailRowsAsync(IQueryable<Posting> postings, DateRange range,
        TrialBalanceRequest request, CancellationToken ct)
    {
        // تفصیلی شناور است و ممکن است در هر یک از شش ستون ردیف باشد؛ هر ستون جدا جمع زده می‌شود
        var sums = new Dictionary<int, Sums>();
        for (var level = 1; level <= Account.MaxDetailLevels; level++)
        {
            var column = $"Detail{level}Id";
            var buckets = await postings
                .Where(p => EF.Property<int?>(p.Line, column) != null)
                .GroupBy(p => new
                {
                    DetailId = EF.Property<int?>(p.Line, column)!.Value,
                    Prior = p.Voucher.VoucherDate < range.From || p.Voucher.Type == VoucherType.Opening
                })
                .Select(g => new Bucket(g.Key.DetailId, g.Key.Prior, g.Sum(x => x.Line.Debit), g.Sum(x => x.Line.Credit)))
                .ToListAsync(ct);

            foreach (var b in buckets)
            {
                sums.TryGetValue(b.Key, out var s);
                sums[b.Key] = s.Add(b);
            }
        }

        var detailQuery = db.DetailAccounts.AsNoTracking();
        if (request.DetailTypeId is short typeId)
            detailQuery = detailQuery.Where(d => d.DetailTypeId == typeId);
        if (request.HideZero || request.DetailTypeId is null)
        {
            var ids = sums.Keys.ToList();
            detailQuery = detailQuery.Where(d => ids.Contains(d.Id));
        }

        var types = await db.DetailTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var details = await detailQuery.Select(d => new { d.Id, d.Code, d.Title, d.DetailTypeId }).ToListAsync(ct);

        return details.Select(d => sums.GetValueOrDefault(d.Id)
                .ToRow(TrialBalanceLevel.Detail, d.Code, d.Title, null, d.Id, types.GetValueOrDefault(d.DetailTypeId)))
            .ToList();
    }

    // ------------------------------------------------------------------ پایه مشترک

    private IQueryable<Posting> Postings(ReportFilter filter, DateRange range, HashSet<int>? scope, int? detailId)
    {
        var query =
            from l in db.VoucherLines.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on l.VoucherId equals v.Id
            where v.FiscalYearId == filter.FiscalYearId
                  && (v.Status == VoucherStatus.Permanent || (filter.IncludeTemporary && v.Status == VoucherStatus.Temporary))
                  && v.VoucherDate <= range.To
            select new Posting { Line = l, Voucher = v };

        if (filter.ExcludeClosing)
            query = query.Where(p => p.Voucher.Type != VoucherType.ClosingTemporary && p.Voucher.Type != VoucherType.Closing);
        if (filter.FromVoucherNo is int fromNo)
            query = query.Where(p => p.Voucher.VoucherNo >= fromNo);
        if (filter.ToVoucherNo is int toNo)
            query = query.Where(p => p.Voucher.VoucherNo <= toNo);
        if (scope is not null)
        {
            var ids = scope.ToList();
            query = query.Where(p => ids.Contains(p.Line.AccountId));
        }
        if (detailId is int d)
            query = query.Where(p => p.Line.Detail1Id == d || p.Line.Detail2Id == d || p.Line.Detail3Id == d
                                     || p.Line.Detail4Id == d || p.Line.Detail5Id == d || p.Line.Detail6Id == d);
        return query;
    }

    private async Task<DateRange> ResolveRangeAsync(ReportFilter filter, CancellationToken ct)
    {
        var year = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId, ct)
                   ?? throw new NotFoundException("سال مالی پیدا نشد.");

        var from = ParseDate(filter.FromDate, "از تاریخ") ?? year.StartDate;
        var to = ParseDate(filter.ToDate, "تا تاریخ") ?? year.EndDate;
        if (from < year.StartDate || to > year.EndDate)
            throw new DomainException($"بازه گزارش باید داخل سال مالی {year.Title} باشد.");
        if (from > to)
            throw new DomainException("«از تاریخ» بعد از «تا تاریخ» است.");
        if (filter.FromVoucherNo > filter.ToVoucherNo)
            throw new DomainException("«از شماره سند» بزرگ‌تر از «تا شماره سند» است.");
        return new DateRange(from, to);
    }

    private static DateOnly? ParseDate(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = Erp.SharedKernel.Text.PersianText.Normalize(value);
        return PersianDate.TryParse(normalized, out var date)
            ? date
            : throw new DomainException($"{label} نامعتبر است: {value}");
    }

    private async Task<Dictionary<int, ChartAccount>> LoadChartAsync(CancellationToken ct) =>
        await db.Accounts.AsNoTracking()
            .Select(a => new ChartAccount(a.Id, a.ParentId, a.Level, a.Code, a.Title, a.IsActive))
            .ToDictionaryAsync(a => a.Id, ct);

    /// <summary>شناسه معین‌های زیرمجموعه یک حساب؛ null یعنی بدون محدودیت.</summary>
    private static HashSet<int>? ResolveScope(Dictionary<int, ChartAccount> chart, string? accountCode)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return null;
        var code = accountCode.Trim();
        var root = chart.Values.FirstOrDefault(a => a.Code == code)
                   ?? throw new DomainException($"حساب با کد {code} وجود ندارد.");

        return chart.Values
            .Where(a => a.Level == AccountLevel.Subsidiary && AncestorAt(chart, a.Id, root.Level) == root.Id)
            .Select(a => a.Id)
            .ToHashSet();
    }

    private static string Level(AccountLevel level) => level switch
    {
        AccountLevel.Group => "گروه",
        AccountLevel.General => "کل",
        _ => "معین"
    };

    private sealed class Posting
    {
        public VoucherLine Line { get; init; } = null!;
        public Voucher Voucher { get; init; } = null!;
    }

    private sealed record ChartAccount(int Id, int? ParentId, AccountLevel Level, string Code, string Title, bool IsActive);

    private readonly record struct DateRange(DateOnly From, DateOnly To);

    private sealed record Bucket(int Key, bool Prior, decimal Debit, decimal Credit);

    private readonly record struct Sums(decimal PriorDebit, decimal PriorCredit, decimal PeriodDebit, decimal PeriodCredit)
    {
        public Sums Add(Bucket b) => b.Prior
            ? this with { PriorDebit = PriorDebit + b.Debit, PriorCredit = PriorCredit + b.Credit }
            : this with { PeriodDebit = PeriodDebit + b.Debit, PeriodCredit = PeriodCredit + b.Credit };

        public TrialBalanceRow ToRow(TrialBalanceLevel level, string code, string title, int? accountId, int? detailId, string? typeTitle)
        {
            var opening = PriorDebit - PriorCredit;
            var totalDebit = PriorDebit + PeriodDebit;
            var totalCredit = PriorCredit + PeriodCredit;
            var ending = totalDebit - totalCredit;
            return new TrialBalanceRow(level, code, title, accountId, detailId, typeTitle,
                Math.Max(opening, 0), Math.Max(-opening, 0),
                PeriodDebit, PeriodCredit,
                totalDebit, totalCredit,
                Math.Max(ending, 0), Math.Max(-ending, 0));
        }
    }
}
