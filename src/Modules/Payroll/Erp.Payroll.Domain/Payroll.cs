using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Payroll.Domain;

/// <summary>نوع قلم حقوق (pay.PayItems.Kind).</summary>
public enum PayItemKind : byte
{
    Earning = 1,
    Deduction = 2,
    EmployerShare = 3,
    Provision = 4,
    /// <summary>فقط برای فرمول‌ها (مثل مزد روزانه حکم)؛ مبلغی پرداخت نمی‌شود.</summary>
    Info = 5
}

/// <summary>منبع مقدار قلم (pay.PayItems.Source).</summary>
public enum PayItemSource : byte
{
    Decree = 1,
    Formula = 2,
    MonthlyInput = 3,
    Loan = 4,
    /// <summary>مالیات: محاسبه سامانه مالیاتی و ورود فایل نتیجه.</summary>
    TaxOffice = 5
}

/// <summary>روزهای ماه در فرمول‌ها (pay.Settings.MonthDaysMode).</summary>
public enum MonthDaysMode : byte
{
    /// <summary>همیشه ۳۰ روز.</summary>
    Thirty = 1,
    /// <summary>تعداد روز واقعی ماه (۲۹ تا ۳۱).</summary>
    Actual = 2
}

/// <summary>نوع حکم (pay.Decrees.DecreeType).</summary>
public enum DecreeType : byte
{
    Hire = 1,
    AnnualRaise = 2,
    Change = 3,
    Other = 4
}

/// <summary>متغیرهای آماده فرمول (از کارکرد ماه، پرونده و کارگاه)؛ کد اقلام و پارامترهای سال هم متغیرند.</summary>
public static class PayVariables
{
    public static readonly IReadOnlyDictionary<string, string> Builtin = new Dictionary<string, string>
    {
        ["DAYS"] = "روزهای کارکرد ماه",
        ["INS_DAYS"] = "روزهای بیمه ماه",
        ["MONTH_DAYS"] = "روزهای ماه (طبق تنظیمات)",
        ["OT_HOURS"] = "ساعت اضافه‌کار",
        ["NIGHT_HOURS"] = "ساعت شب‌کاری",
        ["HOLIDAY_HOURS"] = "ساعت تعطیل‌کاری",
        ["ABSENT_DAYS"] = "روزهای غیبت",
        ["CHILDREN"] = "تعداد فرزندان",
        ["MARRIED"] = "متأهل (۱) یا مجرد (۰)",
        ["INS_SUBJECT"] = "جمع مزایای مشمول بیمه",
        ["TAX_SUBJECT"] = "جمع مزایای مشمول مالیات",
        ["INS_EMPLOYEE_RATE"] = "نرخ سهم کارگر کارگاه (درصد)",
        ["INS_EMPLOYER_RATE"] = "نرخ سهم کارفرما کارگاه (درصد)",
        ["INS_WAGE"] = "دستمزد ماه برای بیمه: (مزد روزانه + پایه سنوات) × روز بیمه",
        ["INS_BENEFITS"] = "مزایای مشمول بیمه (غیر از دستمزد)",
        ["MONTH"] = "ماه اجرای حقوق (۱ تا ۱۲)",
        ["SETTLEMENT"] = "تسویه حساب در این ماه (۱) یا نه (۰)",
        ["YEAR_DAYS"] = "روزهای سال (۳۶۰ در حالت ۳۰ روزه، وگرنه روز واقعی سال)",
        ["YEAR_WORK_DAYS"] = "جمع روز کارکرد امسال تا این ماه",
        ["SERVICE_DAYS"] = "روزهای خدمت از استخدام (یا آخرین تسویه) تا ترک کار / پایان ماه",
        ["LEAVE_BALANCE"] = "مانده مرخصی استحقاقی (روز)",
        ["INS_UNEMPLOYMENT_RATE"] = "نرخ بیمه بیکاری کارگاه (درصد)"
    };

    /// <summary>متغیرهایی که به جمع اقلام وابسته‌اند؛ فرمول قلمِ مشمول بیمه/مالیات نباید از آن‌ها استفاده کند.</summary>
    public static readonly IReadOnlySet<string> Aggregates = new HashSet<string> { "INS_SUBJECT", "TAX_SUBJECT", "INS_BENEFITS" };

    public static bool IsValidCode(string code) =>
        code.Length is > 0 and <= 30 && char.IsAsciiLetterUpper(code[0]) && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}

/// <summary>قلم حقوق (pay.PayItems). اقلام سیستمی کد، نوع و منبعشان ثابت است و حذف نمی‌شوند.</summary>
public class PayItem : AggregateRoot<int>
{
    private PayItem() { } // EF

