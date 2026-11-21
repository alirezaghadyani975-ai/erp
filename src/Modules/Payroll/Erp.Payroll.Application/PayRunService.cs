using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>
/// اجرای ماهانه حقوق یک کارگاه: ساخت با کارکرد پیش‌فرض (روزهای اشتغال در ماه و مرکز هزینه پرونده)، ویرایش کارکرد و ورودی‌ها،
/// محاسبه با فرمول‌ها (بیمه با نرخ کارگاه)، و ورود مالیات حقوق از نتیجه سامانه مالیاتی (بعد از محاسبه؛ خودکار دوباره محاسبه می‌شود).
/// </summary>
public class PayRunService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock)
{
    public const string InsuranceWorkerCode = "INS_WORKER";

    // ---------------- فهرست و ساخت ----------------

    public async Task<List<PayRunListItemDto>> ListAsync(short? year, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var query = db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).AsQueryable();
        if (year is short y) query = query.Where(r => r.Year == y);
        var runs = await query.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync(ct);
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Kind, ct);
        var workshops = await db.Employees.Select(e => new { e.WorkshopId, e.WorkshopTitle }).Distinct().ToListAsync(ct);
        string Workshop(int id) => workshops.FirstOrDefault(w => w.WorkshopId == id)?.WorkshopTitle ?? "?";
        decimal Sum(PayRun r, PayItemKind k) => r.Lines.Where(l => items.GetValueOrDefault(l.PayItemId) == k).Sum(l => l.Amount);
        return runs.Select(r => new PayRunListItemDto(r.Id, r.Year, r.Month, r.Title, r.WorkshopId, Workshop(r.WorkshopId), r.Status,
            r.Attendances.Count, Sum(r, PayItemKind.Earning), Sum(r, PayItemKind.Deduction),
            Sum(r, PayItemKind.Earning) - Sum(r, PayItemKind.Deduction), Sum(r, PayItemKind.EmployerShare),
            r.CalculatedAt is { } at ? PersianDate.ToPersianDateTime(at) : null, r.Description)).ToList();
    }

    /// <summary>اجرای تازه با کارکرد پیش‌فرض همه کارکنانِ شاغل کارگاه در آن ماه.</summary>
    public async Task<int> CreateAsync(CreatePayRunRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunCreate);
        if (r.Month is < 1 or > 12) throw new DomainException("ماه باید بین ۱ و ۱۲ باشد.");
        if (await db.Runs.AnyAsync(x => x.Year == r.Year && x.Month == r.Month && x.WorkshopId == r.WorkshopId, ct))
            throw new DomainException($"حقوق {r.Year}/{r.Month:00} این کارگاه قبلاً ساخته شده است.");
        if (!await db.YearParams.AnyAsync(p => p.Year == r.Year, ct))
            throw new DomainException($"پارامترهای قانونی سال {r.Year} ثبت نشده است؛ در «پارامترهای قانونی سال» از سال قبل کپی و ارقام مصوب را وارد کنید.");
        var settings = await db.Settings.AsNoTracking().SingleAsync(ct);
        var calendarDays = PersianDate.DaysInMonth(r.Year, r.Month);
        var monthDays = settings.MonthDaysMode == MonthDaysMode.Thirty ? 30 : calendarDays;

        var run = new PayRun(r.Year, r.Month, r.WorkshopId, (byte)monthDays, user.UserId, clock.GetUtcNow().UtcDateTime);
        run.SetDescription(r.Description);
        var added = 0;
        foreach (var e in await EligibleAsync(run, ct))
        {
            run.AddAttendance(DefaultAttendance(run, e, calendarDays));
            added++;
        }
        if (added == 0) throw new DomainException("در این کارگاه کارمندِ شاغل در این ماه نیست.");
        db.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    /// <summary>کارکنان شاغلِ تازه (مثلاً استخدام بعد از ساخت اجرا) را اضافه می‌کند؛ تعداد افزوده‌شده.</summary>
    public async Task<int> AddMissingEmployeesAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        run.EnsureEditable();
        var calendarDays = PersianDate.DaysInMonth(run.Year, run.Month);
        var count = 0;
        foreach (var e in (await EligibleAsync(run, ct)).Where(e => run.Attendances.All(a => a.EmployeeId != e.Id)))
        {
            run.AddAttendance(DefaultAttendance(run, e, calendarDays));
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    public async Task RemoveEmployeeAsync(int runId, int attendanceId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        var a = run.Attendances.FirstOrDefault(x => x.Id == attendanceId) ?? throw new NotFoundException("کارکرد پیدا نشد.");
        run.RemoveAttendance(a);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunDelete);
        var run = await LoadAsync(runId, ct);
        run.EnsureEditable();
        db.Remove(run);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- کارکرد ----------------

    public async Task<PayRunDto> GetAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).ThenInclude(a => a.Centers)
            .Include(r => r.Attendances).ThenInclude(a => a.Inputs).FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id) || e.WorkshopId == run.WorkshopId).ToDictionaryAsync(e => e.Id, ct);
        var centers = await db.Centers.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var columns = (await db.PayItems.AsNoTracking()
                .Where(i => i.Source == PayItemSource.MonthlyInput || i.Source == PayItemSource.Loan || i.Source == PayItemSource.TaxOffice)
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct))
            .Where(i => i.IsActive || run.Attendances.Any(a => a.InputOf(i.Id) != 0))
            .Select(i => new InputColumnDto(i.Id, i.Code, i.Title, i.Source, i.Kind)).ToList();

        var rows = run.Attendances.Select(a =>
            {
                var e = employees[a.EmployeeId];
                return new AttendanceRowDto(a.Id, a.EmployeeId, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays,
                    a.OtHours, a.NightHours, a.HolidayHours, a.AbsentDays,
                    a.Centers.Select(c => new CenterShareDto(c.CostCenterId, centers.GetValueOrDefault(c.CostCenterId, "?"), c.Percent)).ToList(),
                    a.Inputs.ToDictionary(i => i.PayItemId, i => i.Amount));
            })
            .OrderBy(r => r.PersonnelNo).ToList();
        var workshop = employees.Values.FirstOrDefault(e => e.WorkshopId == run.WorkshopId)?.WorkshopTitle ?? "?";
        return new PayRunDto(run.Id, run.Year, run.Month, run.Title, run.WorkshopId, workshop, run.Status, run.MonthDays,
            PersianDate.DaysInMonth(run.Year, run.Month), run.Description,
            run.CalculatedAt is { } at ? PersianDate.ToPersianDateTime(at) : null, columns, rows);
    }

    /// <summary>ذخیره کارکرد چند کارمند؛ اجرای محاسبه‌شده «نیازمند محاسبه» می‌شود.</summary>
    public async Task SaveAttendanceAsync(int runId, IReadOnlyCollection<SaveAttendanceRequest> rows, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        run.EnsureEditable();
        var calendarDays = PersianDate.DaysInMonth(run.Year, run.Month);
        var itemIds = rows.SelectMany(r => r.Inputs.Keys).Distinct().ToList();
        var items = await db.PayItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var centerIds = rows.SelectMany(r => r.Centers.Select(c => c.CostCenterId)).Distinct().ToList();
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var names = await NamesAsync(run, ct);

        foreach (var r in rows)
        {
            var a = run.Attendances.FirstOrDefault(x => x.Id == r.AttendanceId) ?? throw new NotFoundException("کارکرد پیدا نشد.");
            try
            {
                a.Update(r.WorkDays, r.InsuranceDays, r.OtHours, r.NightHours, r.HolidayHours, r.AbsentDays, run.MonthDays, calendarDays);
                foreach (var c in r.Centers)
                {
                    var center = centers.GetValueOrDefault(c.CostCenterId) ?? throw new DomainException("مرکز هزینه پیدا نشد.");
                    if (!center.IsActive && a.Centers.All(x => x.CostCenterId != c.CostCenterId))
                        throw new DomainException($"مرکز هزینه «{center.Title}» غیرفعال است.");
                }
                a.SetCenters(r.Centers.Select(c => (c.CostCenterId, c.Percent)).ToList());
                foreach (var (itemId, amount) in r.Inputs)
                    a.SetInput(items.GetValueOrDefault(itemId) ?? throw new DomainException("قلم ورودی پیدا نشد."), amount);
            }
            catch (DomainException ex)
            {
                throw new DomainException($"{names.GetValueOrDefault(a.EmployeeId)}: {ex.Message}");
            }
        }
        run.Changed();
        await db.SaveChangesAsync(ct);
    }

    // ---------------- محاسبه ----------------

    public async Task<CalcResultDto> CalculateAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        var result = await CalculateCoreAsync(run, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// مالیات حقوق از نتیجه سامانه مالیاتی (کد ملی یا شماره پرسنلی ← مبلغ). مالیات کسی که در فایل نیست دست نمی‌خورد.
    /// بعد از ورود، اجرا خودکار دوباره محاسبه می‌شود تا خالص پرداختی به‌روز شود.
    /// </summary>
    public async Task<TaxImportResultDto> SetTaxAsync(int runId, IReadOnlyCollection<TaxRowInput> rows, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        run.EnsureEditable();
        var tax = await db.PayItems.Where(i => i.Source == PayItemSource.TaxOffice && i.IsActive).OrderBy(i => i.SortOrder).FirstOrDefaultAsync(ct)
                  ?? throw new DomainException("قلم فعالی با منبع «سامانه مالیات» تعریف نشده است.");
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToListAsync(ct);

        var unmatched = new List<string>();
        var matched = 0;
        foreach (var row in rows)
        {
            var key = PersianText.NormalizeOrNull(row.Key)?.Replace("-", "").Trim();
            var employee = key is null ? null
                : employees.FirstOrDefault(e => e.NationalId == key) ?? employees.FirstOrDefault(e => e.PersonnelNo == key);
            if (employee is null) { unmatched.Add(row.Key); continue; }
            run.Attendances.First(a => a.EmployeeId == employee.Id).SetInput(tax, row.Amount);
            matched++;
        }
        run.Changed();
        var calc = await CalculateCoreAsync(run, ct);
        await db.SaveChangesAsync(ct);
        return new TaxImportResultDto(matched, unmatched, calc);
    }

    private async Task<CalcResultDto> CalculateCoreAsync(PayRun run, CancellationToken ct)
    {
        run.EnsureEditable();
        var items = await db.PayItems.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
        var calculator = new PayCalculator(items);
        var parameters = await db.YearParams.AsNoTracking().Where(p => p.Year == run.Year).ToDictionaryAsync(p => p.Code, p => p.Value, ct);
        if (parameters.Count == 0)
            throw new DomainException($"پارامترهای قانونی سال {run.Year} ثبت نشده است.");

        var (start, end) = Period(run);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var decrees = await db.Decrees.AsNoTracking().Include(d => d.Items).Where(d => ids.Contains(d.EmployeeId) && d.EffectiveDate <= end)
            .OrderBy(d => d.EffectiveDate).ToListAsync(ct);

        var warnings = new List<CalcWarningDto>();
        var lines = new List<RunLine>();
        decimal earnings = 0, deductions = 0, employer = 0;
        foreach (var a in run.Attendances)
        {
            var e = employees[a.EmployeeId];
            void Warn(string message) => warnings.Add(new CalcWarningDto(e.PersonnelNo, e.DisplayName, message));

            var decree = DecreeAmounts(decrees.Where(d => d.EmployeeId == e.Id).ToList(), e, start, end);
            if (decree is null) Warn("حکم حقوقی نافذ در این ماه ندارد؛ اقلام حکمی صفر لحاظ شد.");
            if (a.WorkDays == 0) Warn("روز کارکرد صفر است.");
            if (e.InsuranceStatus != InsuranceStatus.NotInsured && a.InsuranceDays == 0) Warn("مشمول بیمه است ولی روز بیمه صفر است.");

            var (empRate, erRate, unRate) = e.InsuranceStatus switch
            {
                InsuranceStatus.NotInsured => (0m, 0m, 0m),
                InsuranceStatus.RetiredWorking => (e.EmployeeRate, e.EmployerRate, 0m),
                _ => (e.EmployeeRate, e.EmployerRate, e.UnemploymentRate)
            };
            var ctx = new PayCalcContext(a.WorkDays, e.InsuranceStatus == InsuranceStatus.NotInsured ? 0 : a.InsuranceDays, run.MonthDays,
                a.OtHours, a.NightHours, a.HolidayHours, a.AbsentDays, e.ChildrenCount, e.IsMarried, empRate, erRate, unRate);
            var result = calculator.Calculate(ctx, parameters, decree ?? new Dictionary<int, decimal>(),
                a.Inputs.ToDictionary(i => i.PayItemId, i => i.Amount));
            foreach (var w in result.Warnings) Warn(w);

            lines.AddRange(result.Amounts.Where(x => x.Value != 0).Select(x => new RunLine(e.Id, x.Key, x.Value)));
            earnings += result.Total(items, PayItemKind.Earning);
            deductions += result.Total(items, PayItemKind.Deduction);
            employer += result.Total(items, PayItemKind.EmployerShare);
        }
        if (run.Attendances.Count > 0 && !run.Attendances.Any(a => a.Inputs.Any(i => items.Any(x => x.Id == i.PayItemId && x.Source == PayItemSource.TaxOffice))))
            warnings.Add(new CalcWarningDto("", "", "مالیات حقوق هنوز وارد نشده است (بعد از محاسبه، نتیجه سامانه مالیاتی را وارد کنید)."));

        run.SetResults(lines, user.UserId, clock.GetUtcNow().UtcDateTime);
        return new CalcResultDto(run.Attendances.Count, earnings, deductions, earnings - deductions, employer, warnings);
    }

    /// <summary>
    /// مبلغ اقلام حکمی در ماه: اگر حکم در میانه ماه عوض شده باشد، میانگین وزنی بر اساس روزهای اشتغال زیر هر حکم.
    /// null = حکم نافذی ندارد.
    /// </summary>
    private static Dictionary<int, decimal>? DecreeAmounts(List<Decree> decrees, EmployeeRef e, DateOnly start, DateOnly end)
    {
        var from = e.HireDate > start ? e.HireDate : start;
        var to = e.LeaveDate is { } l && l < end ? l : end;
        if (to < from) to = from;
        var segments = new List<(Decree Decree, int Days)>();
        for (var i = 0; i < decrees.Count; i++)
        {
            var segStart = decrees[i].EffectiveDate > from ? decrees[i].EffectiveDate : from;
            var segEnd = i + 1 < decrees.Count ? decrees[i + 1].EffectiveDate.AddDays(-1) : to;
            if (segEnd > to) segEnd = to;
            var days = segEnd.DayNumber - segStart.DayNumber + 1;
            if (days > 0) segments.Add((decrees[i], days));
        }
        if (segments.Count == 0) return decrees.Count > 0 ? decrees[^1].Items.ToDictionary(i => i.PayItemId, i => i.Amount) : null;
        var total = segments.Sum(s => s.Days);
        return segments.SelectMany(s => s.Decree.Items.Select(i => (i.PayItemId, Weighted: i.Amount * s.Days)))
            .GroupBy(x => x.PayItemId).ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Weighted) / total, 4));
    }

    // ---------------- نتیجه ----------------

    public async Task<List<RunSummaryRowDto>> SummaryAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        return run.Attendances.Select(a =>
            {
                var e = employees[a.EmployeeId];
                var mine = run.Lines.Where(l => l.EmployeeId == a.EmployeeId).Select(l => (Item: items[l.PayItemId], l.Amount)).ToList();
                decimal Sum(Func<PayItem, bool> f) => mine.Where(x => f(x.Item)).Sum(x => x.Amount);
                var earnings = Sum(i => i.Kind == PayItemKind.Earning);
                var deductions = Sum(i => i.Kind == PayItemKind.Deduction);
                var ins = Sum(i => i.Code == InsuranceWorkerCode);
                var tax = Sum(i => i.Source == PayItemSource.TaxOffice);
                return new RunSummaryRowDto(e.Id, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays, earnings,
                    Sum(i => i.Kind == PayItemKind.Earning && i.InsuranceSubject), Sum(i => i.Kind == PayItemKind.Earning && i.TaxSubject),
                    ins, tax, deductions - ins - tax, earnings - deductions, Sum(i => i.Kind == PayItemKind.EmployerShare),
                    Sum(i => i.Kind == PayItemKind.Provision));
            })
            .OrderBy(r => r.PersonnelNo).ToList();
    }

    public async Task<PayslipDto> PayslipAsync(int runId, int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
        var a = run.Attendances.FirstOrDefault(x => x.EmployeeId == employeeId) ?? throw new NotFoundException("کارمند در این اجرا نیست.");
        var e = await db.Employees.FirstAsync(x => x.Id == employeeId, ct);
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var mine = run.Lines.Where(l => l.EmployeeId == employeeId).Select(l => (Item: items[l.PayItemId], l.Amount))
            .OrderBy(x => x.Item.SortOrder).ThenBy(x => x.Item.Code).ToList();
        List<PayslipLineDto> Of(PayItemKind k) => mine.Where(x => x.Item.Kind == k && x.Item.OnPayslip)
            .Select(x => new PayslipLineDto(x.Item.Code, x.Item.Title, x.Amount)).ToList();
        var earnings = mine.Where(x => x.Item.Kind == PayItemKind.Earning).Sum(x => x.Amount);
        var deductions = mine.Where(x => x.Item.Kind == PayItemKind.Deduction).Sum(x => x.Amount);
        return new PayslipDto(run.Title, e.WorkshopTitle, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays, a.OtHours,
            a.NightHours, a.HolidayHours, a.AbsentDays, Of(PayItemKind.Earning), Of(PayItemKind.Deduction),
            mine.Where(x => x.Item.Kind == PayItemKind.EmployerShare).Select(x => new PayslipLineDto(x.Item.Code, x.Item.Title, x.Amount)).ToList(),
            earnings, deductions, earnings - deductions,
            mine.Where(x => x.Item.Kind == PayItemKind.Earning && x.Item.InsuranceSubject).Sum(x => x.Amount));
    }

    // ---------------- کمکی ----------------

    private async Task<PayRun> LoadAsync(int runId, CancellationToken ct) =>
        await db.Runs.Include(r => r.Attendances).ThenInclude(a => a.Centers)
            .Include(r => r.Attendances).ThenInclude(a => a.Inputs).Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");

    private static (DateOnly Start, DateOnly End) Period(PayRun run)
    {
        var start = PersianDate.FromPersian(run.Year, run.Month, 1);
        return (start, start.AddDays(PersianDate.DaysInMonth(run.Year, run.Month) - 1));
    }

    private async Task<List<EmployeeRef>> EligibleAsync(PayRun run, CancellationToken ct)
    {
        var (start, end) = Period(run);
        return await db.Employees.Where(e => e.WorkshopId == run.WorkshopId && e.HireDate <= end && (e.LeaveDate == null || e.LeaveDate >= start))
            .OrderBy(e => e.PersonnelNo).ToListAsync(ct);
    }

    /// <summary>روز کارکرد = روزهای اشتغال در ماه (ماه کامل = MONTH_DAYS)؛ روز بیمه = روز واقعی؛ تعلیق = صفر؛ مرکز = مرکز پرونده.</summary>
    private static Attendance DefaultAttendance(PayRun run, EmployeeRef e, int calendarDays)
    {
        var (start, end) = Period(run);
        var from = e.HireDate > start ? e.HireDate : start;
        var to = e.LeaveDate is { } l && l < end ? l : end;
        var span = Math.Max(0, to.DayNumber - from.DayNumber + 1);
        var work = span == calendarDays ? run.MonthDays : Math.Min(span, (int)run.MonthDays);
        var ins = e.InsuranceStatus == InsuranceStatus.NotInsured ? 0 : span;
        if (e.Status == EmployeeStatus.Suspended) (work, ins) = (0, 0);
        var a = new Attendance(e.Id, e.CostCenterId);
        a.Update(work, ins, 0, 0, 0, 0, run.MonthDays, calendarDays);
        return a;
    }

    private async Task<Dictionary<int, string>> NamesAsync(PayRun run, CancellationToken ct)
    {
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        return await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => $"{e.PersonnelNo} {e.DisplayName}", ct);
    }
}
