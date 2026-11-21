using Erp.SharedKernel.Domain;

namespace Erp.Payroll.Domain;

/// <summary>داده‌های یک کارمند برای محاسبه ماه.</summary>
public sealed record PayCalcContext(
    decimal Days, int InsuranceDays, int MonthDays, decimal OtHours, decimal NightHours, decimal HolidayHours, decimal AbsentDays,
    int Children, bool Married, decimal EmployeeRate, decimal EmployerRate, decimal UnemploymentRate,
    int Month = 1, bool Settlement = false, decimal YearDays = 360, decimal YearWorkDays = 0, decimal ServiceDays = 0, decimal LeaveBalance = 0);

public sealed record PayCalcResult(IReadOnlyDictionary<int, decimal> Amounts, IReadOnlyList<string> Warnings)
{
    public decimal Total(IEnumerable<PayItem> items, PayItemKind kind) =>
        items.Where(i => i.Kind == kind).Sum(i => Amounts.GetValueOrDefault(i.Id));
}

/// <summary>
/// محاسبه حقوق یک کارمند در یک ماه، بدون دسترسی به دیتابیس.
/// ۱) اقلام حکمی از حکم، اقلام ورودی (ورود ماهانه، وام و مساعده، مالیات سامانه) از کارکرد؛
/// ۲) فرمول‌هایی که به جمع مشمول وابسته نیستند به ترتیب وابستگی؛ ۳) INS_SUBJECT = دستمزد بیمه (مزد و سنوات روزانه × روز بیمه) + مزایای مشمول، TAX_SUBJECT = جمع مزایای مشمول مالیات؛
/// ۴) بقیه فرمول‌ها (بیمه، ...). اقلام غیر اطلاعاتی به ریال گرد می‌شوند.
/// متغیر تعریف‌نشده یا خطای فرمول: صفر لحاظ و هشدار (اطلاعات ناقص بعد از محاسبه گزارش می‌شود).
/// </summary>
public sealed class PayCalculator
{
    private readonly List<PayItem> _items;
    private readonly Dictionary<string, Formula> _formulas;
    private readonly List<string> _phase1 = [];
    private readonly List<string> _phase2 = [];

    public PayCalculator(IEnumerable<PayItem> activeItems)
    {
        _items = activeItems.ToList();
        _formulas = _items.Where(i => i.Formula is not null).ToDictionary(i => i.Code, i => i.ParsedFormula()!);
        var order = FormulaOrder.Sort(_formulas);

        var depends = new Dictionary<string, bool>();
        bool Depends(string code)
        {
            if (depends.TryGetValue(code, out var known)) return known;
            depends[code] = false;
            var result = _formulas.TryGetValue(code, out var f) &&
                         f.Variables.Any(v => PayVariables.Aggregates.Contains(v) || (_formulas.ContainsKey(v) && Depends(v)));
            return depends[code] = result;
        }
        foreach (var code in order) (Depends(code) ? _phase2 : _phase1).Add(code);
    }

    public IReadOnlyList<PayItem> Items => _items;

