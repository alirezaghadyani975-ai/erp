using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Reports;

namespace Erp.Api.Components.Shared;

/// <summary>فیلتر قابل ویرایش گزارش‌ها در رابط کاربری و تبدیلش به درخواست و پارامترهای آدرس.</summary>
public sealed class ReportFilterModel
{
    public short? YearId { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public bool IncludeTemporary { get; set; } = true;
    public bool ExcludeClosing { get; set; }
    public int? FromVoucherNo { get; set; }
    public int? ToVoucherNo { get; set; }

    /// <summary>با عوض شدن سال، بازه تاریخ به ابتدا و انتهای همان سال برمی‌گردد.</summary>
    public void SetYear(FiscalYearDto? year)
    {
        YearId = year?.Id;
        FromDate = year?.StartDate;
        ToDate = year?.EndDate;
    }

    public ReportFilter ToFilter() => new(
        YearId ?? throw new InvalidOperationException("سال مالی انتخاب نشده است."),
        Blank(FromDate), Blank(ToDate), IncludeTemporary, ExcludeClosing, FromVoucherNo, ToVoucherNo);

    /// <summary>پارامترهای آدرس برای باز کردن گزارش دیگر با همین فیلتر (مثلاً از تراز به دفتر).</summary>
    public Dictionary<string, object?> ToQuery() => new()
    {
        ["year"] = (int?)YearId, // short در پارامتر آدرس پشتیبانی نمی‌شود
        ["from"] = Blank(FromDate),
        ["to"] = Blank(ToDate),
        ["temp"] = IncludeTemporary ? null : false,
        ["noclosing"] = ExcludeClosing ? true : null,
        ["vfrom"] = FromVoucherNo,
        ["vto"] = ToVoucherNo,
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
