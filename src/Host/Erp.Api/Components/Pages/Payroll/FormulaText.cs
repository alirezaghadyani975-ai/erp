using Erp.Payroll.Application;
using Erp.Payroll.Domain;
using Erp.SharedKernel.Domain;

namespace Erp.Api.Components.Pages.Payroll;

/// <summary>نمایش فارسی فرمول ذخیره‌شده با کد: «ROUND(DAILY_WAGE * DAYS, 0)» ← «گرد(مزد روزانه × روزهای کارکرد ماه ، 0)».</summary>
public static class FormulaText
{
    private static readonly Dictionary<string, string> Functions = new()
    {
        ["IF"] = "اگر", ["MIN"] = "کمینه", ["MAX"] = "بیشینه", ["ROUND"] = "گرد", ["FLOOR"] = "کف", ["CEILING"] = "سقف", ["ABS"] = "قدر مطلق"
    };

    private static readonly Dictionary<string, string> Ops = new()
    {
        ["*"] = "×", ["/"] = "÷", ["-"] = "−", ["<>"] = "≠", ["<="] = "≤", [">="] = "≥"
    };

    public static string Persian(string? formula, IReadOnlyCollection<FormulaVariableDto> catalog)
    {
        if (string.IsNullOrWhiteSpace(formula)) return "";
        IReadOnlyList<(string Kind, string Text)> tokens;
        try { tokens = Formula.Split(formula); }
        catch (DomainException) { return formula; }
        var titles = catalog.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.First().Title);
        var parts = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var (kind, text) = tokens[i];
            parts.Add(kind switch
            {
                "name" when i + 1 < tokens.Count && tokens[i + 1].Kind == "lparen" && Functions.TryGetValue(text, out var fn) => fn,
                "name" => titles.GetValueOrDefault(text, text),
                "op" => Ops.GetValueOrDefault(text, text),
                "comma" => "،",
                _ => text
            });
        }
        // بدون فاصله بعد از «(» و پیش از «)» و «،»
        return string.Join(" ", parts).Replace("( ", "(").Replace(" )", ")").Replace(" ،", "،");
    }
}