    /// <param name="parameters">پارامترهای سال (کد ← مقدار)</param>
    /// <param name="decree">مبلغ اقلام حکمی (شناسه قلم ← مبلغ)</param>
    /// <param name="inputs">ورودی‌های ماه (شناسه قلم ← مبلغ)</param>
    public PayCalcResult Calculate(PayCalcContext ctx, IReadOnlyDictionary<string, decimal> parameters,
        IReadOnlyDictionary<int, decimal> decree, IReadOnlyDictionary<int, decimal> inputs)
    {
        var warnings = new List<string>();
        var byCode = _items.ToDictionary(i => i.Code);
        var values = new Dictionary<string, decimal>();

        foreach (var item in _items)
        {
            switch (item.Source)
            {
                case PayItemSource.Decree:
                    values[item.Code] = decree.GetValueOrDefault(item.Id);
                    break;
                case PayItemSource.MonthlyInput or PayItemSource.Loan or PayItemSource.TaxOffice:
                    values[item.Code] = inputs.GetValueOrDefault(item.Id);
                    break;
            }
        }

        var builtin = new Dictionary<string, decimal>
        {
            ["DAYS"] = ctx.Days, ["INS_DAYS"] = ctx.InsuranceDays, ["MONTH_DAYS"] = ctx.MonthDays,
            ["OT_HOURS"] = ctx.OtHours, ["NIGHT_HOURS"] = ctx.NightHours, ["HOLIDAY_HOURS"] = ctx.HolidayHours,
            ["ABSENT_DAYS"] = ctx.AbsentDays, ["CHILDREN"] = ctx.Children, ["MARRIED"] = ctx.Married ? 1 : 0,
            ["INS_EMPLOYEE_RATE"] = ctx.EmployeeRate, ["INS_EMPLOYER_RATE"] = ctx.EmployerRate,
            ["INS_UNEMPLOYMENT_RATE"] = ctx.UnemploymentRate,
            ["MONTH"] = ctx.Month, ["SETTLEMENT"] = ctx.Settlement ? 1 : 0, ["YEAR_DAYS"] = ctx.YearDays,
            ["YEAR_WORK_DAYS"] = ctx.YearWorkDays, ["SERVICE_DAYS"] = ctx.ServiceDays, ["LEAVE_BALANCE"] = ctx.LeaveBalance
        };
        var missing = new HashSet<string>();

        decimal? Resolve(string name)
        {
            if (builtin.TryGetValue(name, out var b)) return b;
            if (values.TryGetValue(name, out var v)) return v;
            if (parameters.TryGetValue(name, out var p)) return p;
            if (byCode.ContainsKey(name)) return 0; // قلم غیرفعال یا بدون مقدار
            if (missing.Add(name)) warnings.Add($"متغیر «{name}» (پارامتر سال یا قلم) تعریف نشده؛ صفر لحاظ شد.");
            return 0;
        }

        void Run(IEnumerable<string> codes)
        {
            foreach (var code in codes)
            {
                var item = byCode[code];
                decimal result;
                try { result = _formulas[code].Evaluate(Resolve); }
                catch (DomainException ex)
                {
                    warnings.Add($"«{item.Title}»: {ex.Message} صفر لحاظ شد.");
                    result = 0;
                }
                if (result < 0)
                {
                    warnings.Add($"«{item.Title}» منفی شد ({result:#,0}); صفر لحاظ شد.");
                    result = 0;
                }
                values[code] = item.Kind == PayItemKind.Info ? result : Math.Round(result, 0, MidpointRounding.AwayFromZero);
            }
        }

        // دستمزد بیمه به مزد حکم بستگی دارد، نه به مزایا؛ پیش از فرمول‌ها معلوم است
        builtin["INS_WAGE"] = InsuranceBase.Compute(_items, i => values.GetValueOrDefault(i.Code), ctx.InsuranceDays, 0).Wage;
        Run(_phase1);
        var insurance = InsuranceBase.Compute(_items, i => values.GetValueOrDefault(i.Code), ctx.InsuranceDays, 0);
        builtin["INS_BENEFITS"] = insurance.Benefits;
        builtin["INS_SUBJECT"] = insurance.Wage + insurance.Benefits;
        builtin["TAX_SUBJECT"] = _items.Where(i => i.Kind == PayItemKind.Earning && i.TaxSubject).Sum(i => values.GetValueOrDefault(i.Code));
        Run(_phase2);

        var amounts = _items.ToDictionary(i => i.Id, i => values.GetValueOrDefault(i.Code));
        var net = _items.Where(i => i.Kind == PayItemKind.Earning).Sum(i => amounts[i.Id])
                  - _items.Where(i => i.Kind == PayItemKind.Deduction).Sum(i => amounts[i.Id]);
        if (net < 0) warnings.Add($"خالص پرداختی منفی است ({net:#,0}).");
        return new PayCalcResult(amounts, warnings);
    }
}
