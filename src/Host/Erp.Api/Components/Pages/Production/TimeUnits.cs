namespace Erp.Api.Components.Pages.Production;

/// <summary>واحد ورود زمان استاندارد؛ ذخیره همیشه به ثانیه.</summary>
public enum TimeUnit : byte { Second = 1, Minute = 2, Hour = 3 }

public static class TimeUnits
{
    public static decimal Factor(TimeUnit u) => u switch { TimeUnit.Minute => 60, TimeUnit.Hour => 3600, _ => 1 };

    public static string Title(TimeUnit u) => u switch { TimeUnit.Minute => "دقیقه", TimeUnit.Hour => "ساعت", _ => "ثانیه" };

    /// <summary>مناسب‌ترین واحد برای نمایش مقدار ثانیه.</summary>
    public static TimeUnit Best(decimal seconds) =>
        seconds != 0 && seconds % 3600 == 0 ? TimeUnit.Hour : seconds != 0 && seconds % 60 == 0 ? TimeUnit.Minute : TimeUnit.Second;

    /// <summary>متن خوانا: «۲ ساعت و ۱۵ دقیقه».</summary>
    public static string Text(decimal seconds)
    {
        var s = (long)Math.Round(seconds);
        var (h, m, sec) = (s / 3600, s % 3600 / 60, s % 60);
        var parts = new List<string>();
        if (h > 0) parts.Add($"{h:#,0} ساعت");
        if (m > 0) parts.Add($"{m} دقیقه");
        if (sec > 0 || parts.Count == 0) parts.Add($"{sec} ثانیه");
        return string.Join(" و ", parts);
    }
}
