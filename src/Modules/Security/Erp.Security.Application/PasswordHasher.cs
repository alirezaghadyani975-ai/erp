using System.Security.Cryptography;
using System.Text;
using Erp.SharedKernel.Domain;

namespace Erp.Security.Application;

/// <summary>
/// هش یک‌طرفه رمز عبور با PBKDF2-HMAC-SHA512 و salt تصادفی ۱۶ بایتی برای هر کاربر.
/// قالب ذخیره: <c>PBKDF2$SHA512$تکرار$salt(Base64)$hash(Base64)</c>.
/// از هش نمی‌شود رمز را بیرون کشید؛ salt جدا برای هر کاربر جلوی جدول‌های آماده (rainbow) را می‌گیرد
/// و تعداد تکرار بالا حدس زدن را کند می‌کند. مقایسه در زمان ثابت انجام می‌شود.
/// ارقام فارسی رمز به لاتین تبدیل می‌شوند تا تغییر زبان صفحه‌کلید در ارقام مشکل نسازد.
/// </summary>
public static class PasswordHasher
{
    /// <summary>توصیه OWASP برای PBKDF2-HMAC-SHA512.</summary>
    public const int Iterations = 210_000;

    private const int SaltSize = 16;
    private const int HashSize = 64;
    private const string Prefix = "PBKDF2$SHA512$";

    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new DomainException("رمز عبور الزامی است.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Iterations);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>درست بودن رمز. needsRehash: هش با پارامترهای قدیمی‌تر ساخته شده و باید دوباره ساخته شود.</summary>
    public static bool Verify(string? password, string stored, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(password) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var parts = stored[Prefix.Length..].Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 1) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsRehash = ok && (iterations < Iterations || salt.Length < SaltSize || expected.Length < HashSize);
        return ok;
    }

    /// <summary>
    /// برای نام کاربری ناموجود هم همین زمان صرف می‌شود تا از زمان پاسخ نشود فهمید کاربر وجود دارد یا نه.
    /// </summary>
    public static void SimulateVerify(string? password) =>
        Derive(password ?? "", DummySalt, Iterations);

    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(SaltSize);

    private static byte[] Derive(string password, byte[] salt, int iterations, int size = HashSize) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Canonical(password)), salt, iterations, HashAlgorithmName.SHA512, size);

    private static string Canonical(string password)
    {
        var chars = password.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '۰' and <= '۹' => (char)('0' + (chars[i] - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (chars[i] - '٠')),
                _ => chars[i],
            };
        }
        return new string(chars);
    }
}

/// <summary>قواعد رمز عبور: حداقل طول، حرف و رقم، و یکی نبودن با نام کاربری.</summary>
public static class PasswordPolicy
{
    public static void Validate(string? password, string userName, int minLength)
    {
        if (string.IsNullOrEmpty(password)) throw new DomainException("رمز عبور الزامی است.");
        if (password.Length < minLength) throw new DomainException($"رمز عبور باید حداقل {minLength} نویسه باشد.");
        if (password.Length > 128) throw new DomainException("رمز عبور حداکثر ۱۲۸ نویسه است.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new DomainException("رمز عبور باید هم حرف داشته باشد هم رقم.");
        if (password.Contains(userName, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("رمز عبور نباید نام کاربری را در خود داشته باشد.");
    }
}
