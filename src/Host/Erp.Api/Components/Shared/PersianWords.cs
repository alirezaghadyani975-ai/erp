namespace Erp.Api.Components.Shared;

/// <summary>عدد به حروف فارسی (برای جمع مبلغ فرم‌ها؛ مثلاً «یک میلیون و دویست هزار ریال»).</summary>
public static class PersianWords
{
    private static readonly string[] Ones = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
    private static readonly string[] Teens = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
    private static readonly string[] Tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
    private static readonly string[] Hundreds = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
    private static readonly string[] Scales = ["", "هزار", "میلیون", "میلیارد", "هزار میلیارد", "میلیون میلیارد"];

    public static string Of(decimal value)
    {
        var n = (long)Math.Round(Math.Abs(value), 0);
        if (n == 0) return "صفر";
        var parts = new List<string>();
        var scale = 0;
        while (n > 0 && scale < Scales.Length)
        {
            var chunk = (int)(n % 1000);
            if (chunk > 0) parts.Insert(0, Scales[scale] == "" ? Three(chunk) : $"{Three(chunk)} {Scales[scale]}");
            n /= 1000;
            scale++;
        }
        var text = string.Join(" و ", parts);
        return value < 0 ? $"منفی {text}" : text;
    }

    public static string Rial(decimal value) => $"{Of(value)} ریال";

    private static string Three(int n)
    {
        var words = new List<string>();
        if (n >= 100) { words.Add(Hundreds[n / 100]); n %= 100; }
        if (n >= 20) { words.Add(Tens[n / 10]); n %= 10; if (n > 0) words.Add(Ones[n]); }
        else if (n >= 10) words.Add(Teens[n - 10]);
        else if (n > 0) words.Add(Ones[n]);
        return string.Join(" و ", words);
    }
}
