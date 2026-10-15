using System.Globalization;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Reports;

/// <summary>یک کارت خلاصه صفحه نخست. Url برای رفتن به گزارش مربوط.</summary>
public sealed record KpiDto(string Key, string Title, decimal Amount, string Hint, string? Url);

public sealed record DashboardDto(
    string Today, short? FiscalYearId, string? FiscalYearTitle, string MonthTitle,
    int DraftCount, int TemporaryCount, List<KpiDto> Kpis, List<VoucherSummaryDto> RecentVouchers);

/// <summary>
/// داده‌های صفحه نخست از سندهای موقت و دائم سال مالی جاری. کارت‌ها بر پایه کد کل‌ها و گروه‌های کدینگ استاندارد
/// (اسکریپت ۰۹) تعریف شده‌اند؛ کد معین با کد کلش شروع می‌شود.
/// </summary>
public class DashboardService(IAccountingDbContext db, TimeProvider clock)
{
    private sealed record Kpi(string Key, string Title, string[] Prefixes, bool CreditPositive, bool ThisMonth, string Hint, string? Url);

    private static readonly Kpi[] Kpis =
    [
        new("cash", "موجودی نقد و بانک", ["28"], false, false, "کل ۲۸", "/reports/trial-balance"),
        new("receivable", "دریافتنی‌های تجاری", ["24"], false, false, "کل ۲۴", "/reports/trial-balance"),
        new("payable", "حساب‌ها و اسناد پرداختنی", ["50", "51"], true, false, "کل ۵۰ و ۵۱", "/reports/trial-balance"),
        new("revenue", "درآمد این ماه", ["6"], true, true, "گروه ۶", "/reports/trial-balance"),
        new("expense", "هزینه و بهای تمام‌شده این ماه", ["7", "8"], false, true, "گروه ۷ و ۸", "/reports/trial-balance"),
    ];

    private static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var calendar = new PersianCalendar();
        var dt = today.ToDateTime(TimeOnly.MinValue);
        var (year, month) = (calendar.GetYear(dt), calendar.GetMonth(dt));
        var monthStart = PersianDate.FromPersian(year, month, 1);
        var monthEnd = PersianDate.FromPersian(year, month, calendar.GetDaysInMonth(year, month));

        var fiscalYear = await db.FiscalYears.AsNoTracking()
                             .Where(f => f.StartDate <= today && f.EndDate >= today).Select(f => new { f.Id, f.Title }).FirstOrDefaultAsync(ct)
                         ?? await db.FiscalYears.AsNoTracking().OrderByDescending(f => f.StartDate).Select(f => new { f.Id, f.Title }).FirstOrDefaultAsync(ct);

        var kpis = new List<KpiDto>();
        int drafts = 0, temporaries = 0;
        var recent = new List<VoucherSummaryDto>();

        if (fiscalYear is not null)
        {
            var counts = await db.Vouchers.AsNoTracking().Where(v => v.FiscalYearId == fiscalYear.Id)
                .GroupBy(v => v.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            drafts = counts.FirstOrDefault(c => c.Status == VoucherStatus.Draft)?.Count ?? 0;
            temporaries = counts.FirstOrDefault(c => c.Status == VoucherStatus.Temporary)?.Count ?? 0;

            var posted = from l in db.VoucherLines.AsNoTracking()
                         join v in db.Vouchers on l.VoucherId equals v.Id
                         join a in db.Accounts on l.AccountId equals a.Id
                         where v.FiscalYearId == fiscalYear.Id
                               && (v.Status == VoucherStatus.Temporary || v.Status == VoucherStatus.Permanent)
                         select new { a.Code, v.VoucherDate, v.Type, l.Debit, l.Credit };

            foreach (var k in Kpis)
            {
                var (p0, p1) = (k.Prefixes[0], k.Prefixes.Length > 1 ? k.Prefixes[1] : k.Prefixes[0]);
                var q = posted.Where(x => x.Code.StartsWith(p0) || x.Code.StartsWith(p1));
                if (k.ThisMonth)
                    q = q.Where(x => x.VoucherDate >= monthStart && x.VoucherDate <= monthEnd
                                     && x.Type != VoucherType.ClosingTemporary && x.Type != VoucherType.Closing);
                var balance = await q.SumAsync(x => (decimal?)(x.Debit - x.Credit), ct) ?? 0;
                kpis.Add(new KpiDto(k.Key, k.Title, k.CreditPositive ? -balance : balance, k.Hint, k.Url));
            }

            var rows = await db.Vouchers.AsNoTracking().Where(v => v.FiscalYearId == fiscalYear.Id)
                .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Take(8)
                .Select(v => new { v.Id, v.TempNo, v.VoucherNo, v.VoucherDate, v.Description, v.Type, v.Status,
                    Total = v.Lines.Sum(l => l.Debit), v.SourceDocType })
                .ToListAsync(ct);
            recent = rows.Select(r => new VoucherSummaryDto(r.Id, r.TempNo, r.VoucherNo, PersianDate.ToPersian(r.VoucherDate),
                r.Description, r.Type, r.Status, r.Total, r.SourceDocType)).ToList();
        }

        return new DashboardDto(PersianDate.ToPersian(today), fiscalYear?.Id, fiscalYear?.Title,
            $"{MonthNames[month - 1]} {year}", drafts, temporaries, kpis, recent);
    }
}
