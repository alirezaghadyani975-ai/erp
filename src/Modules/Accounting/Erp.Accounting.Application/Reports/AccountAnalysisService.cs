using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Reports;

/// <summary>تراز مقایسه‌ای: مانده هر کل و معین در دو دوره (دو سال یا دو بازه) کنار هم.</summary>
public sealed record ComparativeRequest(ReportFilter Previous, ReportFilter Current, string? AccountCode = null);

/// <summary>تحلیل سنی مانده اشخاص یک معین تا یک تاریخ.</summary>
public sealed record AgingRequest(short FiscalYearId, string AccountCode, string? AsOf = null);

/// <summary>
/// تحلیل‌های حسابداری روی قالب مشترک گزارش (`ReportTable` با سطح دوم):
/// تراز مقایسه‌ای (کل ← معین) و تحلیل سنی مطالبات یا بدهی‌ها (طرف حساب ← اقلام باز).
/// </summary>
public class AccountAnalysisService(IAccountingDbContext db, ReportService reports, IAccessControl access, TimeProvider clock)
{
    /// <summary>بازه‌های سن (روز)؛ آخری «بیشتر از».</summary>
    public static readonly int[] AgeBuckets = [30, 60, 90, 180];

    public async Task<ReportTable> ComparativeAsync(ComparativeRequest r, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.TrialBalance);
        async Task<(TrialBalanceDto General, TrialBalanceDto Sub)> Load(ReportFilter f) =>
            (await reports.TrialBalanceAsync(new TrialBalanceRequest(f, TrialBalanceLevel.General, r.AccountCode, HideZero: false), ct),
             await reports.TrialBalanceAsync(new TrialBalanceRequest(f, TrialBalanceLevel.Subsidiary, r.AccountCode, HideZero: false), ct));
        var (pg, ps) = await Load(r.Previous);
        var (cg, cs) = await Load(r.Current);

        static Dictionary<string, (string Title, decimal Balance)> Map(TrialBalanceDto d) =>
            d.Rows.ToDictionary(x => x.Code, x => (x.Title, x.EndingDebit - x.EndingCredit));
        var (prevG, curG, prevS, curS) = (Map(pg), Map(cg), Map(ps), Map(cs));

        ReportRow Row(string code, string title, decimal prev, decimal cur, List<ReportRow>? children) => new(new Dictionary<string, object?>
        {
            ["code"] = code, ["title"] = title, ["previous"] = prev, ["current"] = cur, ["change"] = cur - prev,
            ["pct"] = ReportBuilder.Percent(cur - prev, Math.Abs(prev)),
        }, children);

        var rows = prevG.Keys.Union(curG.Keys).Order(StringComparer.Ordinal).Select(code =>
        {
            var title = curG.TryGetValue(code, out var c) ? c.Title : prevG[code].Title;
            var children = prevS.Keys.Union(curS.Keys).Where(s => s.StartsWith(code, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
                .Select(s => Row(s, curS.TryGetValue(s, out var x) ? x.Title : prevS[s].Title, prevS.GetValueOrDefault(s).Balance, curS.GetValueOrDefault(s).Balance, null))
                .Where(x => (decimal)x["previous"]! != 0 || (decimal)x["current"]! != 0).ToList();
            return Row(code, title, prevG.GetValueOrDefault(code).Balance, curG.GetValueOrDefault(code).Balance, children);
        }).Where(x => (decimal)x["previous"]! != 0 || (decimal)x["current"]! != 0).ToList();

        return new ReportTable("تراز مقایسه‌ای", [$"دوره ۱: {pg.FromDate} تا {pg.ToDate}", $"دوره ۲: {cg.FromDate} تا {cg.ToDate}",
                "مانده مثبت = بدهکار، منفی = بستانکار؛ تغییر = دوره ۲ − دوره ۱."],
            [new("code", "کد", ReportColumnKind.Code), new("title", "حساب کل / معین"),
             new("previous", "مانده دوره ۱", ReportColumnKind.Amount, Sum: false), new("current", "مانده دوره ۲", ReportColumnKind.Amount, Sum: false),
             new("change", "تغییر", ReportColumnKind.Amount, Sum: false), new("pct", "درصد تغییر", ReportColumnKind.Percent)],
            rows, false, "معین‌ها");
    }

    /// <summary>
    /// تحلیل سنی: برای هر تفصیلی سطح اول معین، گردش‌های طرف ماهیت (بدهکار برای مطالبات، بستانکار برای بدهی‌ها) اقلام باز می‌سازند
    /// و گردش طرف مقابل به ترتیب تاریخ از قدیمی‌ترین قلم کم می‌شود (FIFO). مانده هر قلم به سن (روز تا تاریخ گزارش) دسته‌بندی می‌شود.
    /// مانده افتتاحیه یک قلم به تاریخ سند افتتاحیه است. اسناد موقت و دائم؛ سند اختتامیه حساب نمی‌شود.
    /// </summary>
    public async Task<ReportTable> AgingAsync(AgingRequest r, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.Ledger);
        access.DemandScope(ScopeKind.FiscalYear, r.FiscalYearId);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == r.AccountCode && a.Level == AccountLevel.Subsidiary, ct)
            ?? throw new DomainException($"معین {r.AccountCode} پیدا نشد.");
        var asOf = PersianDate.TryParse(r.AsOf, out var d) ? d : DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var creditNature = account.Nature == AccountNature.Credit;