    public PayItem(string code, PayItemKind kind, PayItemSource source)
    {
        Code = CheckCode(code);
        Kind = kind;
        Source = source;
        CheckKind();
    }

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public PayItemKind Kind { get; private set; }
    public PayItemSource Source { get; private set; }
    public string? Formula { get; private set; }
    public bool InsuranceSubject { get; private set; }
    public bool TaxSubject { get; private set; }
    public bool InsuranceWage { get; private set; }
    public byte? TaxField { get; private set; }
    public int? CostFactorId { get; private set; }
    public int? AccountId { get; private set; }
    public bool OnPayslip { get; private set; } = true;
    public int SortOrder { get; private set; } = 100;
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>تغییر کد، نوع یا منبع؛ برای اقلام سیستمی مجاز نیست.</summary>
    public void Redefine(string code, PayItemKind kind, PayItemSource source)
    {
        var c = CheckCode(code);
        if (IsSystem && (c != Code || kind != Kind || source != Source))
            throw new DomainException($"قلم «{Title}» قانونی (سیستمی) است؛ کد، نوع و منبعش عوض نمی‌شود.");
        (Code, Kind, Source) = (c, kind, source);
        CheckKind();
        if (Source != PayItemSource.Formula) Formula = null;
    }

    /// <param name="insuranceWage">جزء «دستمزد» لیست بیمه (مزد و پایه سنوات)؛ در بیمه با مزد روزانه × روز بیمه جایگزین می‌شود.</param>
    /// <param name="taxField">شماره ستون قلم در فایل مالیات حقوق (WH ریالی).</param>
    public void Update(string title, string? formula, bool insuranceSubject, bool taxSubject, int? costFactorId, int? accountId,
        bool onPayslip, int sortOrder, bool insuranceWage = false, byte? taxField = null)
    {
        var t = PersianText.NormalizeOrNull(title) ?? throw new DomainException("عنوان قلم الزامی است.");
        if (t.Length > 100) throw new DomainException("عنوان قلم حداکثر ۱۰۰ نویسه است.");
        Title = t;

        if (Source == PayItemSource.Formula)
            Formula = Domain.Formula.Parse(formula).Text;
        else if (!string.IsNullOrWhiteSpace(formula))
            throw new DomainException("فرمول فقط برای قلمی است که منبعش «فرمول» است.");
        else
            Formula = null;

        var paid = Kind is PayItemKind.Earning;
        if (!paid && (insuranceSubject || taxSubject))
            throw new DomainException("فقط اقلام مزایا مشمول بیمه یا مالیات می‌شوند.");
        if (Kind == PayItemKind.Info && (costFactorId is not null || accountId is not null))
            throw new DomainException("قلم اطلاعاتی عامل هزینه یا حساب ندارد.");
        if (Kind == PayItemKind.Deduction && costFactorId is not null)
            throw new DomainException("کسور عامل هزینه ندارد؛ فقط حساب بستانکار.");

        if (insuranceWage && !(Kind == PayItemKind.Earning && insuranceSubject))
            throw new DomainException("فقط مزایای مشمول بیمه جزء «دستمزد» لیست بیمه می‌شوند.");
        if (taxField is { } tf)
        {
            if (!TaxFileFields.Titles.ContainsKey(tf)) throw new DomainException("ستون فایل مالیات معتبر نیست.");
            var deductionField = tf is 33 or 34;
            if (Kind == PayItemKind.Earning && deductionField) throw new DomainException("ستون‌های ۳۳ و ۳۴ فایل مالیات برای کسور بیمه است، نه مزایا.");
            if (Kind == PayItemKind.Deduction && !deductionField) throw new DomainException("کسور فقط در ستون‌های ۳۳ و ۳۴ فایل مالیات (حق بیمه ماده ۱۳۷) می‌آید.");
            if (Kind is not (PayItemKind.Earning or PayItemKind.Deduction)) throw new DomainException("فقط مزایا و کسور در فایل مالیات می‌آیند.");
        }
        InsuranceWage = insuranceWage;
        TaxField = taxField;
        InsuranceSubject = insuranceSubject;
        TaxSubject = taxSubject;
        CostFactorId = costFactorId is > 0 ? costFactorId : null;
        AccountId = accountId is > 0 ? accountId : null;
        OnPayslip = Kind != PayItemKind.Info && onPayslip;
        SortOrder = sortOrder;
    }

    public void SetActive(bool active)
    {
        if (!active && IsSystem) throw new DomainException($"قلم «{Title}» قانونی (سیستمی) است و غیرفعال نمی‌شود.");
        IsActive = active;
    }

    /// <summary>فرمول تجزیه‌شده (یا null).</summary>
    public Formula? ParsedFormula() => Formula is null ? null : Domain.Formula.Parse(Formula);

    private void CheckKind()
    {
        if (!Enum.IsDefined(Kind)) throw new DomainException("نوع قلم معتبر نیست.");
        if (!Enum.IsDefined(Source)) throw new DomainException("منبع مقدار قلم معتبر نیست.");
        if (Source == PayItemSource.Loan && Kind != PayItemKind.Deduction) throw new DomainException("قلم وام و مساعده باید از نوع کسور باشد.");
        if (Source == PayItemSource.TaxOffice && Kind != PayItemKind.Deduction) throw new DomainException("قلم مالیات باید از نوع کسور باشد.");
    }

