using System.Globalization;

namespace Erp.Api.Components.Shared.Inputs;

/// <summary>فرهنگ فارسی با تقویم هجری شمسی برای تقویم‌های رابط کاربری (MudDatePicker).</summary>
public static class PersianCulture
{
    public static readonly CultureInfo Instance = Create();

    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("fa-IR").Clone();
        var f = culture.DateTimeFormat;
        if (f.Calendar is not PersianCalendar)
            f.Calendar = new PersianCalendar();

        string[] months = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند", ""];
        f.MonthNames = f.AbbreviatedMonthNames = f.MonthGenitiveNames = f.AbbreviatedMonthGenitiveNames = months;
        f.DayNames = ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"];
        f.AbbreviatedDayNames = f.ShortestDayNames = ["ی", "د", "س", "چ", "پ", "ج", "ش"];
        f.FirstDayOfWeek = DayOfWeek.Saturday;
        f.ShortDatePattern = "yyyy/MM/dd";
        f.DateSeparator = "/";
        return culture;
    }
}
