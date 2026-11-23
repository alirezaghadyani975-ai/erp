using Erp.Payroll.Domain;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests;

/// <summary>موتور فرمول اقلام حقوق.</summary>
public class PayrollFormulaTests
{
    private static decimal Eval(string text, params (string Name, decimal Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value);
        return Formula.Parse(text).Evaluate(n => map.TryGetValue(n, out var v) ? v : null);
    }

    [Fact]
    public void Arithmetic_precedence_functions_and_comparisons()
    {
        Assert.Equal(14, Eval("2 + 3 * 4"));
        Assert.Equal(20, Eval("(2 + 3) * 4"));
        Assert.Equal(-1, Eval("-(3 - 2)"));
        Assert.Equal(3, Eval("min(5, 3, 9)"));
        Assert.Equal(1235, Eval("ROUND(1234.5, 0)"));
        Assert.Equal(1200, Eval("ROUND(1234, -2)"));
        Assert.Equal(1, Eval("2 >= 2"));
        Assert.Equal(10, Eval("IF(MARRIED = 1, 10, 0)", ("MARRIED", 1)));
        Assert.Equal(0, Eval("IF(MARRIED <> 1, 10, 0)", ("MARRIED", 1)));
        Assert.Equal(3, Eval("۱ + ۲")); // ارقام فارسی
    }

    [Fact]
    public void Legal_formulas_compute_expected_amounts()
    {
        // حق مسکن ۲۰ روز از ۳۰: ۳۰٬۰۰۰٬۰۰۰ × ۲۰ / ۳۰
        Assert.Equal(20_000_000, Eval("ROUND(HOUSING * DAYS / MONTH_DAYS, 0)", ("HOUSING", 30_000_000), ("DAYS", 20), ("MONTH_DAYS", 30)));
        // بیمه سهم کارگر با سقف ۷ برابر حداقل مزد
        var ins = Eval("ROUND(MIN(INS_SUBJECT, MIN_WAGE * INS_CAP_FACTOR * INS_DAYS) * INS_EMPLOYEE_RATE / 100, 0)",
            ("INS_SUBJECT", 2_000_000_000), ("MIN_WAGE", 5_541_850), ("INS_CAP_FACTOR", 7), ("INS_DAYS", 30), ("INS_EMPLOYEE_RATE", 7));
        Assert.Equal(Math.Round(5_541_850m * 7 * 30 * 7 / 100, 0), ins);
    }

    [Fact]
    public void Variables_are_collected_and_errors_are_persian()
    {
        Assert.Equal(["DAILY_WAGE", "DAYS"], Formula.Parse("round(daily_wage * days, 0)").Variables.Order());
        Assert.Contains("ناتمام", Assert.Throws<DomainException>(() => Formula.Parse("2 +")).Message);
        Assert.Contains("شناخته نیست", Assert.Throws<DomainException>(() => Formula.Parse("FOO(1)")).Message);
        Assert.Contains("مجاز نیست", Assert.Throws<DomainException>(() => Formula.Parse("2 ^ 3")).Message);
        Assert.Contains("تقسیم بر صفر", Assert.Throws<DomainException>(() => Eval("1 / 0")).Message);
        Assert.Contains("تعریف نشده", Assert.Throws<DomainException>(() => Eval("X + 1")).Message);
    }

    [Fact]
    public void Split_gives_formula_builder_tokens_with_same_rules()
    {
        var tokens = Formula.Split("round(daily_wage * ۳۰, 0) <> 5");
        Assert.Equal(["name", "lparen", "name", "op", "number", "comma", "number", "rparen", "op", "number"], tokens.Select(t => t.Kind));
        Assert.Equal(["ROUND", "(", "DAILY_WAGE", "*", "30", ",", "0", ")", "<>", "5"], tokens.Select(t => t.Text));
        Assert.Contains("IF", Formula.FunctionNames);
        Assert.Throws<DomainException>(() => Formula.Split("A # B"));
    }

    [Fact]
    public void Order_follows_dependencies_and_detects_cycles()
    {
        var order = FormulaOrder.Sort(new Dictionary<string, Formula>
        {
            ["C"] = Formula.Parse("A + B"), ["A"] = Formula.Parse("1"), ["B"] = Formula.Parse("A * 2")
        });
        Assert.Equal(["A", "B", "C"], order);
        var cycle = Assert.Throws<DomainException>(() => FormulaOrder.Sort(new Dictionary<string, Formula>
        {
            ["A"] = Formula.Parse("B + 1"), ["B"] = Formula.Parse("A + 1")
        }));
        Assert.Contains("دور", cycle.Message);
    }
}
