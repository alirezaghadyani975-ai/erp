using System.Text;

namespace Erp.SharedKernel.Text;

/// <summary>
/// یکسان‌سازی متن فارسی: ي/ك عربی به ی/ک فارسی، ارقام فارسی و عربی به لاتین،
/// و حذف فاصله‌های اضافه. همه عنوان‌ها و عبارت‌های جستجو از این‌جا می‌گذرند
/// تا «علي» و «علی» یکی حساب شوند.
/// </summary>
public static class PersianText
{
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text.Trim())
        {
            var c = ch switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                'ة' => 'ه',
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch
            };
            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace) continue;
                c = ' ';
                lastWasSpace = true;
            }
            else lastWasSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string? NormalizeOrNull(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
}
