namespace Erp.SharedKernel.Reports;

/// <summary>نوع ستون گزارش: متن، کد (چپ‌به‌راست)، عدد، مبلغ ریالی، درصد.</summary>
public enum ReportColumnKind : byte { Text, Code, Number, Amount, Percent }

/// <param name="Group">سرگروه ستون (مثلاً «مزایا»، «کسور») برای ردیف دوم سرستون.</param>
/// <param name="Sum">در ردیف جمع آورده شود.</param>
public sealed record ReportColumn(string Key, string Title, ReportColumnKind Kind = ReportColumnKind.Text, string? Group = null, bool Sum = true);

/// <param name="Children">سطح دوم (تفکیک همین ردیف) با همان ستون‌ها؛ در نمایش باز و بسته می‌شود.</param>
public sealed record ReportRow(IReadOnlyDictionary<string, object?> Cells, List<ReportRow>? Children = null)
{
    public object? this[string key] => Cells.GetValueOrDefault(key);
}

/// <summary>گزارش جدولی عمومی (نمایش، چاپ و اکسل با یک قالب `ReportTableView`).</summary>
/// <param name="ChildTitle">عنوان سطح دوم (مثلاً «به تفکیک محصول») برای راهنمای نمایش.</param>
public sealed record ReportTable(string Title, List<string> Subtitle, List<ReportColumn> Columns, List<ReportRow> Rows, bool Landscape = false,
    string? ChildTitle = null)
{
    public decimal Total(string key) => Rows.Sum(r => r[key] is decimal d ? d : 0);

    public bool HasChildren => Rows.Any(r => r.Children is { Count: > 0 });
}
