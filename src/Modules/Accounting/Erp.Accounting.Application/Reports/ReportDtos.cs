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
public sealed record LedgerRequest(ReportFilter Filter, string? AccountCode = null, int? DetailId = null);

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
