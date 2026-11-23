using Erp.Accounting.Contracts;
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
public class PayRunService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock, IVoucherPosting posting,
    IUnitOfWork uow)
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
                .Where(i => i.Source == PayItemSource.MonthlyInput || i.Source == PayItemSource.TaxOffice)
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct))
            .Where(i => i.IsActive || run.Attendances.Any(a => a.InputOf(i.Id) != 0))
            .Select(i => new InputColumnDto(i.Id, i.Code, i.Title, i.Source, i.Kind)).ToList();

        var rows = run.Attendances.Select(a =>
            {
                var e = employees[a.EmployeeId];
                return new AttendanceRowDto(a.Id, a.EmployeeId, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays,
                    a.OtHours, a.NightHours, a.HolidayHours, a.AbsentDays,
                    a.Centers.Select(c => new CenterShareDto(c.CostCenterId, centers.GetValueOrDefault(c.CostCenterId, "?"), c.Percent)).ToList(),
                    a.Inputs.ToDictionary(i => i.PayItemId, i => i.Amount), a.LeaveDays, a.IsSettlement);
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
                a.Update(r.WorkDays, r.InsuranceDays, r.OtHours, r.NightHours, r.HolidayHours, r.AbsentDays, run.MonthDays, calendarDays, r.LeaveDays);
                if (r.IsSettlement is bool settle) a.SetSettlement(settle);
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
        var parameters = await db.YearParams.AsNoTracking().Where(p => p.Year == run.Year).ToDictionaryAsync(p => p.Code, p => p.Value, ct);
        if (parameters.Count == 0)
            throw new DomainException($"پارامترهای قانونی سال {run.Year} ثبت نشده است.");

        var (start, end) = Period(run);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        // الگوی معتبر پایان ماه برای هر کارمند (اقلام، فرمول‌ها، قواعد گروه)
        var plans = await PayPlans.LoadAsync(db, ids, end, parameters, ct);
        var decrees = await db.Decrees.AsNoTracking().Include(d => d.Items).Where(d => ids.Contains(d.EmployeeId) && d.EffectiveDate <= end)
            .OrderBy(d => d.EffectiveDate).ToListAsync(ct);
        var loans = await db.Loans.AsNoTracking().Where(l => ids.Contains(l.EmployeeId)).ToListAsync(ct);
        var loanIds = loans.Select(l => l.Id).ToList();
        var deductedElsewhere = (await db.Runs.AsNoTracking().Where(r => r.Id != run.Id)
                .SelectMany(r => r.LoanDeductions.Where(d => loanIds.Contains(d.LoanId))).ToListAsync(ct))
            .GroupBy(d => d.LoanId).ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));
        var loanItems = items.Where(i => i.Source == PayItemSource.Loan).ToDictionary(i => i.Code, i => i.Id);
        var loanDeductions = new List<RunLoanDeduction>();
        var facts = await FactsAsync(run, employees, id => plans[id].Parameters.GetValueOrDefault("LEAVE_DAYS"), ct);

        var warnings = new List<CalcWarningDto>();
        var lines = new List<RunLine>();
        decimal earnings = 0, deductions = 0, employer = 0;
        foreach (var a in run.Attendances)
        {
            var e = employees[a.EmployeeId];
            void Warn(string message) => warnings.Add(new CalcWarningDto(e.PersonnelNo, e.DisplayName, message));
            var plan = plans[e.Id];
            if (plan.TemplateId is int templateId) a.UseTemplate(templateId);
            else Warn("الگوی محاسبه ندارد؛ همه اقلام فعال با پارامترهای سال محاسبه شد.");

            var decree = DecreeAmounts(decrees.Where(d => d.EmployeeId == e.Id).ToList(), e, start, end);
            if (decree is null) Warn("حکم حقوقی نافذ در این ماه ندارد؛ اقلام حکمی صفر لحاظ شد.");
            if (a.WorkDays == 0) Warn("روز کارکرد صفر است.");
            if (e.InsuranceStatus != InsuranceStatus.NotInsured && a.InsuranceDays == 0) Warn("مشمول بیمه است ولی روز بیمه صفر است.");

            var (empRate, erRate, unRate) = e.InsuranceStatus switch
            {
                InsuranceStatus.NotInsured => (0m, 0m, 0m),
                InsuranceStatus.RetiredWorking => (plan.EmployeeRate ?? e.EmployeeRate, plan.EmployerRate ?? e.EmployerRate, 0m),
                _ => (plan.EmployeeRate ?? e.EmployeeRate, plan.EmployerRate ?? e.EmployerRate, plan.UnemploymentRate ?? e.UnemploymentRate)
            };
            var ctx = new PayCalcContext(a.WorkDays, e.InsuranceStatus == InsuranceStatus.NotInsured ? 0 : a.InsuranceDays, run.MonthDays,
                a.OtHours, a.NightHours, a.HolidayHours, a.AbsentDays, e.ChildrenCount, e.IsMarried, empRate, erRate, unRate,
                run.Month, a.IsSettlement, facts.YearDays, facts.YearWorkDays[e.Id], facts.ServiceDays[e.Id], facts.LeaveBalance[e.Id]);
            if (a.IsSettlement && e.LeaveDate is null) Warn("تسویه حساب علامت خورده ولی ترک کار در پرونده پرسنلی ثبت نشده است.");
            // اقساط وام و مساعده از برنامه کسر (نه ورود دستی)
            var inputs = a.Inputs.ToDictionary(i => i.PayItemId, i => i.Amount);
            foreach (var id in loanItems.Values) inputs.Remove(id);
            foreach (var loan in loans.Where(l => l.EmployeeId == e.Id))
            {
                // در تسویه حساب کل مانده وام کسر می‌شود
                var installment = a.IsSettlement
                    ? Math.Max(0, loan.Amount - deductedElsewhere.GetValueOrDefault(loan.Id))
                    : loan.InstallmentFor(run.Year, run.Month, deductedElsewhere.GetValueOrDefault(loan.Id));
                if (installment <= 0) continue;
                if (!loanItems.TryGetValue(loan.ItemCode, out var itemId))
                {
                    Warn($"قلم فعال «{loan.ItemCode}» برای کسر {(loan.Kind == LoanKind.Loan ? "وام" : "مساعده")} شماره {loan.DocNo} نیست؛ کسر نشد.");
                    continue;
                }
                inputs[itemId] = inputs.GetValueOrDefault(itemId) + installment;
                loanDeductions.Add(new RunLoanDeduction(loan.Id, installment));
            }
            var result = plan.Calculator.Calculate(ctx, plan.Parameters, decree ?? new Dictionary<int, decimal>(), inputs);
            foreach (var w in result.Warnings) Warn(w);

            lines.AddRange(result.Amounts.Where(x => x.Value != 0).Select(x => new RunLine(e.Id, x.Key, x.Value)));
            earnings += result.Total(items, PayItemKind.Earning);
            deductions += result.Total(items, PayItemKind.Deduction);
            employer += result.Total(items, PayItemKind.EmployerShare);
        }
        if (run.Attendances.Count > 0 && !run.Attendances.Any(a => a.Inputs.Any(i => items.Any(x => x.Id == i.PayItemId && x.Source == PayItemSource.TaxOffice))))
            warnings.Add(new CalcWarningDto("", "", "مالیات حقوق هنوز وارد نشده است (بعد از محاسبه، نتیجه سامانه مالیاتی را وارد کنید)."));

        run.SetResults(lines, loanDeductions, user.UserId, clock.GetUtcNow().UtcDateTime);
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

    // ---------------- عیدی، سنوات، مرخصی و تسویه ----------------

    private sealed record Facts(decimal YearDays, Dictionary<int, decimal> YearWorkDays, Dictionary<int, decimal> ServiceDays,
        Dictionary<int, decimal> LeaveBalance, Dictionary<int, DateOnly> ServiceStart);

    /// <summary>
    /// متغیرهای عیدی، سنوات و مرخصی هر کارمند: کارکرد امسال تا این ماه، روزهای خدمت از استخدام یا آخرین تسویه
    /// تا ترک کار (یا پایان ماه)، و مانده مرخصی = استحقاق (LEAVE_DAYS × کارکرد ÷ روزهای سال) − مرخصی استفاده‌شده از شروع خدمت.
    /// </summary>
    private async Task<Facts> FactsAsync(PayRun run, Dictionary<int, EmployeeRef> employees, Func<int, decimal> leaveDaysFor, CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().SingleAsync(ct);
        decimal yearDays = settings.MonthDaysMode == MonthDaysMode.Thirty ? 360 : Enumerable.Range(1, 12).Sum(m => PersianDate.DaysInMonth(run.Year, m));
        var ids = employees.Keys.ToList();
        var others = await db.Runs.AsNoTracking().Where(r => r.Id != run.Id)
            .SelectMany(r => r.Attendances.Where(a => ids.Contains(a.EmployeeId)),
                (r, a) => new { r.Year, r.Month, a.EmployeeId, a.WorkDays, a.LeaveDays, a.IsSettlement })
            .ToListAsync(ct);
        var (_, end) = Period(run);
        var key = run.Year * 100 + run.Month;

        var facts = new Facts(yearDays, [], [], [], []);
        foreach (var a in run.Attendances)
        {
            var e = employees[a.EmployeeId];
            var mine = others.Where(o => o.EmployeeId == e.Id && o.Year * 100 + o.Month < key).ToList();
            facts.YearWorkDays[e.Id] = mine.Where(o => o.Year == run.Year).Sum(o => o.WorkDays) + a.WorkDays;

            var lastSettlement = mine.Where(o => o.IsSettlement).Select(o => o.Year * 100 + o.Month).DefaultIfEmpty(0).Max();
            var start = e.HireDate;
            if (lastSettlement > 0)
            {
                var (y, m) = (lastSettlement / 100, lastSettlement % 100);
                var afterSettlement = PersianDate.FromPersian(y, m, PersianDate.DaysInMonth(y, m)).AddDays(1);
                if (afterSettlement > start) start = afterSettlement;
            }
            var stop = e.LeaveDate is { } l && l < end ? l : end;
            facts.ServiceStart[e.Id] = start;
            facts.ServiceDays[e.Id] = Math.Max(0, stop.DayNumber - start.DayNumber + 1);

            var since = mine.Where(o => o.Year * 100 + o.Month > lastSettlement).ToList();
            var worked = since.Sum(o => o.WorkDays) + a.WorkDays;
            var used = since.Sum(o => o.LeaveDays) + a.LeaveDays;
            facts.LeaveBalance[e.Id] = Math.Round(leaveDaysFor(e.Id) * worked / yearDays - used, 2);
        }
        return facts;
    }

    /// <summary>
    /// تسویه حساب کارمندی که ترک کارش در پرونده ثبت شده: حقوق ماه ترک کار (اگر نیست ساخته می‌شود) با کارکرد تا روز ترک کار
    /// و علامت تسویه محاسبه می‌شود: عیدی سال، بازخرید سنوات، بازخرید مرخصی مانده، و کسر کامل مانده وام و مساعده.
    /// </summary>
    public async Task<SettlementDto> SettleAsync(int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
        var leave = e.LeaveDate ?? throw new DomainException($"برای {e.DisplayName} اول ترک کار را در پرونده پرسنلی ثبت کنید.");
        var (year, month) = ((short)PersianDate.PersianYear(leave), (byte)int.Parse(PersianDate.ToPersian(leave).Substring(5, 2)));

        var runId = await db.Runs.Where(r => r.Year == year && r.Month == month && r.WorkshopId == e.WorkshopId).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct)
                    ?? await CreateAsync(new CreatePayRunRequest(year, month, e.WorkshopId), ct);
        var run = await LoadAsync(runId, ct);
        run.EnsureEditable();
        var calendarDays = PersianDate.DaysInMonth(run.Year, run.Month);
        var a = run.Attendances.FirstOrDefault(x => x.EmployeeId == employeeId);
        var fresh = DefaultAttendance(run, e, calendarDays); // روزها تا تاریخ ترک کار
        if (a is null)
        {
            run.AddAttendance(fresh);
            a = fresh;
        }
        else a.Update(fresh.WorkDays, fresh.InsuranceDays, a.OtHours, a.NightHours, a.HolidayHours, a.AbsentDays, run.MonthDays, calendarDays, a.LeaveDays);
        a.SetSettlement(true);
        run.Changed();
        var calc = await CalculateCoreAsync(run, ct);
        await db.SaveChangesAsync(ct);
        var statement = await SettlementAsync(run.Id, employeeId, ct);
        return statement with { Warnings = calc.Warnings.Where(w => w.PersonnelNo == e.PersonnelNo || w.PersonnelNo == "").Select(w => w.Message).ToList() };
    }

    /// <summary>همه کارکنان (برای فرم تسویه).</summary>
    public async Task<List<EmployeeOptionDto>> EmployeesAsync(CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        return await db.Employees.OrderBy(e => e.PersonnelNo).Select(e => new EmployeeOptionDto(e.Id, e.PersonnelNo, e.DisplayName, e.Status)).ToListAsync(ct);
    }

    /// <summary>آخرین اجرای دارای علامت تسویه کارمند (یا null).</summary>
    public async Task<int?> SettlementRunAsync(int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        return await db.Runs.Where(r => r.Attendances.Any(a => a.EmployeeId == employeeId && a.IsSettlement))
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>لغو علامت تسویه (پیش از قطعی) و محاسبه دوباره.</summary>
    public async Task UnsettleAsync(int runId, int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunEdit);
        var run = await LoadAsync(runId, ct);
        var a = run.Attendances.FirstOrDefault(x => x.EmployeeId == employeeId) ?? throw new NotFoundException("کارمند در این اجرا نیست.");
        run.EnsureEditable();
        a.SetSettlement(false);
        run.Changed();
        await CalculateCoreAsync(run, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>صورت تسویه حساب کارمند در یک اجرا.</summary>
    public async Task<SettlementDto> SettlementAsync(int runId, int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).Include(r => r.LoanDeductions)
                      .FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
        var a = run.Attendances.FirstOrDefault(x => x.EmployeeId == employeeId) ?? throw new NotFoundException("کارمند در این اجرا نیست.");
        var e = await db.Employees.FirstAsync(x => x.Id == employeeId, ct);
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var yearParams = await db.YearParams.AsNoTracking().Where(p => p.Year == run.Year).ToDictionaryAsync(p => p.Code, p => p.Value, ct);
        var plan = (await PayPlans.LoadAsync(db, [e.Id], Period(run).End, yearParams, ct))[e.Id];
        var facts = await FactsAsync(run, new Dictionary<int, EmployeeRef> { [e.Id] = e }, _ => plan.Parameters.GetValueOrDefault("LEAVE_DAYS"), ct);
        var mine = run.Lines.Where(l => l.EmployeeId == employeeId).Select(l => (Item: items[l.PayItemId], l.Amount)).ToList();
        decimal Code(string c) => mine.Where(x => x.Item.Code == c).Sum(x => x.Amount);
        var earnings = mine.Where(x => x.Item.Kind == PayItemKind.Earning).Sum(x => x.Amount);
        var deductions = mine.Where(x => x.Item.Kind == PayItemKind.Deduction).Sum(x => x.Amount);
        var loanIds = await db.Loans.Where(l => l.EmployeeId == employeeId).Select(l => l.Id).ToListAsync(ct);
        return new SettlementDto(run.Id, run.Title, run.Status, e.Id, e.PersonnelNo, e.DisplayName,
            e.LeaveDate is { } l ? PersianDate.ToPersian(l) : null, a.IsSettlement, PersianDate.ToPersian(facts.ServiceStart[e.Id]),
            facts.ServiceDays[e.Id], facts.LeaveBalance[e.Id], facts.YearWorkDays[e.Id], Code("BONUS_PAY"), Code("SEVERANCE_PAY"), Code("LEAVE_PAY"),
            run.LoanDeductions.Where(d => loanIds.Contains(d.LoanId)).Sum(d => d.Amount), earnings, deductions, earnings - deductions, []);
    }

    // ---------------- قطعی و سند ----------------

    public const string SourceModule = "payroll";
    public const string DocType = "pay_run";

    /// <summary>
    /// قطعی کردن حقوق ماه و صدور سند حسابداری (الگوی pay_run) در یک تراکنش:
    /// بدهکار هزینه (عامل × مرکز به نسبت تقسیم کارکرد)، بستانکار حقوق پرداختنی هر کارمند، کسور و بدهی‌ها (معین قلم).
    /// </summary>
    public async Task<RunVoucherDto> FinalizeAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunFinal);
        var run = await LoadAsync(runId, ct);
        var request = await PostingRequestAsync(run, ct);
        run.MakeFinal();
        await using var tx = await uow.BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        var voucher = await posting.PostAsync(request, ct);
        await tx.CommitAsync(ct);
        return new RunVoucherDto(voucher.VoucherId, voucher.TempNo, voucher.VoucherNo, voucher.IsPermanent);
    }

    /// <summary>برگشت از قطعی: سند حسابداری حذف می‌شود (سند دائم‌شده مانع است) و کارکرد دوباره قابل اصلاح است.</summary>
    public async Task ReopenAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunFinal);
        var run = await LoadAsync(runId, ct);
        if (await db.Payments.AnyAsync(p => p.RunId == runId, ct))
            throw new DomainException($"برای حقوق {run.Title} پرداخت ثبت شده است؛ اول پرداخت‌ها را حذف کنید.");
        run.Reopen();
        await using var tx = await uow.BeginAsync(ct);
        await posting.UnpostAsync(SourceModule, DocType, run.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>سند حسابداری اجرای قطعی (یا null).</summary>
    public async Task<RunVoucherDto?> VoucherAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var found = await posting.FindAsync(SourceModule, DocType, [runId], ct);
        return found.TryGetValue(runId, out var v) ? new RunVoucherDto(v.VoucherId, v.TempNo, v.VoucherNo, v.IsPermanent) : null;
    }

    private async Task<PostingRequest> PostingRequestAsync(PayRun run, CancellationToken ct)
    {
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var accounts = await db.Accounts.ToDictionaryAsync(a => a.Id, ct);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var (_, end) = Period(run);
        var title = $"حقوق {run.Title} {employees.Values.FirstOrDefault()?.WorkshopTitle}".Trim();

        // سابقه ذخیره‌ها: ذخیره ماهانه در حقوق‌های قطعی دیگر و این ماه، منهای پرداخت‌های قبلی از محل همان حساب
        var provisionAccounts = items.Values.Where(i => i.AccountId is not null && i.Kind is PayItemKind.Provision or PayItemKind.Earning)
            .Select(i => i.AccountId!.Value).Distinct().ToList();
        var history = await db.Runs.AsNoTracking().Where(r => r.Id != run.Id && r.Status == PayRunStatus.Final)
            .SelectMany(r => r.Lines.Where(l => ids.Contains(l.EmployeeId))).ToListAsync(ct);
        decimal ProvisionBalance(int employeeId, int account)
        {
            decimal Sum(IEnumerable<RunLine> lines, PayItemKind kind) => lines
                .Where(l => l.EmployeeId == employeeId && items[l.PayItemId].AccountId == account && items[l.PayItemId].Kind == kind).Sum(l => l.Amount);
            return Sum(history, PayItemKind.Provision) + Sum(run.Lines, PayItemKind.Provision) - Sum(history, PayItemKind.Earning);
        }

        var expense = new Dictionary<(int Factor, int Center), decimal>();
        var credits = new Dictionary<(string Key, int? Account, int? Person), decimal>();
        foreach (var a in run.Attendances)
        {
            var e = employees[a.EmployeeId];
            var mine = run.Lines.Where(l => l.EmployeeId == a.EmployeeId).Select(l => (Item: items[l.PayItemId], l.Amount)).ToList();
            var net = mine.Where(x => x.Item.Kind == PayItemKind.Earning).Sum(x => x.Amount)
                      - mine.Where(x => x.Item.Kind == PayItemKind.Deduction).Sum(x => x.Amount);
            if (net < 0) throw new DomainException($"خالص پرداختی {e.PersonnelNo} {e.DisplayName} منفی است ({net:#,0}).");
            Add(credits, ("net", null, e.PersonId), net);

            foreach (var (item, amount) in mine.Where(x => x.Amount != 0))
            {
                switch (item.Kind)
                {
                    case PayItemKind.Earning or PayItemKind.EmployerShare or PayItemKind.Provision:
                        var factor = item.CostFactorId ?? throw new DomainException($"قلم «{item.Title}» عامل هزینه ندارد؛ در «اقلام حقوق» تعیین کنید.");
                        var cost = amount;
                        if (item.Kind == PayItemKind.Earning && item.AccountId is int provisionAccount)
                        {
                            // پرداخت از محل ذخیره (عیدی، سنوات): کل مانده ذخیره کارمند آزاد می‌شود و اختلاف (مثبت یا منفی) هزینه است
                            var balance = ProvisionBalance(e.Id, provisionAccount);
                            if (balance > 0)
                            {
                                Debit("provision_use", provisionAccount, balance, e);
                                cost = amount - balance;
                            }
                        }
                        foreach (var (center, share) in Split(cost, a.Centers))
                            expense[(factor, center)] = expense.GetValueOrDefault((factor, center)) + share;
                        if (item.Kind != PayItemKind.Earning) Credit("liability", item, amount, e);
                        break;
                    case PayItemKind.Deduction:
                        Credit("deduction", item, amount, e);
                        break;
                }
            }
        }

        var lines = new List<PostingLine>();
        lines.AddRange(expense.Where(x => x.Value != 0).Select(x => new PostingLine("expense", x.Value,
            [new PostingDetail(PostingDetailKind.CostFactor, x.Key.Factor), new PostingDetail(PostingDetailKind.CostCenter, x.Key.Center)], title)));
        lines.AddRange(credits.Where(x => x.Value != 0).Select(x => new PostingLine(x.Key.Key, x.Value,
            x.Key.Person is int p ? [new PostingDetail(PostingDetailKind.Person, p)] : [], title, x.Key.Account)));
        if (lines.Count == 0) throw new DomainException("حقوق این ماه مبلغی برای سند ندارد.");
        return new PostingRequest(SourceModule, DocType, run.Id, end, title, lines);

        void Debit(string key, int accountId, decimal amount, EmployeeRef e)
        {
            var account = accounts.GetValueOrDefault(accountId) ?? throw new DomainException("حساب ذخیره پیدا نشد.");
            Add(credits, (key, account.Id, account.HasDetails ? e.PersonId : null), amount);
        }

        void Credit(string key, PayItem item, decimal amount, EmployeeRef e)
        {
            var account = item.AccountId is int id ? accounts.GetValueOrDefault(id) : null;
            if (account is null) throw new DomainException($"قلم «{item.Title}» حساب بستانکار ندارد؛ در «اقلام حقوق» تعیین کنید.");
            // معین بدون تفصیلی (مثل حق بیمه و مالیات پرداختنی) تجمیعی؛ بقیه به تفکیک کارمند
            Add(credits, (key, account.Id, account.HasDetails ? e.PersonId : null), amount);
        }

        static void Add<TKey>(Dictionary<TKey, decimal> map, TKey key, decimal amount) where TKey : notnull =>
            map[key] = map.GetValueOrDefault(key) + amount;
    }

    /// <summary>تقسیم مبلغ بین مراکز به درصد؛ آخرین مرکز باقی‌مانده را می‌گیرد تا جمع دقیق بماند.</summary>
    private static IEnumerable<(int Center, decimal Amount)> Split(decimal amount, IReadOnlyList<AttendanceCenter> centers)
    {
        var rest = amount;
        for (var i = 0; i < centers.Count; i++)
        {
            var share = i == centers.Count - 1 ? rest : Math.Round(amount * centers[i].Percent / 100, 0, MidpointRounding.AwayFromZero);
            rest -= share;
            yield return (centers[i].CostCenterId, share);
        }
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
        var cap = await DailyCapAsync(run.Year, ct);
        return run.Attendances.Select(a =>
            {
                var e = employees[a.EmployeeId];
                var mine = run.Lines.Where(l => l.EmployeeId == a.EmployeeId).Select(l => (Item: items[l.PayItemId], l.Amount)).ToList();
                decimal Sum(Func<PayItem, bool> f) => mine.Where(x => f(x.Item)).Sum(x => x.Amount);
                var amounts = mine.ToDictionary(x => x.Item.Id, x => x.Amount);
                var insurance = InsuranceBase.Compute(items.Values, i => amounts.GetValueOrDefault(i.Id), a.InsuranceDays, cap);
                var earnings = Sum(i => i.Kind == PayItemKind.Earning);
                var deductions = Sum(i => i.Kind == PayItemKind.Deduction);
                var ins = Sum(i => i.Code == InsuranceWorkerCode);
                var tax = Sum(i => i.Source == PayItemSource.TaxOffice);
                return new RunSummaryRowDto(e.Id, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays, earnings,
                    e.InsuranceStatus == InsuranceStatus.NotInsured ? 0 : insurance.Subject, Sum(i => i.Kind == PayItemKind.Earning && i.TaxSubject),
                    ins, tax, deductions - ins - tax, earnings - deductions, Sum(i => i.Kind == PayItemKind.EmployerShare),
                    Sum(i => i.Kind == PayItemKind.Provision));
            })
            .OrderBy(r => r.PersonnelNo).ToList();
    }

    /// <summary>فیش همه کارکنان یک اجرا (برای چاپ).</summary>
    public async Task<List<PayslipDto>> PayslipsAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.RunView);
        var ids = await db.Runs.Where(r => r.Id == runId).SelectMany(r => r.Attendances.Select(a => a.EmployeeId)).ToListAsync(ct);
        var result = new List<PayslipDto>();
        foreach (var id in ids) result.Add(await PayslipAsync(runId, id, ct));
        return result.OrderBy(p => p.PersonnelNo).ToList();
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
        var amounts = mine.ToDictionary(x => x.Item.Id, x => x.Amount);
        var insurance = e.InsuranceStatus == InsuranceStatus.NotInsured ? 0
            : InsuranceBase.Compute(items.Values, i => amounts.GetValueOrDefault(i.Id), a.InsuranceDays, await DailyCapAsync(run.Year, ct)).Subject;
        return new PayslipDto(run.Title, e.WorkshopTitle, e.PersonnelNo, e.DisplayName, e.NationalId, a.WorkDays, a.InsuranceDays, a.OtHours,
            a.NightHours, a.HolidayHours, a.AbsentDays, Of(PayItemKind.Earning), Of(PayItemKind.Deduction),
            mine.Where(x => x.Item.Kind == PayItemKind.EmployerShare).Select(x => new PayslipLineDto(x.Item.Code, x.Item.Title, x.Amount)).ToList(),
            earnings, deductions, earnings - deductions, insurance);
    }

    /// <summary>سقف روزانه مشمول بیمه سال: حداقل مزد روزانه × ضریب سقف.</summary>
    private async Task<decimal> DailyCapAsync(short year, CancellationToken ct)
    {
        var p = await db.YearParams.AsNoTracking().Where(x => x.Year == year && (x.Code == "MIN_WAGE" || x.Code == "INS_CAP_FACTOR"))
            .ToDictionaryAsync(x => x.Code, x => x.Value, ct);
        return p.GetValueOrDefault("MIN_WAGE") * p.GetValueOrDefault("INS_CAP_FACTOR");
    }

    // ---------------- کمکی ----------------

    private async Task<PayRun> LoadAsync(int runId, CancellationToken ct) =>
        await db.Runs.Include(r => r.Attendances).ThenInclude(a => a.Centers)
            .Include(r => r.Attendances).ThenInclude(a => a.Inputs).Include(r => r.Lines).Include(r => r.LoanDeductions)
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
