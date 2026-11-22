namespace Erp.SharedKernel.Reports;

/// <summary>یک ستون محاسبه‌شده از مجموعه رکوردهای یک گروه (جمع، تعداد، درصد، ...).</summary>
public sealed record Measure<T>(string Key, Func<IReadOnlyList<T>, object?> Value);

/// <summary>ساخت ردیف‌های دوسطحی از رکوردهای ریز: سطح اول گروه‌بندی با یک کلید، سطح دوم با کلید دیگر؛ هر دو سطح همان ستون‌ها.</summary>
public static class ReportBuilder
{
    /// <param name="title1">عنوان گروه سطح اول (در ستون «title»)؛ مرتب‌سازی هم با همین عنوان است مگر <paramref name="order1"/> بدهید.</param>
    public static List<ReportRow> TwoLevel<T, K1, K2>(IEnumerable<T> facts,
        Func<T, K1> key1, Func<K1, string> title1,
        Func<T, K2> key2, Func<K2, string> title2,
        IReadOnlyList<Measure<T>> measures, Func<K1, object>? order1 = null) where K1 : notnull where K2 : notnull
    {
        ReportRow Make(string title, IReadOnlyList<T> rows, List<ReportRow>? children)
        {
            var cells = new Dictionary<string, object?> { ["title"] = title };
            foreach (var m in measures) cells[m.Key] = m.Value(rows);
            return new ReportRow(cells, children);
        }

        var groups = facts.GroupBy(key1);
        var ordered = order1 is null ? groups.OrderBy(g => title1(g.Key), StringComparer.Ordinal) : groups.OrderBy(g => order1(g.Key));
        return ordered.Select(g =>
        {
            var children = g.GroupBy(key2).OrderBy(c => title2(c.Key), StringComparer.Ordinal)
                .Select(c => Make(title2(c.Key), c.ToList(), null)).ToList();
            return Make(title1(g.Key), g.ToList(), children);
        }).ToList();
    }

    /// <summary>درصد a از b با یک رقم اعشار؛ b صفر = خالی.</summary>
    public static decimal? Percent(decimal a, decimal b) => b == 0 ? null : Math.Round(a / b * 100, 1);
}
