using System.Globalization;

namespace Erp.SharedKernel.Calendar;

/// <summary>
/// تبدیل تاریخ شمسی و میلادی. در دیتابیس تاریخ میلادی (DateOnly) ذخیره می‌شود
/// و فقط در ورود و نمایش به شمسی تبدیل می‌شود.
/// </summary>
public static class PersianDate
{
    private static readonly PersianCalendar Calendar = new();

    public static string ToPersian(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return $"{Calendar.GetYear(dt):0000}/{Calendar.GetMonth(dt):00}/{Calendar.GetDayOfMonth(dt):00}";
    }

    public static DateOnly FromPersian(int year, int month, int day) =>
        DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));

    /// <summary>ورودی مثل 1405/07/21 یا 1405-7-21.</summary>
    public static DateOnly Parse(string value)
    {
        if (!TryParse(value, out var date))
            throw new FormatException($"تاریخ شمسی نامعتبر است: {value}");
        return date;
    }

    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('/', '-');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var y)
            || !int.TryParse(parts[1], out var m)
            || !int.TryParse(parts[2], out var d))
            return false;

        try
        {
            date = FromPersian(y, m, d);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static int PersianYear(DateOnly date) => Calendar.GetYear(date.ToDateTime(TimeOnly.MinValue));
}
