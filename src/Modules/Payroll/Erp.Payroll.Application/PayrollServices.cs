using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>
/// اقلام حقوق با فرمول. کنترل‌ها: متغیرهای فرمول شناخته‌شده باشند، دور نداشته باشند، و مزایا به جمع مشمول بیمه/مالیات
/// (مستقیم یا از طریق قلم دیگر) وابسته نباشد؛ چون آن جمع‌ها از خود مزایا ساخته می‌شوند.
/// </summary>
public class PayItemService(IPayrollDbContext db, IAccessControl access)
{
    public async Task<List<PayItemDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var factors = await db.Factors.ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        var accounts = await db.Accounts.ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Title}", ct);
        return (await db.PayItems.AsNoTracking().Where(i => includeInactive || i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct))
            .Select(i => new PayItemDto(i.Id, i.Code, i.Title, i.Kind, i.Source, i.Formula, i.InsuranceSubject, i.TaxSubject,
                i.CostFactorId, i.CostFactorId is int f ? factors.GetValueOrDefault(f) : null,
                i.AccountId, i.AccountId is int a ? accounts.GetValueOrDefault(a) : null,
                i.OnPayslip, i.SortOrder, i.IsSystem, i.IsActive, i.InsuranceWage, i.TaxField)).ToList();
    }

    /// <summary>معین‌های قابل انتخاب برای کسور، سهم کارفرما و ذخیره.</summary>
    public Task<List<AccountRef>> AccountsAsync(CancellationToken ct) => db.Accounts.OrderBy(a => a.Code).ToListAsync(ct);

    /// <summary>عوامل هزینه برای مزایا، سهم کارفرما و ذخیره.</summary>
    public Task<List<FactorRef>> FactorsAsync(CancellationToken ct) => db.Factors.OrderBy(f => f.Nature).ThenBy(f => f.Title).ToListAsync(ct);

    /// <summary>همه متغیرهای قابل استفاده در فرمول (آماده، پارامترهای سال، کد اقلام).</summary>
    public async Task<List<FormulaVariableDto>> VariablesAsync(CancellationToken ct)
    {
        var list = PayVariables.Builtin.Select(v => new FormulaVariableDto(v.Key, v.Value, "کارکرد و پرونده")).ToList();
        var parameters = await db.YearParams.AsNoTracking().OrderByDescending(p => p.Year).ToListAsync(ct);
        list.AddRange(parameters.GroupBy(p => p.Code).Select(g => new FormulaVariableDto(g.Key, g.First().Title, "پارامترهای سال")).OrderBy(v => v.Code));
        list.AddRange((await db.PayItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ToListAsync(ct))
            .Select(i => new FormulaVariableDto(i.Code, i.Title, "اقلام حقوق")));
        return list;
    }

    /// <summary>کنترل فرمول پیش از ذخیره (برای فرم).</summary>
    public async Task<FormulaCheckDto> CheckFormulaAsync(int? id, SavePayItemRequest r, CancellationToken ct)
    {
        try
        {
            var item = id is int existing ? await db.PayItems.AsNoTracking().FirstAsync(i => i.Id == existing, ct) : null;
            var candidate = Build(item, r);
            await ValidateFormulasAsync(candidate, ct);
            return new FormulaCheckDto(true, null, candidate.ParsedFormula()?.Variables.Order().ToList() ?? []);
        }
        catch (DomainException ex)
        {
            return new FormulaCheckDto(false, ex.Message, []);
        }
    }

    public async Task<int> CreateAsync(SavePayItemRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var item = Build(null, r);
        await ValidateAsync(item, null, ct);
        db.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task UpdateAsync(int id, SavePayItemRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var item = await db.PayItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("قلم حقوق پیدا نشد.");
        var oldCode = item.Code;
        var oldSource = item.Source;
        Apply(item, r);
        await ValidateAsync(item, oldCode, ct);
        if (oldSource == PayItemSource.Decree && item.Source != PayItemSource.Decree && await db.Decrees.AnyAsync(d => d.Items.Any(x => x.PayItemId == id), ct))
            throw new DomainException($"قلم «{item.Title}» در حکم‌ها آمده؛ منبعش از «حکم» عوض نمی‌شود.");
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var item = await db.PayItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("قلم حقوق پیدا نشد.");
        if (item.IsSystem) throw new DomainException($"قلم «{item.Title}» قانونی (سیستمی) است و حذف نمی‌شود.");
        if (await db.Decrees.AnyAsync(d => d.Items.Any(x => x.PayItemId == id), ct))
            throw new DomainException($"قلم «{item.Title}» در حکم‌ها آمده و حذف نمی‌شود؛ غیرفعالش کنید.");
        var users = (await db.PayItems.AsNoTracking().Where(i => i.Id != id && i.Formula != null).ToListAsync(ct))
            .Where(i => i.ParsedFormula()!.Variables.Contains(item.Code)).Select(i => i.Title).ToList();
        if (users.Count > 0) throw new DomainException($"کد {item.Code} در فرمول «{string.Join("، ", users)}» آمده و حذف نمی‌شود.");
        db.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>قلم تازه، یا قلم موجودِ بدون ردیابی (AsNoTracking) با مقادیر درخواست.</summary>
    private static PayItem Build(PayItem? untracked, SavePayItemRequest r)
    {
        var item = untracked ?? new PayItem(r.Code, r.Kind, r.Source);
        Apply(item, r);
        return item;
    }

    private static void Apply(PayItem item, SavePayItemRequest r)
    {
        item.Redefine(r.Code, r.Kind, r.Source);
        item.Update(r.Title, r.Formula, r.InsuranceSubject, r.TaxSubject, r.CostFactorId, r.AccountId, r.OnPayslip, r.SortOrder,
            r.InsuranceWage, r.TaxField);
        item.SetActive(r.IsActive);
    }

    private async Task ValidateAsync(PayItem item, string? oldCode, CancellationToken ct)
    {
        if (await db.PayItems.AnyAsync(i => i.Id != item.Id && i.Code == item.Code, ct))
            throw new DomainException($"کد قلم {item.Code} قبلاً تعریف شده است.");
        if (await db.PayItems.AnyAsync(i => i.Id != item.Id && i.Title == item.Title, ct))
            throw new DomainException($"قلم «{item.Title}» قبلاً تعریف شده است.");
        if (await db.YearParams.AnyAsync(p => p.Code == item.Code, ct))
            throw new DomainException($"کد {item.Code} کد یک پارامتر سال است؛ کد دیگری بگذارید.");

        if (item.CostFactorId is int factorId)
        {
            var factor = await db.Factors.FirstOrDefaultAsync(f => f.Id == factorId, ct) ?? throw new DomainException("عامل هزینه پیدا نشد.");
            if (!factor.IsActive) throw new DomainException($"عامل هزینه «{factor.Title}» غیرفعال است.");
        }
        if (item.AccountId is int accountId)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct) ?? throw new DomainException("حساب معین پیدا نشد.");
            if (!account.IsActive) throw new DomainException($"حساب {account.Code} غیرفعال است.");
        }
        switch (item.Kind)
        {
            case PayItemKind.Earning when item.CostFactorId is null:
                throw new DomainException("مزایا عامل هزینه لازم دارد (سند هزینه حقوق با آن صادر می‌شود).");
            case PayItemKind.Deduction when item.AccountId is null:
                throw new DomainException("کسور حساب بستانکار لازم دارد.");
            case PayItemKind.EmployerShare or PayItemKind.Provision when item.CostFactorId is null || item.AccountId is null:
                throw new DomainException("سهم کارفرما و ذخیره هم عامل هزینه و هم حساب بستانکار لازم دارند.");
        }

        if (oldCode is not null && oldCode != item.Code)
        {
            var users = (await db.PayItems.AsNoTracking().Where(i => i.Id != item.Id && i.Formula != null).ToListAsync(ct))
                .Where(i => i.ParsedFormula()!.Variables.Contains(oldCode)).Select(i => i.Title).ToList();
            if (users.Count > 0) throw new DomainException($"کد {oldCode} در فرمول «{string.Join("، ", users)}» آمده؛ اول آن فرمول‌ها را اصلاح کنید.");
        }
        await ValidateFormulasAsync(item, ct);
    }

    private async Task ValidateFormulasAsync(PayItem candidate, CancellationToken ct)
    {
        var others = await db.PayItems.AsNoTracking().Where(i => i.Id != candidate.Id || candidate.Id == 0).ToListAsync(ct);
        if (candidate.Id == 0) others = others.Where(i => i.Code != candidate.Code).ToList();
        var all = others.Append(candidate).ToList();
        var paramCodes = (await db.YearParams.Select(p => p.Code).Distinct().ToListAsync(ct)).ToHashSet();
        var itemCodes = all.Select(i => i.Code).ToHashSet();

        if (candidate.ParsedFormula() is { } f)
        {
            foreach (var v in f.Variables)
            {
                if (v == candidate.Code) throw new DomainException($"فرمول «{candidate.Title}» به خودش ارجاع دارد.");
                if (!PayVariables.Builtin.ContainsKey(v) && !paramCodes.Contains(v) && !itemCodes.Contains(v))
                    throw new DomainException($"متغیر «{v}» در فرمول شناخته نیست (نه متغیر آماده، نه پارامتر سال، نه کد قلم).");
            }
        }

        var formulas = all.Where(i => i.Formula is not null).ToDictionary(i => i.Code, i => i.ParsedFormula()!);
        FormulaOrder.Sort(formulas);

        // مزایا نباید (حتی غیرمستقیم) به جمع مشمول بیمه/مالیات وابسته باشد
        var dependsOnTotals = new Dictionary<string, bool>();
        bool Depends(string code)
        {
            if (dependsOnTotals.TryGetValue(code, out var known)) return known;
            dependsOnTotals[code] = false;
            var result = formulas.TryGetValue(code, out var fx) &&
                         fx.Variables.Any(v => PayVariables.Aggregates.Contains(v) || (formulas.ContainsKey(v) && Depends(v)));
            return dependsOnTotals[code] = result;
        }
        foreach (var earning in all.Where(i => i.Kind is PayItemKind.Earning or PayItemKind.Info && i.Formula is not null))
            if (Depends(earning.Code))
                throw new DomainException($"فرمول «{earning.Title}» به جمع مشمول بیمه یا مالیات وابسته است؛ مزایا خودش جزء همان جمع است.");
    }
}

