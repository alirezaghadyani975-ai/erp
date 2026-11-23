using Erp.Payroll.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>مقدار نمونه پارامترها برای آزمایش فرمول؛ Source توضیح می‌دهد مقدارها از کجا آمده‌اند.</summary>
public sealed record FormulaSampleDto(string Source, Dictionary<string, decimal> Values);

/// <summary>
/// پشتیبان فرمول‌ساز: فهرست پارامترها با نام فارسی و مقدار نمونه برای آزمایش فرمول روی یک کارمند
/// (کارکرد و مبلغ اقلام از آخرین حقوق محاسبه‌شده او، پارامترهای سال و قواعد گروه الگو).
/// </summary>
public class PayFormulaService(IPayrollDbContext db, IAccessControl access)
{
    private static readonly Dictionary<string, string> BuiltinCategory = new()
    {
        ["DAYS"] = "کارکرد ماه", ["INS_DAYS"] = "کارکرد ماه", ["MONTH_DAYS"] = "کارکرد ماه", ["OT_HOURS"] = "کارکرد ماه",
        ["NIGHT_HOURS"] = "کارکرد ماه", ["HOLIDAY_HOURS"] = "کارکرد ماه", ["ABSENT_DAYS"] = "کارکرد ماه", ["MONTH"] = "کارکرد ماه",
        ["SETTLEMENT"] = "کارکرد ماه", ["YEAR_DAYS"] = "کارکرد ماه", ["YEAR_WORK_DAYS"] = "کارکرد ماه",
        ["CHILDREN"] = "پرونده و کارگاه", ["MARRIED"] = "پرونده و کارگاه", ["SERVICE_DAYS"] = "پرونده و کارگاه",
        ["LEAVE_BALANCE"] = "پرونده و کارگاه", ["INS_EMPLOYEE_RATE"] = "پرونده و کارگاه", ["INS_EMPLOYER_RATE"] = "پرونده و کارگاه",
        ["INS_UNEMPLOYMENT_RATE"] = "پرونده و کارگاه",
        ["INS_SUBJECT"] = "جمع‌ها", ["TAX_SUBJECT"] = "جمع‌ها", ["INS_WAGE"] = "جمع‌ها", ["INS_BENEFITS"] = "جمع‌ها",
    };

    public async Task<List<FormulaVariableDto>> CatalogAsync(CancellationToken ct)
    {
        var list = PayVariables.Builtin.Select(b => new FormulaVariableDto(b.Key, b.Value, BuiltinCategory.GetValueOrDefault(b.Key, "کارکرد ماه"))).ToList();
        var lastYear = await db.YearParams.MaxAsync(p => (short?)p.Year, ct);
        var yearParams = lastYear is short y ? await db.YearParams.Where(p => p.Year == y).OrderBy(p => p.Code).ToListAsync(ct) : [];
        list.AddRange(yearParams.Select(p => new FormulaVariableDto(p.Code, p.Title, "پارامترهای سال")));
        // قواعد گروه که پارامتر سال هم‌نام ندارند (قاعده استاندارد بدون مقدار سال، یا پارامتر اختصاصی گروه‌ها)
        var known = list.Select(v => v.Code).ToHashSet();
        list.AddRange(GroupRules.Standard.Where(r => !known.Contains(r.Code)).Select(r => new FormulaVariableDto(r.Code, r.Title, "قواعد گروه")));
        known = list.Select(v => v.Code).ToHashSet();
        var groupCodes = await db.Groups.SelectMany(g => g.Params.Select(p => p.Code)).Distinct().ToListAsync(ct);
        list.AddRange(groupCodes.Where(c => !known.Contains(c)).Order().Select(c => new FormulaVariableDto(c, c, "قواعد گروه")));
        var items = await db.PayItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct);
        list.AddRange(items.Select(i => new FormulaVariableDto(i.Code, i.Title, "اقلام حقوق")));
        return list;
    }

    /// <summary>
    /// مقدار نمونه: پارامترهای آخرین سال، جایگزین گروه الگو (اگر داده شد)، و اگر کارمند داده شد کارکرد، پرونده و مبلغ اقلام
    /// از آخرین حقوق محاسبه‌شده او. پارامتری که مقدار ندارد در نتیجه نیست (در فرمول‌ساز صفر و قابل ویرایش).
    /// </summary>
    public async Task<FormulaSampleDto> SampleAsync(int? employeeId, int? templateId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateView);
        var values = new Dictionary<string, decimal>();
        var source = new List<string>();
        var lastYear = await db.YearParams.MaxAsync(p => (short?)p.Year, ct);
        if (lastYear is short y)
        {
            foreach (var p in await db.YearParams.Where(p => p.Year == y).ToListAsync(ct)) values[p.Code] = p.Value;
            source.Add($"پارامترهای سال {y}");
        }
        if (templateId is int t && await db.Templates.Where(x => x.Id == t).Select(x => (int?)x.GroupId).FirstOrDefaultAsync(ct) is int groupId)
        {
            var group = await db.Groups.AsNoTracking().Include(g => g.Params).FirstAsync(g => g.Id == groupId, ct);
            foreach (var p in group.Params) values[p.Code] = p.Value;
            source.Add($"قواعد گروه «{group.Title}»");
        }
        if (employeeId is int e)
        {
            var emp = await db.Employees.FirstOrDefaultAsync(x => x.Id == e, ct);
            if (emp is not null)
            {
                (values["CHILDREN"], values["MARRIED"]) = (emp.ChildrenCount, emp.IsMarried ? 1 : 0);
                (values["INS_EMPLOYEE_RATE"], values["INS_EMPLOYER_RATE"], values["INS_UNEMPLOYMENT_RATE"]) = (emp.EmployeeRate, emp.EmployerRate, emp.UnemploymentRate);
                var run = await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines)
                    .Where(r => r.Attendances.Any(a => a.EmployeeId == e) && r.Status >= PayRunStatus.Calculated)
                    .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefaultAsync(ct);
                if (run is not null)
                {
                    var a = run.Attendances.First(x => x.EmployeeId == e);
                    (values["DAYS"], values["INS_DAYS"], values["MONTH_DAYS"], values["OT_HOURS"]) = (a.WorkDays, a.InsuranceDays, run.MonthDays, a.OtHours);
                    (values["NIGHT_HOURS"], values["HOLIDAY_HOURS"], values["ABSENT_DAYS"], values["MONTH"]) = (a.NightHours, a.HolidayHours, a.AbsentDays, run.Month);
                    values["SETTLEMENT"] = a.IsSettlement ? 1 : 0;
                    var codes = await db.PayItems.ToDictionaryAsync(i => i.Id, i => i.Code, ct);
                    foreach (var l in run.Lines.Where(l => l.EmployeeId == e)) values[codes[l.PayItemId]] = l.Amount;
                    source.Add($"حقوق {run.Title} {emp.DisplayName}");
                }
                else source.Add($"پرونده {emp.DisplayName} (حقوق محاسبه‌شده‌ای ندارد)");
            }
        }
        return new FormulaSampleDto(source.Count == 0 ? "بدون مقدار نمونه" : string.Join("، ", source), values);
    }
}