    private static string CheckCode(string? code)
    {
        var c = Domain.Formula.Normalize(code).Replace(' ', '_');
        if (!PayVariables.IsValidCode(c))
            throw new DomainException("کد قلم باید با حرف لاتین شروع شود و فقط حروف بزرگ لاتین، رقم و _ داشته باشد (حداکثر ۳۰).");
        if (PayVariables.Builtin.ContainsKey(c)) throw new DomainException($"کد «{c}» از متغیرهای آماده فرمول است؛ کد دیگری بگذارید.");
        return c;
    }
}

/// <summary>پارامتر قانونی سال (pay.YearParams)؛ کد آن در فرمول متغیر است.</summary>
public class YearParam
{
    private YearParam() { } // EF

    public YearParam(short year, string code)
    {
        if (year is < 1300 or > 1500) throw new DomainException("سال شمسی معتبر نیست.");
        var c = Domain.Formula.Normalize(code).Replace(' ', '_');
        if (!PayVariables.IsValidCode(c))
            throw new DomainException("کد پارامتر باید با حرف لاتین شروع شود و فقط حروف بزرگ لاتین، رقم و _ داشته باشد.");
        if (PayVariables.Builtin.ContainsKey(c)) throw new DomainException($"کد «{c}» از متغیرهای آماده فرمول است.");
        (Year, Code) = (year, c);
    }

    public short Year { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public decimal Value { get; private set; }

    public void Update(string title, decimal value)
    {
        var t = PersianText.NormalizeOrNull(title) ?? throw new DomainException("عنوان پارامتر الزامی است.");
        if (t.Length > 100) throw new DomainException("عنوان پارامتر حداکثر ۱۰۰ نویسه است.");
        if (value < 0) throw new DomainException($"مقدار «{t}» منفی نمی‌شود.");
        Title = t;
        Value = Math.Round(value, 6);
    }
}

/// <summary>تنظیمات حقوق شرکت (pay.Settings، یک ردیف).</summary>
public class PaySettings
{
    private PaySettings() { } // EF

    public byte Id { get; private set; } = 1;
    public MonthDaysMode MonthDaysMode { get; private set; } = MonthDaysMode.Thirty;

    public void Update(MonthDaysMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new DomainException("روش روزهای ماه معتبر نیست.");
        MonthDaysMode = mode;
    }
}

/// <summary>حکم حقوقی (pay.Decrees): مقدار اقلام حکمی کارمند از تاریخ اجرا تا حکم بعدی.</summary>
public class Decree : AggregateRoot<int>
{
    private readonly List<DecreeItem> _items = [];

    private Decree() { } // EF

    public Decree(int employeeId, int docNo, int userId, DateTime nowUtc)
    {
        if (employeeId <= 0) throw new DomainException("کارمند الزامی است.");
        EmployeeId = employeeId;
        DocNo = docNo;
        CreatedBy = userId;
        CreatedAt = nowUtc;
    }

    public int EmployeeId { get; private set; }
    public int DocNo { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public DecreeType DecreeType { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<DecreeItem> Items => _items.AsReadOnly();

    /// <param name="items">قلم حکمی و مبلغ؛ مبلغ صفر ذخیره نمی‌شود.</param>
    public void Update(DateOnly effectiveDate, DecreeType type, string? description, IEnumerable<(PayItem Item, decimal Amount)> items)
    {
        if (!Enum.IsDefined(type)) throw new DomainException("نوع حکم معتبر نیست.");
        var desc = PersianText.NormalizeOrNull(description);
        if (desc?.Length > 500) throw new DomainException("شرح حکم حداکثر ۵۰۰ نویسه است.");

        var rows = new List<DecreeItem>();
        foreach (var (item, amount) in items)
        {
            if (item.Source != PayItemSource.Decree) throw new DomainException($"قلم «{item.Title}» حکمی نیست.");
            if (amount < 0) throw new DomainException($"مبلغ «{item.Title}» منفی نمی‌شود.");
            if (rows.Any(r => r.PayItemId == item.Id)) throw new DomainException($"قلم «{item.Title}» دو بار در حکم آمده است.");
            if (amount != 0) rows.Add(new DecreeItem(item.Id, Math.Round(amount, 4)));
        }
        if (rows.Count == 0) throw new DomainException("حکم حداقل یک قلم با مبلغ دارد.");

        EffectiveDate = effectiveDate;
        DecreeType = type;
        Description = desc;
        _items.Clear();
        _items.AddRange(rows);
    }
}

public class DecreeItem
{
    private DecreeItem() { } // EF

    internal DecreeItem(int payItemId, decimal amount) => (PayItemId, Amount) = (payItemId, amount);

    public int DecreeId { get; private set; }
    public int PayItemId { get; private set; }
    public decimal Amount { get; private set; }
}