        var lines = await (from l in db.VoucherLines.AsNoTracking()
                           join v in db.Vouchers.AsNoTracking() on l.VoucherId equals v.Id
                           where l.AccountId == account.Id && v.FiscalYearId == r.FiscalYearId && v.VoucherDate <= asOf
                                 && (v.Status == VoucherStatus.Temporary || v.Status == VoucherStatus.Permanent)
                                 && v.Type != VoucherType.Closing && v.Type != VoucherType.ClosingTemporary
                           orderby v.VoucherDate, v.TempNo, l.RowNo
                           select new { l.Detail1Id, v.VoucherDate, v.VoucherNo, v.TempNo, l.Debit, l.Credit, l.Description })
            .ToListAsync(ct);
        var detailIds = lines.Select(l => l.Detail1Id).OfType<int>().Distinct().ToList();
        var details = await db.DetailAccounts.AsNoTracking().Where(x => detailIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.Code} {x.Title}", ct);

        string Bucket(int days)
        {
            for (var i = 0; i < AgeBuckets.Length; i++)
                if (days <= AgeBuckets[i]) return $"b{i}";
            return $"b{AgeBuckets.Length}";
        }

        var rows = new List<ReportRow>();
        foreach (var g in lines.GroupBy(l => l.Detail1Id).OrderBy(g => g.Key is int k ? details.GetValueOrDefault(k) : ""))
        {
            // اقلام طرف ماهیت (مبلغ مثبت = افزایش مانده) و گردش مقابل که FIFO تسویه می‌کند
            var open = new List<(DateOnly Date, string Doc, string? Text, decimal Left)>();
            decimal advance = 0;
            foreach (var l in g)
            {
                var amount = creditNature ? l.Credit - l.Debit : l.Debit - l.Credit;
                if (amount > 0) open.Add((l.VoucherDate, l.VoucherNo?.ToString() ?? $"موقت {l.TempNo}", l.Description, amount));
                else
                {
                    var pay = -amount;
                    for (var i = 0; i < open.Count && pay > 0; i++)
                    {
                        var take = Math.Min(open[i].Left, pay);
                        open[i] = open[i] with { Left = open[i].Left - take };
                        pay -= take;
                    }
                    advance += pay;
                    open.RemoveAll(o => o.Left == 0);
                }
            }
            // پیش‌دریافت/پیش‌پرداخت با اقلام بعدی تسویه می‌شود
            for (var i = 0; i < open.Count && advance > 0; i++)
            {
                var take = Math.Min(open[i].Left, advance);
                open[i] = open[i] with { Left = open[i].Left - take };
                advance -= take;
            }
            open.RemoveAll(o => o.Left == 0);
            if (open.Count == 0 && advance == 0) continue;

            Dictionary<string, object?> Cells(string title, IEnumerable<(DateOnly Date, decimal Left)> items, decimal adv, string? doc = null, string? date = null)
            {
                var list = items.ToList();
                var c = new Dictionary<string, object?>
                {
                    ["title"] = title, ["doc"] = doc, ["date"] = date,
                    ["balance"] = list.Sum(x => x.Left) - adv,
                    ["advance"] = adv == 0 ? null : adv,
                    ["days"] = list.Count == 0 ? null : (decimal)list.Max(x => asOf.DayNumber - x.Date.DayNumber),
                };
                for (var i = 0; i <= AgeBuckets.Length; i++) c[$"b{i}"] = 0m;
                foreach (var x in list) c[Bucket(asOf.DayNumber - x.Date.DayNumber)] = (decimal)c[Bucket(asOf.DayNumber - x.Date.DayNumber)]! + x.Left;
                return c;
            }
            var children = open.Select(o => new ReportRow(Cells(o.Text ?? "", [(o.Date, o.Left)], 0, o.Doc, PersianDate.ToPersian(o.Date)))).ToList();
            rows.Add(new ReportRow(Cells(g.Key is int k ? details.GetValueOrDefault(k, "?") : "بدون تفصیلی", open.Select(o => (o.Date, o.Left)), advance), children));
        }

        var columns = new List<ReportColumn>
        {
            new("title", "طرف حساب / شرح قلم"), new("doc", "سند", ReportColumnKind.Code), new("date", "تاریخ", ReportColumnKind.Code),
            new("balance", "مانده", ReportColumnKind.Amount), new("advance", creditNature ? "پیش‌پرداخت" : "پیش‌دریافت", ReportColumnKind.Amount),
            new("days", "قدیمی‌ترین (روز)", ReportColumnKind.Number, Sum: false),
        };
        for (var i = 0; i <= AgeBuckets.Length; i++)
            columns.Add(new($"b{i}", i == 0 ? $"تا {AgeBuckets[0]} روز" : i < AgeBuckets.Length ? $"{AgeBuckets[i - 1] + 1} تا {AgeBuckets[i]}" : $"بیش از {AgeBuckets[^1]}",
                ReportColumnKind.Amount, "سن مانده"));
        return new ReportTable($"تحلیل سنی {account.Code} {account.Title}",
            [$"تا تاریخ {PersianDate.ToPersian(asOf)}", creditNature ? "اقلام بستانکار باز؛ پرداخت‌ها از قدیمی‌ترین قلم کم شده است." : "اقلام بدهکار باز؛ دریافت‌ها از قدیمی‌ترین قلم کم شده است."],
            columns, rows, true, "اقلام باز");
    }
}
