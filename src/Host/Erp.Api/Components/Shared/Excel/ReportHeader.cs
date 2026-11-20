using Erp.Api.Infrastructure;

namespace Erp.Api.Components.Shared.Excel;

/// <summary>سطرهای توضیح بالای خروجی اکسل و چاپ گزارش‌ها.</summary>
public static class ReportHeader
{
    public static List<string?> Lines(AppSession session, string? from, string? to, bool? includeTemporary, params string?[] extra)
    {
        var lines = new List<string?> { session.Info?.CompanyTitle };
        if (from is not null || to is not null)
            lines.Add($"از {from} تا {to}" + includeTemporary switch
            {
                true => " — اسناد موقت و دائم",
                false => " — فقط اسناد دائم",
                null => ""
            });
        lines.AddRange(extra);
        lines.Add($"تهیه‌کننده: {session.Info?.DisplayName} — {Format.Today()} {DateTime.Now:HH:mm}");
        return lines;
    }
}