/// <summary>پارامترهای قانونی هر سال (حداقل مزد، حق مسکن، ...). فهرست آزاد است.</summary>
public class YearParamService(IPayrollDbContext db, IAccessControl access)
{
    public async Task<List<short>> YearsAsync(CancellationToken ct) =>
        await db.YearParams.Select(p => p.Year).Distinct().OrderByDescending(y => y).ToListAsync(ct);

    public async Task<List<YearParamDto>> ListAsync(short year, CancellationToken ct) =>
        (await db.YearParams.AsNoTracking().Where(p => p.Year == year).OrderBy(p => p.Code).ToListAsync(ct))
            .Select(p => new YearParamDto(p.Year, p.Code, p.Title, p.Value)).ToList();

    public async Task SaveAsync(short year, SaveYearParamRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var probe = new YearParam(year, r.Code);
        var row = await db.YearParams.FirstOrDefaultAsync(p => p.Year == year && p.Code == probe.Code, ct);
        if (row is null)
        {
            if (await db.PayItems.AnyAsync(i => i.Code == probe.Code, ct))
                throw new DomainException($"کد {probe.Code} کد یک قلم حقوق است؛ کد دیگری بگذارید.");
            row = probe;
            db.Add(row);
        }
        row.Update(r.Title, r.Value);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(short year, string code, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var row = await db.YearParams.FirstOrDefaultAsync(p => p.Year == year && p.Code == code, ct) ?? throw new NotFoundException("پارامتر پیدا نشد.");
        if (!await db.YearParams.AnyAsync(p => p.Code == code && p.Year != year, ct))
        {
            var users = (await db.PayItems.AsNoTracking().Where(i => i.Formula != null).ToListAsync(ct))
                .Where(i => i.ParsedFormula()!.Variables.Contains(code)).Select(i => i.Title).ToList();
            if (users.Count > 0) throw new DomainException($"پارامتر {code} در فرمول «{string.Join("، ", users)}» آمده و حذف نمی‌شود.");
        }
        db.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>پارامترهای یک سال را به سال دیگر کپی می‌کند (فقط کدهایی که آنجا نیستند). تعداد کپی‌شده.</summary>
    public async Task<int> CopyAsync(short fromYear, short toYear, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        if (fromYear == toYear) throw new DomainException("سال مبدأ و مقصد یکی است.");
        var source = await db.YearParams.AsNoTracking().Where(p => p.Year == fromYear).ToListAsync(ct);
        if (source.Count == 0) throw new DomainException($"سال {fromYear} پارامتری ندارد.");
        var existing = (await db.YearParams.Where(p => p.Year == toYear).Select(p => p.Code).ToListAsync(ct)).ToHashSet();
        var count = 0;
        foreach (var p in source.Where(p => !existing.Contains(p.Code)))
        {
            var copy = new YearParam(toYear, p.Code);
            copy.Update(p.Title, p.Value);
            db.Add(copy);
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }
}

public class PaySettingsService(IPayrollDbContext db, IAccessControl access)
{
    public async Task<PaySettingsDto> GetAsync(CancellationToken ct)
    {
        var s = await db.Settings.AsNoTracking().SingleAsync(ct);
        return new PaySettingsDto(s.MonthDaysMode);
    }

    public async Task SaveAsync(PaySettingsDto dto, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.Setup);
        var s = await db.Settings.SingleAsync(ct);
        s.Update(dto.MonthDaysMode);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// حکم حقوقی: مقدار اقلام «حکمی» (مزد روزانه، پایه سنوات، ...) از تاریخ اجرا تا حکم بعدی.
/// یک حکم در هر تاریخ برای هر کارمند؛ تاریخ اجرا از استخدام تا ترک کار.
/// </summary>
public class DecreeService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock)
{
    public const string DailyWageCode = "DAILY_WAGE";

    public async Task<List<DecreeListItemDto>> ListAsync(int? employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeView);
        var query = db.Decrees.AsNoTracking().Include(d => d.Items).AsQueryable();
        if (employeeId is int e) query = query.Where(d => d.EmployeeId == e);
        var decrees = await query.OrderByDescending(d => d.EffectiveDate).ThenByDescending(d => d.DocNo).ToListAsync(ct);
        var ids = decrees.Select(d => d.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var wageId = await db.PayItems.Where(i => i.Code == DailyWageCode).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        return decrees.Select(d => new DecreeListItemDto(d.Id, d.DocNo, d.EmployeeId, employees[d.EmployeeId].PersonnelNo,
            employees[d.EmployeeId].DisplayName, PersianDate.ToPersian(d.EffectiveDate), d.DecreeType,
            d.Items.FirstOrDefault(i => i.PayItemId == wageId)?.Amount ?? 0, d.Description)).ToList();
    }

    /// <summary>کارمندان برای انتخاب در فرم حکم (کسانی که ترک کار نکرده‌اند، به‌علاوه includeId).</summary>
    public async Task<List<EmployeeOptionDto>> EmployeesAsync(int? includeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeView);
        return await db.Employees.Where(e => e.Status != EmployeeStatus.Left || e.Id == includeId).OrderBy(e => e.PersonnelNo)
            .Select(e => new EmployeeOptionDto(e.Id, e.PersonnelNo, e.DisplayName, e.Status)).ToListAsync(ct);
    }

    public async Task<DecreeDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeView);
        var d = await db.Decrees.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("حکم پیدا نشد.");
        return await ToDtoAsync(d.Id, d.DocNo, d.EmployeeId, PersianDate.ToPersian(d.EffectiveDate), d.DecreeType, d.Description,
            d.Items.ToDictionary(i => i.PayItemId, i => i.Amount), ct);
    }

