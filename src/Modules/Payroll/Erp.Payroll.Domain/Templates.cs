using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Payroll.Domain;

/// <summary>
/// قواعد استاندارد گروه استخدامی: پارامترهای سال که اقلام قانونی می‌خوانند و گروه می‌تواند برای خودش عوض کند.
/// هر پارامتر دیگری (کد لاتین) هم به‌عنوان پارامتر گروه پذیرفته می‌شود.
/// </summary>
public static class GroupRules
{
    public static readonly IReadOnlyList<(string Code, string Section, string Title)> Standard =
    [
        ("BONUS_DAYS", "عیدی", "روز مزد عیدی در سال"),
        ("BONUS_CAP_DAYS", "عیدی", "سقف عیدی (روز حداقل مزد)"),
        ("SEVERANCE_DAYS", "سنوات", "روز مزد سنوات به ازای هر سال"),
        ("LEAVE_DAYS", "مرخصی", "روز مرخصی استحقاقی سال"),
        ("DAY_HOURS", "کارکرد", "ساعت کار روزانه"),
        ("OT_RATE", "کارکرد", "ضریب اضافه‌کار"),
        ("NIGHT_RATE", "کارکرد", "ضریب شب‌کاری"),
        ("HOLIDAY_RATE", "کارکرد", "ضریب تعطیل‌کاری (اضافه بر مزد)"),
        ("INS_CAP_FACTOR", "بیمه", "سقف دستمزد بیمه (برابر حداقل مزد)"),
    ];
}

/// <summary>گروه استخدامی (pay.EmploymentGroups): قواعد مشترک چند الگوی محاسبه.</summary>
public class EmploymentGroup : AggregateRoot<int>
{
    private readonly List<GroupParam> _params = [];

    private EmploymentGroup() { } // EF

    public EmploymentGroup(string code, string title) => Update(code, title, null, null, null, true, null);

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    /// <summary>نرخ سهم کارگر (درصد) برای این گروه؛ null = نرخ کارگاه.</summary>
    public decimal? EmployeeRate { get; private set; }
    public decimal? EmployerRate { get; private set; }
    public decimal? UnemploymentRate { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public IReadOnlyList<GroupParam> Params => _params.AsReadOnly();

    public void Update(string code, string title, decimal? employeeRate, decimal? employerRate, decimal? unemploymentRate, bool isActive, string? notes)
    {
        Code = Templates.CheckCode(code, "کد گروه");
        Title = Templates.CheckTitle(title, "عنوان گروه");
        foreach (var r in new[] { employeeRate, employerRate, unemploymentRate })
            if (r is < 0 or > 100) throw new DomainException("نرخ بیمه باید بین ۰ و ۱۰۰ درصد باشد.");
        (EmployeeRate, EmployerRate, UnemploymentRate, IsActive) = (employeeRate, employerRate, unemploymentRate, isActive);
        Notes = PersianText.NormalizeOrNull(notes);
    }

    /// <summary>پارامترهای گروه (جای پارامتر هم‌نام سال را می‌گیرند).</summary>
    public void SetParams(IEnumerable<(string Code, decimal Value)> values)
    {
        var list = new List<GroupParam>();
        foreach (var (code, value) in values)
        {
            var c = Formula.Normalize(code).Replace(' ', '_');
            if (!PayVariables.IsValidCode(c)) throw new DomainException($"کد پارامتر «{code}» معتبر نیست (حرف بزرگ لاتین، رقم و _).");
            if (PayVariables.Builtin.ContainsKey(c)) throw new DomainException($"«{c}» از متغیرهای آماده است و در گروه عوض نمی‌شود.");
            if (value < 0) throw new DomainException($"مقدار «{c}» منفی است.");
            if (list.Any(p => p.Code == c)) throw new DomainException($"پارامتر «{c}» تکراری است.");
            list.Add(new GroupParam(Id, c, value));
        }
        _params.RemoveAll(p => list.All(n => n.Code != p.Code));
        foreach (var n in list)
        {
            var existing = _params.FirstOrDefault(p => p.Code == n.Code);
            if (existing is null) _params.Add(n);
            else existing.SetValue(n.Value);
        }
    }
}

public class GroupParam
{
    private GroupParam() { } // EF

    public GroupParam(int groupId, string code, decimal value) => (GroupId, Code, Value) = (groupId, code, value);

    public int GroupId { get; private set; }
    public string Code { get; private set; } = null!;
    public decimal Value { get; private set; }

