namespace Erp.Accounting.Domain.BaseData;

public enum PersonKind : byte
{
    Natural = 1, // حقیقی
    Legal = 2    // حقوقی
}

public enum Gender : byte { Male = 1, Female = 2 }

public enum MaritalStatus : byte { Single = 1, Married = 2 }

/// <summary>نوع شخص حقوقی.</summary>
public enum LegalType : byte
{
    PrivateJointStock = 1,  // سهامی خاص
    PublicJointStock = 2,   // سهامی عام
    LimitedLiability = 3,   // با مسئولیت محدود
    Partnership = 4,        // تضامنی
    Cooperative = 5,        // تعاونی
    Government = 6,         // دولتی
    NonCommercial = 7,      // مؤسسه غیرتجاری
    Other = 20              // سایر
}

/// <summary>نوع شخص در سامانه مودیان (صورتحساب الکترونیکی).</summary>
public enum TaxPayerType : byte
{
    Natural = 1,          // حقیقی
    Legal = 2,            // حقوقی
    CivilPartnership = 3, // مشارکت مدنی
    Foreign = 4,          // اتباع غیرایرانی
    FinalConsumer = 5     // مصرف‌کننده نهایی
}

public enum ContractType : byte
{
    Permanent = 1,  // دائم
    Fixed = 2,      // قراردادی
    Contract = 3,   // پیمانی
    Hourly = 4,     // ساعتی
    PartTime = 5,   // پاره‌وقت
    Intern = 6      // کارآموز
}

public enum InsuranceType : byte
{
    SocialSecurity = 1, // تأمین اجتماعی
    HealthService = 2,  // خدمات درمانی
    ArmedForces = 3,    // نیروهای مسلح
    Other = 4,          // سایر
    None = 9            // بدون بیمه
}

public enum EducationLevel : byte
{
    BelowDiploma = 1, // زیر دیپلم
    Diploma = 2,      // دیپلم
    Associate = 3,    // کاردانی
    Bachelor = 4,     // کارشناسی
    Master = 5,       // کارشناسی ارشد
    Doctorate = 6     // دکترا
}

public enum MilitaryStatus : byte
{
    Completed = 1,  // پایان خدمت
    Exempt = 2,     // معاف
    Eligible = 3,   // مشمول
    Serving = 4     // در حال خدمت
}

/// <summary>معافیت مالیات حقوق.</summary>
public enum TaxExemptionType : byte
{
    Veteran = 1,       // ایثارگر / جانباز
    FreedPow = 2,      // آزاده
    MartyrChild = 3,   // فرزند شهید
    Deprived = 4,      // مناطق محروم
    Other = 9          // سایر
}

/// <summary>کد نقش‌های سیستمی (base.PersonRoleTypes.Code).</summary>
public static class RoleCodes
{
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Contractor = "contractor";
    public const string Shareholder = "shareholder";
    public const string Affiliate = "affiliate";
    public const string Employee = "employee";
    public const string Other = "other";
}
