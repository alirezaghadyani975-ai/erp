using Erp.Accounting.Domain.Vouchers;

namespace Erp.Accounting.Application.Reports;

/// <summary>سطح تراز آزمایشی.</summary>
public enum TrialBalanceLevel : byte
{
    Group = 1,      // گروه
    General = 2,    // کل
    Subsidiary = 3, // معین
    Detail = 4      // تفصیلی
}

/// <summary>
/// فیلتر مشترک گزارش‌ها. تاریخ‌ها شمسی (1405/01/01)؛ خالی یعنی ابتدا/انتهای سال مالی.
/// بازه شماره سند روی شماره قطعی است، پس با آن فقط اسناد دائم در گزارش می‌آیند.
/// </summary>
public sealed record ReportFilter(
    short FiscalYearId,
    string? FromDate = null,
    string? ToDate = null,
    bool IncludeTemporary = true,
    bool ExcludeClosing = false,
    int? FromVoucherNo = null,
    int? ToVoucherNo = null);

/// <param name="AccountCode">فقط زیرمجموعه این حساب (گروه، کل یا معین).</param>
/// <param name="DetailTypeId">در سطح تفصیلی: فقط این نوع تفصیلی.</param>
/// <param name="DetailId">فقط ردیف‌هایی که این تفصیلی را (در هر سطحی) دارند.</param>
public sealed record TrialBalanceRequest(
    ReportFilter Filter,
    TrialBalanceLevel Level = TrialBalanceLevel.Subsidiary,
    string? AccountCode = null,
    short? DetailTypeId = null,
    int? DetailId = null,
    bool HideZero = true);

/// <summary>
/// یک ردیف تراز. همه ستون‌ها محاسبه می‌شوند و رابط کاربری ۴ یا ۶ ستون را نمایش می‌دهد.
/// تراز ۴ ستونی: گردش (Total) و مانده (Ending).
/// تراز ۶ ستونی: مانده ابتدای دوره (Opening)، گردش طی دوره (Period) و مانده پایان دوره (Ending).
/// سند افتتاحیه همیشه جزو مانده ابتدای دوره حساب می‌شود.
/// </summary>
public sealed record TrialBalanceRow(
    TrialBalanceLevel Level,
    string Code,
    string Title,
    int? AccountId,
    int? DetailId,
    string? DetailTypeTitle,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal EndingDebit,
    decimal EndingCredit)
{
    public bool HasChildren => Level is TrialBalanceLevel.Group or TrialBalanceLevel.General;
}

public sealed record TrialBalanceDto(
    TrialBalanceLevel Level,
    string FromDate,
    string ToDate,
    List<TrialBalanceRow> Rows,
    TrialBalanceRow Totals)
{
    public bool IsBalanced => Totals.TotalDebit == Totals.TotalCredit && Totals.EndingDebit == Totals.EndingCredit;
}

/// <param name="AccountCode">حساب گروه، کل یا معین (دفتر کل/معین). با تفصیلی: دفتر تفصیلی همان حساب.</param>
/// <param name="DetailId">دفتر تفصیلی؛ بدون حساب یعنی گردش تفصیلی در همه حساب‌ها.</param>
/// <param name="ExtraDetailIds">تفصیلی‌های دیگری که ردیف باید آن‌ها را هم داشته باشد (از مرور حساب‌ها).</param>
public sealed record LedgerRequest(ReportFilter Filter, string? AccountCode = null, int? DetailId = null,
    List<int>? ExtraDetailIds = null);

public sealed record LedgerRow(
    long VoucherId,
    string Date,
    int TempNo,
    int? VoucherNo,
    VoucherStatus Status,
    short RowNo,
    string AccountCode,
    string AccountTitle,
    string? Details,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record LedgerDto(
    string Title,
    string FromDate,
    string ToDate,
    decimal OpeningBalance,
    List<LedgerRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal EndingBalance);

/// <summary>
/// سطحی که مرور حساب‌ها بر اساس آن گروه‌بندی می‌کند. در هر قدم می‌شود سطح را عوض کرد
/// (مثلاً از یک تفصیلی به معین‌هایی که در آن‌ها گردش دارد، یا از معین به تفصیلی سطح ۲).
/// </summary>
public enum ReviewDimension : byte
{
    Group = 1,
    General = 2,
    Subsidiary = 3,
    Detail1 = 11,
    Detail2 = 12,
    Detail3 = 13,
    Detail4 = 14,
    Detail5 = 15,
    Detail6 = 16,
    /// <summary>هر تفصیلی در هر سطحی (مانده کامل یک تفصیلی در همه حساب‌ها).</summary>
    AnyDetail = 20,
    /// <summary>اسناد (با مانده جاری).</summary>
    Voucher = 30
}

/// <param name="AccountCode">فقط زیرمجموعه این حساب (گروه، کل یا معین).</param>
/// <param name="DetailIds">فقط ردیف‌هایی که همه این تفصیلی‌ها را (در هر سطحی) دارند.</param>
/// <param name="DetailTypeId">در سطح «همه تفصیلی‌ها»: فقط این نوع.</param>
public sealed record ReviewRequest(
    ReportFilter Filter,
    ReviewDimension By,
    string? AccountCode = null,
    List<int>? DetailIds = null,
    short? DetailTypeId = null);

/// <summary>
/// یک ردیف مرور حساب‌ها. برای سطح «اسناد»: مانده ابتدا صفر است، گردش = گردش سند و مانده پایان = مانده جاری.
/// ردیف تفصیلی بدون DetailId یعنی «بدون تفصیلی در این سطح».
/// </summary>
public sealed record ReviewRow(
    string Code,
    string Title,
    string? Extra,
    int? AccountId,
    int? DetailId,
    long? VoucherId,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal EndingDebit,
    decimal EndingCredit);

/// <param name="Path">عنوان محدودیت‌های فعلی (حساب و تفصیلی‌ها) برای سربرگ و چاپ.</param>
public sealed record ReviewDto(
    ReviewDimension By,
    string FromDate,
    string ToDate,
    List<string> Path,
    decimal OpeningBalance,
    List<ReviewRow> Rows,
    ReviewRow Totals);
