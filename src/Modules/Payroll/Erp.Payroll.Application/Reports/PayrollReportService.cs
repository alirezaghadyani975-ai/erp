using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application.Reports;

/// <summary>
/// گزارش‌های حقوق و دستمزد. منبع: اجراهای محاسبه‌شده و قطعی (اجرای «نیازمند محاسبه» در گزارش‌های مبلغی نمی‌آید،
/// چون نتیجه‌اش با کارکرد هم‌خوان نیست؛ کارکرد ماهانه و کاردکس مرخصی از خود کارکرد خوانده می‌شوند).
/// </summary>
public class PayrollReportService(IPayrollDbContext db, IAccessControl access)
{
    private static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    private const string Earnings = "مزایا", Deductions = "کسور", Employer = "سهم کارفرما", Provisions = "ذخیره‌ها";

    // ---------------- ۱) لیست حقوق و دستمزد ----------------

    /// <summary>لیست حقوق ماه: هر قلم یک ستون، با جمع مزایا، کسور، خالص، سهم کارفرما و ذخیره‌ها.</summary>
    public async Task<ReportTable> RegisterAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var month = f.Month ?? throw new DomainException("ماه را انتخاب کنید.");
        var runs = await RunsAsync(f with { MonthTo = null }, amounts: true, ct);
        var items = await ItemsAsync(ct);
        var employees = await EmployeesAsync(runs, ct);
        var used = runs.SelectMany(r => r.Lines).Select(l => l.PayItemId).ToHashSet();
        var shown = items.Where(i => used.Contains(i.Id) && i.Kind != PayItemKind.Info).OrderBy(i => (int)i.Kind).ThenBy(i => i.SortOrder).ToList();

        var columns = new List<ReportColumn>
        {
            new("no", "پرسنلی", ReportColumnKind.Code), new("name", "نام و نام خانوادگی"), new("nid", "کد ملی", ReportColumnKind.Code),
            new("days", "کارکرد", ReportColumnKind.Number), new("ins_days", "روز بیمه", ReportColumnKind.Number)
        };
        void Group(PayItemKind kind, string group, string? totalKey, string? totalTitle)
        {
            columns.AddRange(shown.Where(i => i.Kind == kind).Select(i => new ReportColumn($"i{i.Id}", i.Title, ReportColumnKind.Amount, group)));
            if (totalKey is not null) columns.Add(new ReportColumn(totalKey, totalTitle!, ReportColumnKind.Amount, group));
        }
        Group(PayItemKind.Earning, Earnings, "earn", "جمع مزایا");
        Group(PayItemKind.Deduction, Deductions, "ded", "جمع کسور");
        columns.Add(new ReportColumn("net", "خالص پرداختی", ReportColumnKind.Amount));
        Group(PayItemKind.EmployerShare, Employer, null, null);
        Group(PayItemKind.Provision, Provisions, null, null);