    /// <summary>پیش‌نویس حکم تازه با مبالغ آخرین حکم کارمند (همه اقلام حکمی فعال، حتی صفر).</summary>
    public async Task<DecreeDto> DraftAsync(int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeView);
        var last = await db.Decrees.AsNoTracking().Include(x => x.Items).Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
        var type = last is null ? DecreeType.Hire : DecreeType.AnnualRaise;
        var date = last is null ? employee.HireDate : PersianDate.FromPersian(PersianDate.PersianYear(DateOnly.FromDateTime(clock.GetLocalNow().DateTime)), 1, 1);
        return await ToDtoAsync(0, 0, employeeId, PersianDate.ToPersian(date), type, null,
            last?.Items.ToDictionary(i => i.PayItemId, i => i.Amount) ?? [], ct);
    }

    public async Task<int> CreateAsync(SaveDecreeRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeCreate);
        var no = (await db.Decrees.MaxAsync(d => (int?)d.DocNo, ct) ?? 0) + 1;
        var d = new Decree(r.EmployeeId, no, user.UserId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(d, r, ct);
        db.Add(d);
        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    public async Task UpdateAsync(int id, SaveDecreeRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeEdit);
        var d = await db.Decrees.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("حکم پیدا نشد.");
        if (d.EmployeeId != r.EmployeeId) throw new DomainException("کارمند حکم عوض نمی‌شود.");
        await ApplyAsync(d, r, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.DecreeDelete);
        var d = await db.Decrees.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("حکم پیدا نشد.");
        db.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Decree d, SaveDecreeRequest r, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == r.EmployeeId, ct) ?? throw new DomainException("کارمند انتخاب نشده یا پیدا نشد.");
        var date = PersianDate.Parse(r.EffectiveDate);
        if (date < employee.HireDate)
            throw new DomainException($"تاریخ اجرای حکم پیش از تاریخ استخدام ({PersianDate.ToPersian(employee.HireDate)}) است.");
        if (employee.LeaveDate is { } left && date > left)
            throw new DomainException($"تاریخ اجرای حکم بعد از ترک کار ({PersianDate.ToPersian(left)}) است.");
        if (await db.Decrees.AnyAsync(x => x.Id != d.Id && x.EmployeeId == r.EmployeeId && x.EffectiveDate == date, ct))
            throw new DomainException($"برای {employee.DisplayName} در تاریخ {r.EffectiveDate} حکم دیگری ثبت شده است.");

        var ids = r.Items.Select(i => i.PayItemId).ToList();
        var items = await db.PayItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var rows = r.Items.Select(i => (items.GetValueOrDefault(i.PayItemId) ?? throw new DomainException("قلم حکم پیدا نشد."), i.Amount)).ToList();
        var wage = rows.FirstOrDefault(x => x.Item1.Code == DailyWageCode);
        if (wage.Item1 is null || wage.Amount <= 0) throw new DomainException("مزد روزانه در حکم الزامی است.");
        d.Update(date, r.DecreeType, r.Description, rows);
    }

    private async Task<DecreeDto> ToDtoAsync(int id, int no, int employeeId, string date, DecreeType type, string? description,
        Dictionary<int, decimal> amounts, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
        var items = await db.PayItems.AsNoTracking().Where(i => i.Source == PayItemSource.Decree && (i.IsActive || amounts.Keys.Contains(i.Id)))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct);
        return new DecreeDto(id, no, employeeId, employee.PersonnelNo, employee.DisplayName, date, type, description,
            items.Select(i => new DecreeItemDto(i.Id, i.Code, i.Title, amounts.GetValueOrDefault(i.Id))).ToList());
    }
}
