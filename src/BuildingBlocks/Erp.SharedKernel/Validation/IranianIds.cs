using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Erp.SharedKernel.Validation;

/// <summary>نوع شناسه یا داده‌ای که قالب و رقم کنترل آن بررسی می‌شود.</summary>
public enum IdKind
{
    NationalCode,        // کد ملی ۱۰ رقمی (رقم کنترل)
    LegalNationalId,     // شناسه ملی اشخاص حقوقی ۱۱ رقمی (رقم کنترل)
    ForeignNationalCode, // کد فراگیر اتباع خارجی ۱۲ رقمی
    EconomicCode,        // کد اقتصادی: ۱۱ (حقوقی)، ۱۲ (قدیمی) یا ۱۴ رقمی (حقیقی)
    Iban,                // شبا: IR + ۲۴ رقم (mod-97)
    CardNumber,          // شماره کارت ۱۶ رقمی (الگوریتم لون)
    Mobile,              // موبایل 09xxxxxxxxx
    Phone,               // تلفن ثابت با پیش‌شماره، ۱۱ رقم
    PostalCode,          // کد پستی ۱۰ رقمی
    InsuranceNo,         // شماره بیمه تأمین اجتماعی (۶ تا ۱۰ رقم)
    BranchCode,          // کد شعبه سامانه مودیان (۴ رقم)
    Email
}

