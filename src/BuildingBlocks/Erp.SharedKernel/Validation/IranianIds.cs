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

    /// <summary>نام بانک از کد بانک داخل شبا (رقم‌های ۵ تا ۷).</summary>
    public static string? BankFromIban(string? iban)
    {
        var v = Normalize(IdKind.Iban, iban);
        return v is not null && IbanShape().IsMatch(v) ? IbanBanks.GetValueOrDefault(v.Substring(4, 3)) : null;
    }

    /// <summary>نام بانک از شش رقم اول کارت.</summary>
    public static string? BankFromCard(string? card)
    {
        var v = Digits(card);
        return v.Length >= 6 ? CardBanks.GetValueOrDefault(v[..6]) : null;
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

    private static readonly Dictionary<string, string> IbanBanks = new()
    {
        ["010"] = "بانک مرکزی", ["011"] = "صنعت و معدن", ["012"] = "ملت", ["013"] = "رفاه کارگران",
        ["014"] = "مسکن", ["015"] = "سپه", ["016"] = "کشاورزی", ["017"] = "ملی ایران", ["018"] = "تجارت",
        ["019"] = "صادرات ایران", ["020"] = "توسعه صادرات", ["021"] = "پست بانک", ["022"] = "توسعه تعاون",
        ["051"] = "مؤسسه اعتباری توسعه", ["053"] = "کارآفرین", ["054"] = "پارسیان", ["055"] = "اقتصاد نوین",
        ["056"] = "سامان", ["057"] = "پاسارگاد", ["058"] = "سرمایه", ["059"] = "سینا",
        ["060"] = "قرض‌الحسنه مهر ایران", ["061"] = "شهر", ["062"] = "آینده", ["064"] = "گردشگری",
        ["065"] = "حکمت ایرانیان", ["066"] = "دی", ["069"] = "ایران زمین", ["070"] = "قرض‌الحسنه رسالت",
        ["073"] = "کوثر", ["075"] = "ملل", ["078"] = "خاورمیانه", ["079"] = "مهر اقتصاد", ["080"] = "نور",
        ["095"] = "ایران و ونزوئلا",
    };

    private static readonly Dictionary<string, string> CardBanks = new()
    {
        ["603799"] = "ملی ایران", ["610433"] = "ملت", ["991975"] = "ملت", ["627353"] = "تجارت", ["585983"] = "تجارت",
        ["603769"] = "صادرات ایران", ["589210"] = "سپه", ["603770"] = "کشاورزی", ["639217"] = "کشاورزی",
        ["589463"] = "رفاه کارگران", ["628023"] = "مسکن", ["627961"] = "صنعت و معدن", ["627648"] = "توسعه صادرات",
        ["207177"] = "توسعه صادرات", ["627760"] = "پست بانک", ["502908"] = "توسعه تعاون", ["627412"] = "اقتصاد نوین",
        ["622106"] = "پارسیان", ["639194"] = "پارسیان", ["627884"] = "پارسیان", ["502229"] = "پاسارگاد",
        ["639347"] = "پاسارگاد", ["621986"] = "سامان", ["639607"] = "سرمایه", ["639346"] = "سینا",
        ["502806"] = "شهر", ["504706"] = "شهر", ["636214"] = "آینده", ["502938"] = "دی", ["505785"] = "ایران زمین",
        ["504172"] = "قرض‌الحسنه رسالت", ["606373"] = "قرض‌الحسنه مهر ایران", ["627488"] = "کارآفرین",
        ["502910"] = "کارآفرین", ["505416"] = "گردشگری", ["636949"] = "حکمت ایرانیان", ["505801"] = "کوثر",
        ["606256"] = "ملل", ["585947"] = "خاورمیانه", ["507677"] = "نور", ["581874"] = "ایران و ونزوئلا",
    };
}
