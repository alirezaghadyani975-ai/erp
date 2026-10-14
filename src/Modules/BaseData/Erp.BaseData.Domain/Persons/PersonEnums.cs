namespace Erp.BaseData.Domain.Persons;

// فقط مقادیری که قاعده یا معنای قانونی ثابت دارند enum هستند؛ فهرست‌های قابل تغییر در جداول پایه‌اند.

public enum Gender : byte { Male = 1, Female = 2 }

public enum MaritalStatus : byte { Single = 1, Married = 2 }

/// <summary>نوع شخص حقوقی (قانون تجارت).</summary>
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