/// <summary>
/// یکسان‌سازی و کنترل صحت شناسه‌های ایرانی. هم سمت سرور (قبل از ذخیره) و هم در فرم‌ها استفاده می‌شود،
/// تا قاعده فقط یک جا نوشته شده باشد. Error برای مقدار خالی null برمی‌گرداند (الزامی بودن جدا بررسی می‌شود).
/// </summary>
public static partial class IranianIds
{
    /// <summary>ارقام فارسی/عربی به لاتین و حذف فاصله، خط تیره و نقطه.</summary>
    public static string Digits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var c = ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch
            };
            if (c is ' ' or '-' or '_' or '.' or '‌' || char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>شکل استاندارد ذخیره؛ برای مقدار خالی null.</summary>
    public static string? Normalize(IdKind kind, string? value)
    {
        var v = Digits(value);
        if (v.Length == 0) return null;
        return kind switch
        {
            IdKind.Iban => NormalizeIban(v),
            IdKind.Mobile => NormalizeMobile(v),
            IdKind.Email => value!.Trim().ToLowerInvariant(),
            _ => v
        };
    }

    /// <summary>پیام خطای فارسی، یا null اگر معتبر یا خالی باشد.</summary>
    public static string? Error(IdKind kind, string? value)
    {
        var v = Normalize(kind, value);
        if (v is null) return null;
        return kind switch
        {
            IdKind.NationalCode => !AllDigits(v, 10) ? "کد ملی باید ۱۰ رقم باشد."
                : !IsValidNationalCode(v) ? "کد ملی معتبر نیست (رقم کنترل نمی‌خواند)." : null,
            IdKind.LegalNationalId => !AllDigits(v, 11) ? "شناسه ملی باید ۱۱ رقم باشد."
                : !IsValidLegalNationalId(v) ? "شناسه ملی معتبر نیست (رقم کنترل نمی‌خواند)." : null,
            IdKind.ForeignNationalCode => AllDigits(v, 12) ? null : "کد فراگیر اتباع باید ۱۲ رقم باشد.",
            IdKind.EconomicCode => v.All(char.IsAsciiDigit) && v.Length is 11 or 12 or 14 ? null
                : "کد اقتصادی باید ۱۱، ۱۲ یا ۱۴ رقم باشد.",
            IdKind.Iban => !IbanShape().IsMatch(v) ? "شبا باید IR و ۲۴ رقم باشد."
                : !IsValidIban(v) ? "شبا معتبر نیست (رقم کنترل نمی‌خواند)." : null,
            IdKind.CardNumber => !AllDigits(v, 16) ? "شماره کارت باید ۱۶ رقم باشد."
                : !IsValidLuhn(v) ? "شماره کارت معتبر نیست." : null,
            IdKind.Mobile => MobileShape().IsMatch(v) ? null : "موبایل باید ۱۱ رقم و با 09 شروع شود.",
            IdKind.Phone => PhoneShape().IsMatch(v) ? null : "تلفن را با پیش‌شماره و ۱۱ رقم وارد کنید (مثل 02188776655).",
            IdKind.PostalCode => !AllDigits(v, 10) ? "کد پستی باید ۱۰ رقم باشد."
                : !PostalShape().IsMatch(v) ? "کد پستی معتبر نیست." : null,
            IdKind.InsuranceNo => v.All(char.IsAsciiDigit) && v.Length is >= 6 and <= 10 ? null
                : "شماره بیمه باید ۶ تا ۱۰ رقم باشد.",
            IdKind.BranchCode => AllDigits(v, 4) ? null : "کد شعبه باید ۴ رقم باشد (مثل 0000).",
            IdKind.Email => EmailShape().IsMatch(v) ? null : "ایمیل معتبر نیست.",
            _ => null
        };
    }

    public static bool IsValid(IdKind kind, string? value) => Error(kind, value) is null;

    /// <summary>حداکثر طول ورودی (با کاراکترهای جداکننده‌ای که کاربر ممکن است بگذارد).</summary>
    public static int MaxInputLength(IdKind kind) => kind switch
    {
        IdKind.Iban => 34,
        IdKind.CardNumber => 19,
        IdKind.Email => 100,
        _ => 16
    };

    public static string Placeholder(IdKind kind) => kind switch
    {
        IdKind.NationalCode => "۱۰ رقم",
        IdKind.LegalNationalId => "۱۱ رقم",
        IdKind.ForeignNationalCode => "۱۲ رقم",
        IdKind.EconomicCode => "۱۱، ۱۲ یا ۱۴ رقم",
        IdKind.Iban => "IR000000000000000000000000",
        IdKind.CardNumber => "۱۶ رقم",
        IdKind.Mobile => "09xxxxxxxxx",
        IdKind.Phone => "021xxxxxxxx",
        IdKind.PostalCode => "۱۰ رقم",
        IdKind.BranchCode => "0000",
        _ => ""
    };

    // ------------------------------------------------------------------ الگوریتم‌ها

    /// <summary>کد ملی: مجموع رقم‌های ۱ تا ۹ ضربدر ۱۰ تا ۲، باقیمانده بر ۱۱.</summary>
    public static bool IsValidNationalCode(string code)
    {
        if (!AllDigits(code, 10) || code.Distinct().Count() == 1) return false;
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (code[i] - '0') * (10 - i);
        var r = sum % 11;
        var check = code[9] - '0';
        return r < 2 ? check == r : check == 11 - r;
    }

    /// <summary>شناسه ملی حقوقی: رقم دهم + ۲ به هر رقم اضافه و با ضرایب ۲۹،۲۷،۲۳،۱۹،۱۷ جمع می‌شود.</summary>
    public static bool IsValidLegalNationalId(string id)
    {
        if (!AllDigits(id, 11) || id.Distinct().Count() == 1) return false;
        int[] coefficients = [29, 27, 23, 19, 17, 29, 27, 23, 19, 17];
        var d = (id[9] - '0') + 2;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += ((id[i] - '0') + d) * coefficients[i];
        var r = sum % 11;
        if (r == 10) r = 0;
        return r == id[10] - '0';
    }

    /// <summary>شبا: چهار نویسه اول به انتها، IR به 1827، باقیمانده بر ۹۷ باید ۱ شود.</summary>
    public static bool IsValidIban(string iban)
    {
        if (!IbanShape().IsMatch(iban)) return false;
        var rearranged = iban[4..] + "1827" + iban[2..4];
        return BigInteger.Parse(rearranged) % 97 == 1;
    }

    public static bool IsValidLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (doubleIt) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    /// <summary>کد سه‌رقمی بانک داخل شبا (رقم‌های ۵ تا ۷)، برای پیدا کردن بانک در جدول بانک‌ها.</summary>
    public static string? IbanBankCode(string? iban)
    {
        var v = Normalize(IdKind.Iban, iban);
        return v is not null && IbanShape().IsMatch(v) ? v.Substring(4, 3) : null;
    }

    /// <summary>شش رقم اول کارت (پیش‌شماره بانک).</summary>
    public static string? CardPrefix(string? card)
    {
        var v = Digits(card);
        return v.Length >= 6 && v[..6].All(char.IsAsciiDigit) ? v[..6] : null;
    }

    private static string NormalizeIban(string v)
    {
        v = v.ToUpperInvariant();
        return v.All(char.IsAsciiDigit) && v.Length == 24 ? "IR" + v : v;
    }

    private static string NormalizeMobile(string v)
    {
        if (v.StartsWith("+98")) v = "0" + v[3..];
        else if (v.StartsWith("0098")) v = "0" + v[4..];
        else if (v.StartsWith('9') && v.Length == 10) v = "0" + v;
        return v;
    }

    private static bool AllDigits(string v, int length) => v.Length == length && v.All(char.IsAsciiDigit);

    [GeneratedRegex(@"^IR\d{24}$")]
    private static partial Regex IbanShape();

    [GeneratedRegex(@"^09\d{9}$")]
    private static partial Regex MobileShape();

    [GeneratedRegex(@"^0[1-8]\d{9}$")]
    private static partial Regex PhoneShape();

    // ۵ رقم اول بدون ۰ و ۲ (رقم پنجم بدون ۵) و ۴ رقم تکراری در ابتدا مجاز نیست — قاعده شرکت ملی پست
    [GeneratedRegex(@"^(?!(\d)\1{3})[13-9]{4}[1346-9][013-9]{5}$")]
    private static partial Regex PostalShape();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();
}