    internal void SetValue(decimal value) => Value = value;
}

/// <summary>الگوی محاسبه (pay.CalcTemplates): گروه + اقلام فعال با فرمول اختیاری جایگزین.</summary>
public class CalcTemplate : AggregateRoot<int>
{
    private readonly List<TemplateItem> _items = [];

    private CalcTemplate() { } // EF

    public CalcTemplate(string code, string title, int groupId) => Update(code, title, groupId, true, null);

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public int GroupId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public IReadOnlyList<TemplateItem> Items => _items.AsReadOnly();

    public void Update(string code, string title, int groupId, bool isActive, string? notes)
    {
        Code = Templates.CheckCode(code, "کد الگو");
        Title = Templates.CheckTitle(title, "عنوان الگو");
        if (groupId <= 0) throw new DomainException("گروه استخدامی الگو را انتخاب کنید.");
        (GroupId, IsActive) = (groupId, isActive);
        Notes = PersianText.NormalizeOrNull(notes);
    }

    /// <summary>
    /// اقلام الگو؛ فرمول فقط برای قلمی که منبعش «فرمول» است و اگر با فرمول پیش‌فرض یکی باشد خالی ذخیره می‌شود.
    /// </summary>
    public void SetItems(IReadOnlyCollection<(PayItem Item, string? Formula)> items)
    {
        if (items.Count == 0) throw new DomainException("الگو دست‌کم یک قلم لازم دارد.");
        var list = new List<TemplateItem>();
        foreach (var (item, formula) in items)
        {
            if (list.Any(t => t.PayItemId == item.Id)) throw new DomainException($"قلم «{item.Title}» تکراری است.");
            string? text = null;
            if (!string.IsNullOrWhiteSpace(formula))
            {
                if (item.Source != PayItemSource.Formula)
                    throw new DomainException($"«{item.Title}» از فرمول محاسبه نمی‌شود؛ فرمول جایگزین ندارد.");
                try { text = Formula.Parse(formula).Text; }
                catch (DomainException ex) { throw new DomainException($"فرمول «{item.Title}»: {ex.Message}"); }
                if (text == item.Formula) text = null;
            }
            list.Add(new TemplateItem(Id, item.Id, text));
        }
        var formulas = list.Select(t => (t, items.First(i => i.Item.Id == t.PayItemId).Item))
            .Where(x => x.Item.Source == PayItemSource.Formula)
            .ToDictionary(x => x.Item.Code, x => Formula.Parse(x.t.Formula ?? x.Item.Formula));
        FormulaOrder.Sort(formulas); // دور در وابستگی فرمول‌ها خطا می‌دهد

        _items.RemoveAll(t => list.All(n => n.PayItemId != t.PayItemId));
        foreach (var n in list)
        {
            var existing = _items.FirstOrDefault(t => t.PayItemId == n.PayItemId);
            if (existing is null) _items.Add(n);
            else existing.SetFormula(n.Formula);
        }
    }
}

public class TemplateItem
{
    private TemplateItem() { } // EF

    public TemplateItem(int templateId, int payItemId, string? formula) => (TemplateId, PayItemId, Formula) = (templateId, payItemId, formula);

    public int TemplateId { get; private set; }
    public int PayItemId { get; private set; }
    /// <summary>فرمول جایگزین در این الگو؛ null = فرمول پیش‌فرض قلم.</summary>
    public string? Formula { get; private set; }

    internal void SetFormula(string? formula) => Formula = formula;
}

/// <summary>الگوی کارمند از یک تاریخ (pay.EmployeeTemplates).</summary>
public class EmployeeTemplate
{
    private EmployeeTemplate() { } // EF

    public EmployeeTemplate(int employeeId, int templateId, DateOnly fromDate, int createdBy, DateTime nowUtc) =>
        (EmployeeId, TemplateId, FromDate, CreatedBy, CreatedAt) = (employeeId, templateId, fromDate, createdBy, nowUtc);

    public int Id { get; private set; }
    public int EmployeeId { get; private set; }
    public int TemplateId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void ChangeTemplate(int templateId) => TemplateId = templateId;
}

internal static class Templates
{
    public static string CheckCode(string? code, string what)
    {
        var c = PersianText.NormalizeOrNull(code) ?? throw new DomainException($"{what} الزامی است.");
        if (c.Length > 20) throw new DomainException($"{what} حداکثر ۲۰ نویسه است.");
        return c;
    }

    public static string CheckTitle(string? title, string what)
    {
        var t = PersianText.NormalizeOrNull(title) ?? throw new DomainException($"{what} الزامی است.");
        if (t.Length > 100) throw new DomainException($"{what} حداکثر ۱۰۰ نویسه است.");
        return t;
    }
}