        var rows = new List<ReportRow>();
        foreach (var run in runs)
            foreach (var a in run.Attendances.OrderBy(a => employees[a.EmployeeId].PersonnelNo))
            {
                var e = employees[a.EmployeeId];
                var lines = run.Lines.Where(l => l.EmployeeId == a.EmployeeId).ToDictionary(l => l.PayItemId, l => l.Amount);
                var cells = new Dictionary<string, object?>
                {
                    ["no"] = e.PersonnelNo, ["name"] = e.DisplayName, ["nid"] = e.NationalId, ["days"] = a.WorkDays, ["ins_days"] = (decimal)a.InsuranceDays
                };
                foreach (var i in shown) cells[$"i{i.Id}"] = lines.GetValueOrDefault(i.Id);
                var earn = shown.Where(i => i.Kind == PayItemKind.Earning).Sum(i => lines.GetValueOrDefault(i.Id));
                var ded = shown.Where(i => i.Kind == PayItemKind.Deduction).Sum(i => lines.GetValueOrDefault(i.Id));
                (cells["earn"], cells["ded"], cells["net"]) = (earn, ded, earn - ded);
                rows.Add(new ReportRow(cells));
            }
        return new ReportTable($"لیست حقوق و دستمزد {MonthNames[month - 1]} {f.Year}", Subtitle(runs, f), columns, rows, Landscape: true);
    }

    // ---------------- ۲) کارکرد ماهانه ----------------

    public async Task<ReportTable> AttendanceAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var month = f.Month ?? throw new DomainException("ماه را انتخاب کنید.");
        var runs = await RunsAsync(f with { MonthTo = null }, amounts: false, ct);
        var employees = await EmployeesAsync(runs, ct);
        var centers = await db.Centers.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var columns = new List<ReportColumn>
        {
            new("no", "پرسنلی", ReportColumnKind.Code), new("name", "نام و نام خانوادگی"), new("workshop", "کارگاه"),
            new("days", "کارکرد", ReportColumnKind.Number), new("ins_days", "روز بیمه", ReportColumnKind.Number),
            new("ot", "اضافه‌کار (ساعت)", ReportColumnKind.Number), new("night", "شب‌کاری", ReportColumnKind.Number),
            new("holiday", "تعطیل‌کاری", ReportColumnKind.Number), new("absent", "غیبت (روز)", ReportColumnKind.Number),
            new("leave", "مرخصی (روز)", ReportColumnKind.Number), new("centers", "مرکز هزینه"), new("note", "وضعیت")
        };
        var rows = runs.SelectMany(run => run.Attendances.Select(a => (run, a)))
            .OrderBy(x => employees[x.a.EmployeeId].PersonnelNo)
            .Select(x =>
            {
                var e = employees[x.a.EmployeeId];
                return new ReportRow(new Dictionary<string, object?>
                {
                    ["no"] = e.PersonnelNo, ["name"] = e.DisplayName, ["workshop"] = e.WorkshopTitle, ["days"] = x.a.WorkDays,
                    ["ins_days"] = (decimal)x.a.InsuranceDays, ["ot"] = x.a.OtHours, ["night"] = x.a.NightHours, ["holiday"] = x.a.HolidayHours,
                    ["absent"] = x.a.AbsentDays, ["leave"] = x.a.LeaveDays,
                    ["centers"] = string.Join("، ", x.a.Centers.Select(c => c.Percent == 100 ? centers.GetValueOrDefault(c.CostCenterId)
                        : $"{centers.GetValueOrDefault(c.CostCenterId)} {c.Percent:0.##}٪")),
                    ["note"] = x.a.IsSettlement ? "تسویه حساب" : e.LeaveDate is { } l ? $"ترک کار {PersianDate.ToPersian(l)}" : null
                });
            }).ToList();
        return new ReportTable($"کارکرد ماهانه {MonthNames[month - 1]} {f.Year}", Subtitle(runs, f), columns, rows, Landscape: true);
    }

    // ---------------- ۳) کاردکس مرخصی ----------------

    /// <summary>
    /// کاردکس مرخصی: با کارمند، گردش ماه‌به‌ماه سال (استحقاق، استفاده، بازخرید در تسویه، مانده)؛ بدون کارمند، خلاصه همه کارکنان.
    /// استحقاق هر ماه = LEAVE_DAYS سال × کارکرد ÷ روزهای سال؛ تسویه حساب مانده را صفر می‌کند.
    /// </summary>
    public async Task<ReportTable> LeaveKardexAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var settings = await db.Settings.AsNoTracking().SingleAsync(ct);
        var leaveDays = await db.YearParams.AsNoTracking().Where(p => p.Code == "LEAVE_DAYS").ToDictionaryAsync(p => p.Year, p => p.Value, ct);
        var query = db.Runs.AsNoTracking().Include(r => r.Attendances).Where(r => r.Year <= f.Year);
        if (f.WorkshopId is int w) query = query.Where(r => r.WorkshopId == w);
        var runs = await query.ToListAsync(ct);
        var movements = runs.SelectMany(r => r.Attendances.Where(a => f.EmployeeId == null || a.EmployeeId == f.EmployeeId)
                .Select(a => (r.Year, r.Month, a.EmployeeId, a.WorkDays, a.LeaveDays, a.IsSettlement)))
            .OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();
        var ids = movements.Select(m => m.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        decimal YearDays(short y) => settings.MonthDaysMode == MonthDaysMode.Thirty ? 360 : Enumerable.Range(1, 12).Sum(m => PersianDate.DaysInMonth(y, m));

        var detail = f.EmployeeId is not null;
        var columns = detail
            ? new List<ReportColumn>
            {
                new("month", "ماه"), new("days", "کارکرد", ReportColumnKind.Number), new("earned", "استحقاق (روز)", ReportColumnKind.Number),
                new("used", "استفاده‌شده", ReportColumnKind.Number), new("bought", "بازخرید در تسویه", ReportColumnKind.Number),
                new("balance", "مانده", ReportColumnKind.Number, Sum: false)
            }
            : new List<ReportColumn>
            {
                new("no", "پرسنلی", ReportColumnKind.Code), new("name", "نام و نام خانوادگی"), new("opening", "مانده اول سال", ReportColumnKind.Number),
                new("earned", "استحقاق امسال", ReportColumnKind.Number), new("used", "استفاده امسال", ReportColumnKind.Number),
                new("bought", "بازخرید امسال", ReportColumnKind.Number), new("balance", "مانده", ReportColumnKind.Number)
            };
        var rows = new List<ReportRow>();
        foreach (var id in ids.OrderBy(i => employees[i].PersonnelNo))
        {
            decimal balance = 0, opening = 0, earnedYear = 0, usedYear = 0, boughtYear = 0;
            if (detail) rows.Add(new ReportRow(new Dictionary<string, object?> { ["month"] = "مانده از سال‌های قبل", ["balance"] = 0m }));
            foreach (var m in movements.Where(x => x.EmployeeId == id))
            {
                var earned = Math.Round(leaveDays.GetValueOrDefault(m.Year) * m.WorkDays / YearDays(m.Year), 2);
                balance += earned - m.LeaveDays;
                var bought = m.IsSettlement ? Math.Max(0, balance) : 0;
                if (m.IsSettlement) balance = 0;
                if (m.Year < f.Year) { opening = balance; continue; }
                (earnedYear, usedYear, boughtYear) = (earnedYear + earned, usedYear + m.LeaveDays, boughtYear + bought);
                if (detail)
                    rows.Add(new ReportRow(new Dictionary<string, object?>
                    {
                        ["month"] = $"{MonthNames[m.Month - 1]} {m.Year}", ["days"] = m.WorkDays, ["earned"] = earned, ["used"] = m.LeaveDays,
                        ["bought"] = bought, ["balance"] = Math.Round(balance, 2)
                    }));
            }
            if (detail)
                rows[0] = new ReportRow(new Dictionary<string, object?> { ["month"] = "مانده از سال‌های قبل", ["balance"] = Math.Round(opening, 2) });
            else
                rows.Add(new ReportRow(new Dictionary<string, object?>
                {
                    ["no"] = employees[id].PersonnelNo, ["name"] = employees[id].DisplayName, ["opening"] = Math.Round(opening, 2),
                    ["earned"] = earnedYear, ["used"] = usedYear, ["bought"] = boughtYear, ["balance"] = Math.Round(balance, 2)
                }));
        }
        var who = detail && employees.TryGetValue(f.EmployeeId!.Value, out var one) ? $" — {one.PersonnelNo} {one.DisplayName}" : "";
        return new ReportTable($"کاردکس مرخصی سال {f.Year}{who}",
            [$"استحقاق سالانه {leaveDays.GetValueOrDefault(f.Year):0.##} روز کاری؛ مانده با تسویه حساب بازخرید و صفر می‌شود."], columns, rows);
    }

    // ---------------- ۴) خلاصه سالانه اقلام ----------------

    public async Task<ReportTable> YearSummaryAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var runs = await RunsAsync(f with { Month = null, MonthTo = null }, amounts: true, ct);
        var items = await ItemsAsync(ct);
        var columns = new List<ReportColumn> { new("item", "قلم"), new("kind", "نوع") };
        columns.AddRange(Enumerable.Range(1, 12).Select(m => new ReportColumn($"m{m}", MonthNames[m - 1], ReportColumnKind.Amount)));
        columns.Add(new ReportColumn("total", "جمع سال", ReportColumnKind.Amount));
        var rows = new List<ReportRow>();
        foreach (var item in items.Where(i => i.Kind != PayItemKind.Info).OrderBy(i => (int)i.Kind).ThenBy(i => i.SortOrder))
        {
            var byMonth = runs.GroupBy(r => r.Month).ToDictionary(g => (int)g.Key, g => g.SelectMany(r => r.Lines).Where(l => l.PayItemId == item.Id).Sum(l => l.Amount));
            if (byMonth.Values.All(v => v == 0)) continue;
            var cells = new Dictionary<string, object?> { ["item"] = item.Title, ["kind"] = KindTitle(item.Kind) };
            for (var m = 1; m <= 12; m++) cells[$"m{m}"] = byMonth.GetValueOrDefault(m);
            cells["total"] = byMonth.Values.Sum();
            rows.Add(new ReportRow(cells));
        }
        var net = new Dictionary<string, object?> { ["item"] = "خالص پرداختی", ["kind"] = "" };
        for (var m = 1; m <= 12; m++)
            net[$"m{m}"] = runs.Where(r => r.Month == m).SelectMany(r => r.Lines).Sum(l => Sign(items, l));
        net["total"] = runs.SelectMany(r => r.Lines).Sum(l => Sign(items, l));
        rows.Add(new ReportRow(net));
        return new ReportTable($"خلاصه سالانه اقلام حقوق {f.Year}", Subtitle(runs, f), columns, rows, Landscape: true);
    }

    // ---------------- ۵) سوابق حقوق کارمند ----------------

    public async Task<ReportTable> EmployeeHistoryAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var employeeId = f.EmployeeId ?? throw new DomainException("کارمند را انتخاب کنید.");
        var runs = (await RunsAsync(f with { Month = null, MonthTo = null, WorkshopId = null }, amounts: true, ct))
            .Where(r => r.Attendances.Any(a => a.EmployeeId == employeeId)).OrderBy(r => r.Month).ToList();
        var items = await ItemsAsync(ct);
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
        var columns = new List<ReportColumn>
        {
            new("month", "ماه"), new("days", "کارکرد", ReportColumnKind.Number), new("earn", "جمع مزایا", ReportColumnKind.Amount),
            new("ins", "بیمه سهم کارگر", ReportColumnKind.Amount), new("tax", "مالیات", ReportColumnKind.Amount),
            new("loan", "وام و مساعده", ReportColumnKind.Amount), new("other", "سایر کسور", ReportColumnKind.Amount),
            new("net", "خالص پرداختی", ReportColumnKind.Amount), new("employer", "سهم کارفرما", ReportColumnKind.Amount),
            new("prov", "ذخیره عیدی و سنوات", ReportColumnKind.Amount)
        };
        var rows = runs.Select(r =>
        {
            var a = r.Attendances.First(x => x.EmployeeId == employeeId);
            var lines = r.Lines.Where(l => l.EmployeeId == employeeId).Select(l => (Item: items.First(i => i.Id == l.PayItemId), l.Amount)).ToList();
            decimal Sum(Func<PayItem, bool> p) => lines.Where(x => p(x.Item)).Sum(x => x.Amount);
            var earn = Sum(i => i.Kind == PayItemKind.Earning);
            var ded = Sum(i => i.Kind == PayItemKind.Deduction);
            var ins = Sum(i => i.Code == PayRunService.InsuranceWorkerCode);
            var tax = Sum(i => i.Source == PayItemSource.TaxOffice);
            var loan = Sum(i => i.Source == PayItemSource.Loan);
            return new ReportRow(new Dictionary<string, object?>
            {
                ["month"] = $"{MonthNames[r.Month - 1]}{(a.IsSettlement ? " (تسویه)" : "")}", ["days"] = a.WorkDays, ["earn"] = earn, ["ins"] = ins,
                ["tax"] = tax, ["loan"] = loan, ["other"] = ded - ins - tax - loan, ["net"] = earn - ded,
                ["employer"] = Sum(i => i.Kind == PayItemKind.EmployerShare), ["prov"] = Sum(i => i.Kind == PayItemKind.Provision)
            });
        }).ToList();
        return new ReportTable($"سوابق حقوق {e.PersonnelNo} {e.DisplayName} — سال {f.Year}", Subtitle(runs, f), columns, rows);
    }

    // ---------------- ۶) هزینه حقوق به تفکیک مرکز هزینه ----------------

    /// <summary>هزینه حقوق (مزایا، سهم کارفرما، ذخیره‌ها) به نسبت تقسیم کارکرد هر کارمند بین مراکز؛ ورودی جذب دستمزد در بهای تمام‌شده.</summary>
    public async Task<ReportTable> CostByCenterAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var runs = await RunsAsync(f, amounts: true, ct);
        var items = (await ItemsAsync(ct)).ToDictionary(i => i.Id);
        var centers = await db.Centers.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var totals = new Dictionary<int, (decimal Earn, decimal Employer, decimal Prov, decimal Days)>();
        foreach (var run in runs)
            foreach (var a in run.Attendances)
            {
                var lines = run.Lines.Where(l => l.EmployeeId == a.EmployeeId).ToList();
                decimal Of(PayItemKind k) => lines.Where(l => items[l.PayItemId].Kind == k).Sum(l => l.Amount);
                var (earn, employer, prov) = (Of(PayItemKind.Earning), Of(PayItemKind.EmployerShare), Of(PayItemKind.Provision));
                foreach (var c in a.Centers)
                {
                    var p = c.Percent / 100;
                    var t = totals.GetValueOrDefault(c.CostCenterId);
                    totals[c.CostCenterId] = (t.Earn + Math.Round(earn * p, 0), t.Employer + Math.Round(employer * p, 0),
                        t.Prov + Math.Round(prov * p, 0), t.Days + a.WorkDays * p);
                }
            }
        var columns = new List<ReportColumn>
        {
            new("center", "مرکز هزینه"), new("days", "نفر-روز کارکرد", ReportColumnKind.Number), new("earn", "حقوق و مزایا", ReportColumnKind.Amount),
            new("employer", "سهم کارفرما (بیمه)", ReportColumnKind.Amount), new("prov", "ذخیره عیدی و سنوات", ReportColumnKind.Amount),
            new("total", "جمع هزینه", ReportColumnKind.Amount)
        };
        var rows = totals.OrderBy(t => centers.GetValueOrDefault(t.Key)).Select(t => new ReportRow(new Dictionary<string, object?>
        {
            ["center"] = centers.GetValueOrDefault(t.Key, "?"), ["days"] = Math.Round(t.Value.Days, 2), ["earn"] = t.Value.Earn,
            ["employer"] = t.Value.Employer, ["prov"] = t.Value.Prov, ["total"] = t.Value.Earn + t.Value.Employer + t.Value.Prov
        })).ToList();
        var period = f.Month is byte m1 ? f.MonthTo is byte m2 && m2 != m1 ? $"{MonthNames[m1 - 1]} تا {MonthNames[m2 - 1]}" : MonthNames[m1 - 1] : "کل سال";
        return new ReportTable($"هزینه حقوق به تفکیک مرکز هزینه — {period} {f.Year}", Subtitle(runs, f), columns, rows);
    }

    // ---------------- ۷) مانده ذخیره عیدی و سنوات ----------------

    /// <summary>مانده ذخیره هر کارمند تا پایان ماه (فقط حقوق‌های قطعی): ذخیره ماهانه منهای آزادشده در پرداخت عیدی/سنوات.</summary>
    public async Task<ReportTable> ProvisionBalancesAsync(PayrollReportFilter f, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.ReportView);
        var to = f.Year * 100 + (f.Month ?? 12);
        var query = db.Runs.AsNoTracking().Include(r => r.Lines).Where(r => r.Status == PayRunStatus.Final);
        if (f.WorkshopId is int w) query = query.Where(r => r.WorkshopId == w);
        var runs = (await query.ToListAsync(ct)).Where(r => r.Year * 100 + r.Month <= to).OrderBy(r => r.Year).ThenBy(r => r.Month).ToList();
        var items = (await ItemsAsync(ct)).ToDictionary(i => i.Id);
        var accounts = items.Values.Where(i => i.Kind == PayItemKind.Provision && i.AccountId is not null)
            .GroupBy(i => i.AccountId!.Value).ToDictionary(g => g.Key, g => g.First().Title);
        var ids = runs.SelectMany(r => r.Lines).Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        var columns = new List<ReportColumn> { new("no", "پرسنلی", ReportColumnKind.Code), new("name", "نام و نام خانوادگی") };
        foreach (var (acc, title) in accounts)
        {
            columns.Add(new ReportColumn($"y{acc}", $"{title} امسال", ReportColumnKind.Amount, title));
            columns.Add(new ReportColumn($"b{acc}", "مانده", ReportColumnKind.Amount, title));
        }
        columns.Add(new ReportColumn("total", "جمع مانده ذخایر", ReportColumnKind.Amount));
        var rows = new List<ReportRow>();
        foreach (var id in ids.OrderBy(i => employees[i].PersonnelNo))
        {
            var cells = new Dictionary<string, object?> { ["no"] = employees[id].PersonnelNo, ["name"] = employees[id].DisplayName };
            decimal total = 0;
            foreach (var acc in accounts.Keys)
            {
                decimal balance = 0, year = 0;
                foreach (var r in runs)
                {
                    var mine = r.Lines.Where(l => l.EmployeeId == id && items[l.PayItemId].AccountId == acc).ToList();
                    var accrued = mine.Where(l => items[l.PayItemId].Kind == PayItemKind.Provision).Sum(l => l.Amount);
                    balance += accrued;
                    if (r.Year == f.Year) year += accrued;
                    if (mine.Any(l => items[l.PayItemId].Kind == PayItemKind.Earning)) balance = 0; // پرداخت از محل ذخیره کل مانده را آزاد می‌کند
                }
                (cells[$"y{acc}"], cells[$"b{acc}"]) = (year, balance);
                total += balance;
            }
            cells["total"] = total;
            rows.Add(new ReportRow(cells));
        }
        var monthTitle = f.Month is byte m ? $"پایان {MonthNames[m - 1]}" : "پایان سال";
        return new ReportTable($"مانده ذخیره عیدی و سنوات کارکنان — {monthTitle} {f.Year}", ["فقط حقوق‌های قطعی (سند صادرشده)."], columns, rows);
    }

    // ---------------- کمکی ----------------

    /// <summary>اجرا با کارکرد و ردیف‌های نتیجه (ردیف‌ها در پرس‌وجوی جدا تا ضرب دکارتی کارکرد × ردیف پیش نیاید).</summary>
    private sealed record RunView(int Id, short Year, byte Month, int WorkshopId, PayRunStatus Status, IReadOnlyList<Attendance> Attendances,
        List<RunLine> Lines);

    private async Task<List<RunView>> RunsAsync(PayrollReportFilter f, bool amounts, CancellationToken ct)
    {
        var query = db.Runs.AsNoTracking().Include(r => r.Attendances).ThenInclude(a => a.Centers).Where(r => r.Year == f.Year);
        if (amounts) query = query.Where(r => r.Status != PayRunStatus.Draft);
        if (f.FinalOnly) query = query.Where(r => r.Status == PayRunStatus.Final);
        if (f.WorkshopId is int w) query = query.Where(r => r.WorkshopId == w);
        if (f.Month is byte from)
        {
            var to = f.MonthTo ?? from;
            query = query.Where(r => r.Month >= from && r.Month <= to);
        }
        var runs = await query.OrderBy(r => r.Month).ThenBy(r => r.WorkshopId).ToListAsync(ct);
        var ids = runs.Select(r => r.Id).ToList();
        var lines = amounts
            ? (await db.Runs.AsNoTracking().Where(r => ids.Contains(r.Id)).SelectMany(r => r.Lines).ToListAsync(ct)).ToLookup(l => l.RunId)
            : Enumerable.Empty<RunLine>().ToLookup(l => l.RunId);
        return runs.Select(r => new RunView(r.Id, r.Year, r.Month, r.WorkshopId, r.Status, r.Attendances, lines[r.Id].ToList())).ToList();
    }

    private async Task<Dictionary<int, EmployeeRef>> EmployeesAsync(List<RunView> runs, CancellationToken ct)
    {
        var ids = runs.SelectMany(r => r.Attendances).Select(a => a.EmployeeId).Distinct().ToList();
        return await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
    }

    private async Task<List<PayItem>> ItemsAsync(CancellationToken ct) =>
        await db.PayItems.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(ct);

    private static decimal Sign(List<PayItem> items, RunLine l) => items.First(i => i.Id == l.PayItemId).Kind switch
    {
        PayItemKind.Earning => l.Amount,
        PayItemKind.Deduction => -l.Amount,
        _ => 0
    };

    private static string KindTitle(PayItemKind k) => k switch
    {
        PayItemKind.Earning => Earnings, PayItemKind.Deduction => Deductions, PayItemKind.EmployerShare => Employer,
        PayItemKind.Provision => Provisions, _ => "اطلاعاتی"
    };

    private static List<string> Subtitle(List<RunView> runs, PayrollReportFilter f)
    {
        var list = new List<string>();
        if (runs.Count == 0) list.Add("حقوقی با این شرایط محاسبه نشده است.");
        if (runs.Any(r => r.Status != PayRunStatus.Final) && !f.FinalOnly) list.Add("شامل حقوق محاسبه‌شده‌ای که هنوز قطعی نشده است.");
        return list;
    }
}
