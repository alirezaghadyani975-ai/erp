namespace Erp.Payroll.Application.Reports;

/// <summary>نوع ستون گزارش: متن، کد (چپ‌به‌راست)، عدد، مبلغ ریالی.</summary>
public enum ReportColumnKind : byte { Text, Code, Number, Amount }

/// <param name="Group">سرگروه ستون (مثلاً «مزایا»، «کسور») برای ردیف دوم سرستون.</param>
/// <param name="Sum">در ردیف جمع آورده شود.</param>
public sealed record ReportColumn(string Key, string Title, ReportColumnKind Kind = ReportColumnKind.Text, string? Group = null, bool Sum = true);

public sealed record ReportRow(IReadOnlyDictionary<string, object?> Cells)
{
    public object? this[string key] => Cells.GetValueOrDefault(key);
}

/// <summary>گزارش جدولی عمومی حقوق (نمایش، چاپ و اکسل با یک قالب).</summary>
public sealed record ReportTable(string Title, List<string> Subtitle, List<ReportColumn> Columns, List<ReportRow> Rows, bool Landscape = false)
{
    public decimal Total(string key) => Rows.Sum(r => r[key] is decimal d ? d : 0);
}

/// <summary>فیلتر مشترک گزارش‌های حقوق.</summary>
/// <param name="FinalOnly">فقط حقوق‌های قطعی؛ در غیر این صورت محاسبه‌شده‌ها هم (نیازمند محاسبه‌ها هرگز).</param>
public sealed record PayrollReportFilter(short Year, byte? Month = null, int? WorkshopId = null, int? EmployeeId = null, bool FinalOnly = false,
    byte? MonthTo = null);
