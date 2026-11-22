namespace Erp.Payroll.Application.Reports;

/// <summary>فیلتر مشترک گزارش‌های حقوق.</summary>
/// <param name="FinalOnly">فقط حقوق‌های قطعی؛ در غیر این صورت محاسبه‌شده‌ها هم (نیازمند محاسبه‌ها هرگز).</param>
public sealed record PayrollReportFilter(short Year, byte? Month = null, int? WorkshopId = null, int? EmployeeId = null, bool FinalOnly = false,
    byte? MonthTo = null);
