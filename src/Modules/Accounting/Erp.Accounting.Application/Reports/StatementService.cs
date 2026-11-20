using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Reports;

public enum StatementRowKind : byte { Heading = 1, Line = 2, Subtotal = 3, Total = 4 }

/// <summary>یک سطر صورت مالی. مبلغ منفی در صورت مالی داخل پرانتز نمایش داده می‌شود.</summary>
public sealed record StatementRow(StatementRowKind Kind, string? Code, string Title, decimal? Current, decimal? Compare);

/// <param name="Difference">ترازنامه: دارایی‌ها منهای (حقوق مالکانه + بدهی‌ها)؛ باید صفر باشد.</param>
public sealed record StatementDto(string Title, string CurrentLabel, string? CompareLabel, List<StatementRow> Rows, decimal Difference,
    List<string> Notes);

/// <summary>
/// صورت‌های مالی از روی کدینگ استاندارد: ترازنامه (صورت وضعیت مالی) و صورت سود و زیان در سطح حساب کل.
/// گروه‌های ۱ و ۲ دارایی، ۳ حقوق مالکانه، ۴ و ۵ بدهی؛ ۶، ۷ و ۸ سود و زیانی؛ ۹ انتظامی (در صورت‌ها نمی‌آید).
/// اسناد بستن و اختتامیه حساب نمی‌شوند؛ سود و زیان بسته‌نشده در ترازنامه سطر «سود (زیان) دوره» است.
/// </summary>
public class StatementService(IAccountingDbContext db, IAccessControl access)
{
    private static readonly (string Code, string Title, int Sign)[] BalanceGroups =
    [
        ("1", "دارایی‌های غیرجاری", 1), ("2", "دارایی‌های جاری", 1),
        ("3", "حقوق مالکانه", -1), ("4", "بدهی‌های غیرجاری", -1), ("5", "بدهی‌های جاری", -1)
    ];

    /// <summary>ترازنامه در «تا تاریخ» فیلتر، در مقایسه با مانده «از تاریخ» (پیش‌فرض: ابتدای سال، یعنی افتتاحیه).</summary>
    public async Task<StatementDto> BalanceSheetAsync(ReportFilter filter, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.Statements);
        var (year, from, to) = await RangeAsync(filter, ct);
        var chart = await ChartAsync(ct);
        var sums = await SumsAsync(year.Id, from, to, filter.IncludeTemporary, ct);

        // بر اساس کل: Current = مانده تا تاریخ، Compare = مانده قبل از «از تاریخ» (با افتتاحیه)
        var byGeneral = new Dictionary<int, (decimal Current, decimal Compare)>();
        decimal profitNow = 0, profitBefore = 0;
        foreach (var s in sums)
        {
            var account = chart[s.AccountId];
            var general = account.GeneralId;
            var group = chart[general].GroupCode;
            if (group is "6" or "7" or "8")
            {
                profitNow -= s.Net;
                if (s.Prior) profitBefore -= s.Net;
                continue;
            }
            byGeneral.TryGetValue(general, out var v);
            byGeneral[general] = (v.Current + s.Net, v.Compare + (s.Prior ? s.Net : 0));
        }

        var rows = new List<StatementRow> { new(StatementRowKind.Heading, null, "دارایی‌ها", null, null) };
        decimal assetsNow = 0, assetsBefore = 0, claimsNow = 0, claimsBefore = 0, liabNow = 0, liabBefore = 0;
        foreach (var (code, title, sign) in BalanceGroups)
        {
            if (code == "3")
            {
                rows.Add(new(StatementRowKind.Total, null, "جمع دارایی‌ها", assetsNow, assetsBefore));
                rows.Add(new(StatementRowKind.Heading, null, "حقوق مالکانه و بدهی‌ها", null, null));
            }
            var start = rows.Count;
            rows.Add(new(StatementRowKind.Heading, code, title, null, null));
            decimal now = 0, before = 0;
            foreach (var (generalId, v) in byGeneral.Where(x => chart[x.Key].GroupCode == code).OrderBy(x => chart[x.Key].Code, StringComparer.Ordinal))
            {
                if (v.Current == 0 && v.Compare == 0) continue;
                rows.Add(new(StatementRowKind.Line, chart[generalId].Code, chart[generalId].Title, sign * v.Current, sign * v.Compare));
                now += sign * v.Current;
                before += sign * v.Compare;
            }
            if (code == "3" && (profitNow != 0 || profitBefore != 0))
            {
                rows.Add(new(StatementRowKind.Line, null, "سود (زیان) دوره (بسته‌نشده)", profitNow, profitBefore));
                now += profitNow;
                before += profitBefore;
            }
            if (rows.Count == start + 1) rows.RemoveAt(start); // گروه بدون مانده نمایش داده نمی‌شود
            else rows.Add(new(StatementRowKind.Subtotal, null, $"جمع {title}", now, before));
            if (sign > 0) { assetsNow += now; assetsBefore += before; }
            else
            {
                claimsNow += now; claimsBefore += before;
                if (code != "3") { liabNow += now; liabBefore += before; }
            }
        }
        rows.Add(new(StatementRowKind.Total, null, "جمع بدهی‌ها", liabNow, liabBefore));
        rows.Add(new(StatementRowKind.Total, null, "جمع حقوق مالکانه و بدهی‌ها", claimsNow, claimsBefore));

