using Erp.SharedKernel.Validation;

namespace Erp.Accounting.Tests;

public class IranianIdsTests
{
    [Theory]
    [InlineData("0010532129")]
    [InlineData("4499876540")]
    [InlineData("۰۰۱۰۵۳۲۱۲۹")]      // ارقام فارسی
    [InlineData("001-053212-9")]    // با خط تیره
    public void Valid_national_codes(string code) => Assert.Null(IranianIds.Error(IdKind.NationalCode, code));

    [Theory]
    [InlineData("0010532128", "رقم کنترل")]
    [InlineData("1111111111", "رقم کنترل")]
    [InlineData("12345", "۱۰ رقم")]
    [InlineData("00105321a9", "۱۰ رقم")]
    public void Invalid_national_codes(string code, string message) =>
        Assert.Contains(message, IranianIds.Error(IdKind.NationalCode, code));

    [Theory]
    [InlineData("10100000006", true)]
    [InlineData("10862264127", true)]
    [InlineData("10862264128", false)]
    [InlineData("1086226412", false)]
    public void Legal_national_id(string id, bool valid) => Assert.Equal(valid, IranianIds.IsValid(IdKind.LegalNationalId, id));

    [Theory]
    [InlineData("IR110170000000123456789001", true)]
    [InlineData("ir11 0170 0000 0012 3456 7890 01", true)] // فاصله و حروف کوچک
    [InlineData("110170000000123456789001", true)]          // بدون IR
    [InlineData("IR120170000000123456789001", false)]
    [InlineData("IR1101700000001234567890", false)]
    public void Iban(string iban, bool valid) => Assert.Equal(valid, IranianIds.IsValid(IdKind.Iban, iban));

    [Fact]
    public void Iban_is_normalized_and_bank_detected()
    {
        Assert.Equal("IR110170000000123456789001", IranianIds.Normalize(IdKind.Iban, "ir11-0170-0000-0012-3456-7890-01"));
        Assert.Equal("017", IranianIds.IbanBankCode("IR110170000000123456789001"));
        Assert.Equal("056", IranianIds.IbanBankCode("IR150560000000000001234567"));
    }

    [Theory]
    [InlineData("6037991234567893", true)]
    [InlineData("6037-9912-3456-7893", true)]
    [InlineData("6037991234567894", false)]
    public void Card_number(string card, bool valid) => Assert.Equal(valid, IranianIds.IsValid(IdKind.CardNumber, card));

    [Fact]
    public void Card_prefix() => Assert.Equal("610433", IranianIds.CardPrefix("6104-3300-0000-0003"));

    [Theory]
    [InlineData("09121234567", "09121234567")]
    [InlineData("+989121234567", "09121234567")]
    [InlineData("9121234567", "09121234567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "09121234567")]
    public void Mobile_is_normalized(string input, string expected)
    {
        Assert.Equal(expected, IranianIds.Normalize(IdKind.Mobile, input));
        Assert.True(IranianIds.IsValid(IdKind.Mobile, input));
    }

    [Theory]
    [InlineData("1981735861", true)]
    [InlineData("0123456789", false)] // با صفر شروع نمی‌شود
    [InlineData("1111123456", false)] // چهار رقم تکراری
    [InlineData("12345", false)]
    public void Postal_code(string code, bool valid) => Assert.Equal(valid, IranianIds.IsValid(IdKind.PostalCode, code));

    [Theory]
    [InlineData("10862264127", true)]    // ۱۱ رقمی حقوقی
    [InlineData("411111111111", true)]   // ۱۲ رقمی قدیمی
    [InlineData("12345678901234", true)] // ۱۴ رقمی حقیقی
    [InlineData("1234567890123", false)]
    public void Economic_code(string code, bool valid) => Assert.Equal(valid, IranianIds.IsValid(IdKind.EconomicCode, code));

    [Fact]
    public void Empty_values_are_not_errors()
    {
        foreach (var kind in Enum.GetValues<IdKind>())
        {
            Assert.Null(IranianIds.Error(kind, null));
            Assert.Null(IranianIds.Error(kind, "  "));
        }
    }
}
