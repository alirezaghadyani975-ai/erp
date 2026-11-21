namespace Erp.Payroll.Domain;

/// <summary>
/// ستون‌های مبلغ فایل فهرست حقوق ریالی (WH) طبق «راهنمای تولید فایل الکترونیکی مالیات بر درآمد حقوق» نسخه 1.7.0.5.
/// </summary>
public static class TaxFileFields
{
    public static readonly IReadOnlyDictionary<byte, string> Titles = new Dictionary<byte, string>
    {
        [7] = "ناخالص حقوق و مزایای مستمر نقدی ماه جاری",
        [15] = "حق‌الزحمه، مشاوره، حضور، نظارت، تألیف، فنی",
        [16] = "قراردادهای پژوهشی",
        [17] = "اضافه‌کاری",
        [18] = "هزینه سفر",
        [19] = "فوق‌العاده مسافرت (مأموریت)",
        [20] = "کارانه",
        [21] = "پاداش (غیر از آخر سال، پایان خدمت و بهره‌وری)",
        [22] = "پاداش آخر سال",
        [23] = "عیدی سالانه",
        [24] = "پاداش پایان خدمت",
        [25] = "خسارت اخراج",
        [26] = "بازخرید خدمت",
        [27] = "حق سنوات",
        [28] = "حقوق مرخصی استفاده‌نشده",
        [29] = "سایر حقوق و مزایای غیرمستمر نقدی",
        [33] = "حق بیمه درمان ماده ۱۳۷ (کسر)",
        [34] = "حق بیمه عمر و زندگی ماده ۱۳۷ (کسر)",
        [36] = "حق‌التدریس، حق‌التحقیق، حق پژوهش",
        [37] = "حق کشیک",
        [38] = "رفاهی، انگیزشی و بهره‌وری",
        [39] = "حق‌السعی"
    };

    /// <summary>جدول ۲: مدرک تحصیلی.</summary>
    public static readonly IReadOnlyDictionary<byte, string> Education = new Dictionary<byte, string>
    {
        [1] = "زیر دیپلم", [2] = "دیپلم", [3] = "فوق دیپلم", [4] = "کارشناسی", [5] = "کارشناسی ارشد", [6] = "دکتری", [7] = "فوق دکتری"
    };

    /// <summary>جدول ۴: نوع معافیت.</summary>
    public static readonly IReadOnlyDictionary<byte, string> Exemptions = new Dictionary<byte, string>
    {
        [1] = "عدم معافیت", [2] = "جانبازان", [3] = "فرزندان شاهد", [4] = "آزادگان", [5] = "نیروهای مسلح",
        [6] = "کارشناس خارجی (بند ۳ ماده ۹۱)", [7] = "اجتناب از مالیات مضاعف (ماده ۹۱)", [8] = "مشمولین قانون استخدامی وزارت اطلاعات",
        [9] = "رزمندگان با حداقل ۱۲ ماه حضور در جبهه", [10] = "ماده ۲۵ قانون حمایت از حقوق معلولان",
        [11] = "ماده ۱۸ قانون حمایت از خانواده و جوانی جمعیت"
    };

    /// <summary>جدول ۶: رسته.</summary>
    public static readonly IReadOnlyDictionary<byte, string> JobCategories = new Dictionary<byte, string>
    {
        [1] = "آموزش و فرهنگی", [2] = "اداری و مالی", [3] = "هیئت علمی", [4] = "امور اجتماعی", [5] = "انبارداری",
        [6] = "بازاریابی و فروش", [7] = "بهداشت و درمان", [8] = "تحقیقات", [9] = "ترابری", [10] = "تولیدی",
        [11] = "حراست و نگهبانی", [12] = "خدمات", [13] = "سایر", [14] = "فناوری اطلاعات", [15] = "فنی و مهندسی",
        [16] = "قضات", [17] = "کارگری", [18] = "کشاورزی و محیط زیست", [19] = "کنترل کیفی"
    };

    /// <summary>جدول ۷: نوع استخدام.</summary>
    public static readonly IReadOnlyDictionary<byte, string> EmploymentTypes = new Dictionary<byte, string>
    {
        [1] = "قراردادی", [2] = "شرکتی", [3] = "رسمی", [4] = "پیمانی", [5] = "مأمور", [6] = "کارمزدی", [7] = "خرید خدمت",
        [8] = "ساعتی", [9] = "روزمزد", [10] = "رسمی-آزمایشی", [11] = "رسمی فصلی", [12] = "سایر"
    };

    /// <summary>جدول ۹: وضعیت محل خدمت.</summary>
    public static readonly IReadOnlyDictionary<byte, string> ServiceLocations = new Dictionary<byte, string>
    {
        [1] = "عادی", [2] = "مناطق کمتر توسعه‌یافته", [3] = "پارک علم و فناوری", [4] = "ماده ۱۲ قانون حداکثر استفاده از توان تولیدی"
    };
}

/// <summary>مبنای بیمه یک کارمند در یک ماه (همان ستون‌های لیست تأمین اجتماعی).</summary>
/// <param name="DailyWage">مزد روزانه (DSW_ROOZ)</param>
/// <param name="DailySeniority">پایه سنوات روزانه (DSW_INC)</param>
/// <param name="Wage">دستمزد ماه = (مزد + سنوات روزانه) × روز بیمه (DSW_MAH)</param>
/// <param name="Benefits">مزایای ماهانه مشمول (DSW_MAZ)</param>
/// <param name="Subject">جمع مشمول با سقف (DSW_MASH)</param>
/// <param name="Total">جمع کل مشمول و غیرمشمول (DSW_TOTL)</param>
public sealed record InsuranceBase(decimal DailyWage, decimal DailySeniority, decimal Wage, decimal Benefits, decimal Subject, decimal Total)
{
    public const string DailyWageCode = "DAILY_WAGE";
    public const string SeniorityDailyCode = "SENIORITY_DAILY";

    /// <summary>
    /// از مبالغ اقلام: اگر قلمی «دستمزد بیمه» باشد، دستمزد = (مزد + سنوات روزانه) × روز بیمه و جای آن اقلام را می‌گیرد؛
    /// بقیه مزایای مشمول «مزایا» هستند. سقف = حداقل مزد × ضریب سقف × روز بیمه.
    /// </summary>
    public static InsuranceBase Compute(IReadOnlyCollection<PayItem> items, Func<PayItem, decimal> amount, int insuranceDays, decimal dailyCap)
    {
        decimal Code(string c) => items.FirstOrDefault(i => i.Code == c) is { } i ? amount(i) : 0;
        var earnings = items.Where(i => i.Kind == PayItemKind.Earning).ToList();
        var hasWage = earnings.Any(i => i.InsuranceWage);
        var (daily, seniority) = (Code(DailyWageCode), Code(SeniorityDailyCode));
        var wage = hasWage ? Math.Round((daily + seniority) * insuranceDays, 0, MidpointRounding.AwayFromZero)
                           : 0;
        var benefits = earnings.Where(i => i.InsuranceSubject && !i.InsuranceWage).Sum(amount);
        if (!hasWage) benefits = earnings.Where(i => i.InsuranceSubject).Sum(amount);
        var nonSubject = earnings.Where(i => !i.InsuranceSubject).Sum(amount);
        var raw = wage + benefits;
        var subject = dailyCap > 0 ? Math.Min(raw, Math.Round(dailyCap * insuranceDays, 0, MidpointRounding.AwayFromZero)) : raw;
        if (insuranceDays == 0) subject = 0;
        return new InsuranceBase(daily, seniority, wage, benefits, subject, raw + nonSubject);
    }
}