        var notes = new List<string>();
        var difference = assetsNow - claimsNow;
        if (difference != 0)
            notes.Add($"ترازنامه {Amount(difference)} ریال اختلاف دارد؛ معمولاً یعنی ردیفی در حساب‌های گروه ۹ (مثلاً «تراز افتتاحیه و اختتامیه») یا گروهی خارج از کدینگ استاندارد ثبت شده است.");
        if (profitNow != 0)
            notes.Add("سود (زیان) دوره هنوز با سند بستن حساب‌های موقت به حقوق مالکانه منتقل نشده و جدا آمده است.");

        return new StatementDto($"ترازنامه (صورت وضعیت مالی) در {PersianDate.ToPersian(to)}", PersianDate.ToPersian(to),
            from == year.StartDate ? "ابتدای سال" : PersianDate.ToPersian(from.AddDays(-1)), rows, difference, notes);
    }

    /// <summary>صورت سود و زیان بازه فیلتر، در مقایسه با کل سال مالی قبل (اگر هست).</summary>
    public async Task<StatementDto> IncomeStatementAsync(ReportFilter filter, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.Statements);
        var (year, from, to) = await RangeAsync(filter, ct);
        var chart = await ChartAsync(ct);
        var current = Profit(await SumsAsync(year.Id, from, to, filter.IncludeTemporary, ct), chart);

        Dictionary<int, decimal> compare = [];
        string? compareLabel = null;
        var prevEnd = year.StartDate.AddDays(-1);
        var previous = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.EndDate == prevEnd, ct);
        if (previous is not null && access.CanUse(ScopeKind.FiscalYear, previous.Id))
        {
            compare = Profit(await SumsAsync(previous.Id, previous.StartDate, previous.EndDate, filter.IncludeTemporary, ct), chart);
            compareLabel = $"سال {previous.Title}";
        }

        var rows = new List<StatementRow>();
        var used = new HashSet<int>();
        (decimal Now, decimal Before) Section(string title, Func<GeneralRow, bool> match, bool showHeading = true)
        {
            var generals = chart.Values.OfType<GeneralRow>().Where(g => !used.Contains(g.Id) && match(g))
                .OrderBy(g => g.Code, StringComparer.Ordinal).ToList();
            decimal now = 0, before = 0;
            var lines = new List<StatementRow>();
            foreach (var g in generals)
            {
                used.Add(g.Id);
                var c = current.GetValueOrDefault(g.Id);
                var p = compare.GetValueOrDefault(g.Id);
                if (c == 0 && p == 0) continue;
                lines.Add(new(StatementRowKind.Line, g.Code, g.Title, c, compareLabel is null ? null : p));
                now += c;
                before += p;
            }
            if (lines.Count == 0) return (0, 0);
            if (showHeading && lines.Count > 1)
            {
                rows.Add(new(StatementRowKind.Heading, null, title, null, null));
                rows.AddRange(lines);
                rows.Add(new(StatementRowKind.Subtotal, null, $"جمع {title}", now, compareLabel is null ? null : before));
            }
            else rows.AddRange(lines);
            return (now, before);
        }
        StatementRow Total(string title, decimal now, decimal before) =>
            new(StatementRowKind.Total, null, title, now, compareLabel is null ? null : before);

        var revenue = Section("درآمدهای عملیاتی", g => g.Code is "60" or "61" or "62" or "63");
        var cost = Section("بهای تمام‌شده درآمدهای عملیاتی", g => g.GroupCode == "7");
        var gross = (revenue.Now + cost.Now, revenue.Before + cost.Before);
        rows.Add(Total("سود (زیان) ناخالص", gross.Item1, gross.Item2));
        var expenses = Section("هزینه‌های عملیاتی", g => g.GroupCode == "8" && g.Code != "89");
        var otherOp = Section("سایر درآمدها و هزینه‌های عملیاتی", g => g.Code == "64");
        var operating = (gross.Item1 + expenses.Now + otherOp.Now, gross.Item2 + expenses.Before + otherOp.Before);
        rows.Add(Total("سود (زیان) عملیاتی", operating.Item1, operating.Item2));
        var finance = Section("هزینه‌های مالی", g => g.Code == "89");
        var nonOp = Section("سایر درآمدها و هزینه‌های غیرعملیاتی", g => g.GroupCode == "6");
        var net = (operating.Item1 + finance.Now + nonOp.Now, operating.Item2 + finance.Before + nonOp.Before);
        rows.Add(Total("سود (زیان) خالص", net.Item1, net.Item2));

        return new StatementDto($"صورت سود و زیان از {PersianDate.ToPersian(from)} تا {PersianDate.ToPersian(to)}",
            $"{PersianDate.ToPersian(from)} تا {PersianDate.ToPersian(to)}", compareLabel, rows, 0,
            ["درآمدها مثبت و هزینه‌ها منفی (داخل پرانتز) نمایش داده می‌شوند. مالیات بر درآمد هنوز محاسبه و جدا نمی‌شود."]);
    }

    // ------------------------------------------------------------------

    private abstract record ChartRow(int Id, string Code, string Title, string GroupCode);
    private sealed record GeneralRow(int Id, string Code, string Title, string GroupCode) : ChartRow(Id, Code, Title, GroupCode);
    private sealed record OtherRow(int Id, string Code, string Title, string GroupCode, int GeneralId) : ChartRow(Id, Code, Title, GroupCode);

    private sealed class Chart(Dictionary<int, ChartRow> rows)
    {
        public IEnumerable<ChartRow> Values => rows.Values;
        public Node this[int id] => new(rows[id], this);
        public ChartRow Row(int id) => rows[id];
    }

    private readonly record struct Node(ChartRow Row, Chart Chart)
    {
        public int GeneralId => Row is OtherRow o ? o.GeneralId : Row.Id;
        public string GroupCode => Row.GroupCode;
        public string Code => Row.Code;
        public string Title => Row.Title;
    }

    private async Task<Chart> ChartAsync(CancellationToken ct)
    {
        var all = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.ParentId, a.Level, a.Code, a.Title }).ToListAsync(ct);
        var byId = all.ToDictionary(a => a.Id);
        var rows = new Dictionary<int, ChartRow>();
        foreach (var a in all)
        {
            var group = a;
            while (group.ParentId is int p && byId.TryGetValue(p, out var parent)) group = parent;
            if (a.Level == AccountLevel.General) rows[a.Id] = new GeneralRow(a.Id, a.Code, a.Title, group.Code);
            else
            {
                var general = a.Level == AccountLevel.Subsidiary && a.ParentId is int gp ? gp : a.Id;
                rows[a.Id] = new OtherRow(a.Id, a.Code, a.Title, group.Code, general);
            }
        }
        return new Chart(rows);
    }

    private sealed record Sum(int AccountId, bool Prior, decimal Net);

    private async Task<List<Sum>> SumsAsync(short yearId, DateOnly start, DateOnly end, bool includeTemporary, CancellationToken ct)
    {
        var rows = await (from l in db.VoucherLines.AsNoTracking()
                          join v in db.Vouchers.AsNoTracking() on l.VoucherId equals v.Id
                          where v.FiscalYearId == yearId && v.VoucherDate <= end
                                && (v.Status == VoucherStatus.Permanent || (includeTemporary && v.Status == VoucherStatus.Temporary))
                                && v.Type != VoucherType.ClosingTemporary && v.Type != VoucherType.Closing
                          group l by new { l.AccountId, Prior = v.VoucherDate < start || v.Type == VoucherType.Opening } into g
                          select new { g.Key.AccountId, g.Key.Prior, Net = g.Sum(x => x.Debit - x.Credit) })
            .ToListAsync(ct);
        return rows.Select(r => new Sum(r.AccountId, r.Prior, r.Net)).ToList();
    }

    /// <summary>سود و زیان دوره (بدون مانده‌های قبل از بازه) به تفکیک کل، بستانکار مثبت.</summary>
    private static Dictionary<int, decimal> Profit(List<Sum> sums, Chart chart)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var s in sums.Where(s => !s.Prior))
        {
            var node = chart[s.AccountId];
            if (node.GroupCode is not ("6" or "7" or "8")) continue;
            result[node.GeneralId] = result.GetValueOrDefault(node.GeneralId) - s.Net;
        }
        return result;
    }

    private async Task<(FiscalYear Year, DateOnly From, DateOnly To)> RangeAsync(ReportFilter filter, CancellationToken ct)
    {
        access.DemandScope(ScopeKind.FiscalYear, filter.FiscalYearId);
        var year = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId, ct)
                   ?? throw new NotFoundException("سال مالی پیدا نشد.");
        DateOnly? Parse(string? value, string label) => string.IsNullOrWhiteSpace(value) ? null
            : PersianDate.TryParse(PersianText.Normalize(value), out var d) ? d : throw new DomainException($"{label} نامعتبر است: {value}");
        var from = Parse(filter.FromDate, "از تاریخ") ?? year.StartDate;
        var to = Parse(filter.ToDate, "تا تاریخ") ?? year.EndDate;
        if (from < year.StartDate || to > year.EndDate) throw new DomainException($"بازه باید داخل سال مالی {year.Title} باشد.");
        if (from > to) throw new DomainException("«از تاریخ» بعد از «تا تاریخ» است.");
        return (year, from, to);
    }

    private static string Amount(decimal v) => v.ToString("#,0.####", System.Globalization.CultureInfo.InvariantCulture);
}
