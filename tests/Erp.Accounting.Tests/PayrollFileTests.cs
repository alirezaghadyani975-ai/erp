using Erp.Payroll.Application.Files;

namespace Erp.Accounting.Tests;

/// <summary>کدگذاری ایران سیستم و فایل dBase لیست بیمه، مطابق نمونه فایل واقعی تأمین اجتماعی.</summary>
public class PayrollFileTests
{
    [Theory]
    [InlineData("سرپرست مالی", "FCF391F52096A8A495A4A8")]   // DSW_OCP نمونه
    [InlineData("مشمول بیمه", "F9F5FE9320F1F8F5AAF5")]       // بخشی از DSK_DISC نمونه
    [InlineData("زن", "F6A5")]
    [InlineData("66/03/08", "36362F30332F3038")]             // تاریخ چپ‌به‌راست می‌ماند
    public void Iran_system_matches_social_security_sample(string text, string hex) =>
        Assert.Equal(hex, Convert.ToHexString(IranSystem.Encode(text)));

    [Fact]
    public void Lam_alef_ligature_and_mixed_digits()
    {
        Assert.Equal("F2", Convert.ToHexString(IranSystem.Encode("لا")));
        // «کد 12»: عدد سر جایش، متن معکوس
        Assert.Equal("313220A2EE", Convert.ToHexString(IranSystem.Encode("کد 12")));
    }

    [Fact]
    public void Dbf_header_and_record_layout()
    {
        var w = new DbfWriter([new DbfField("NAME", 'C', 10), new DbfField("AMOUNT", 'N', 8)]);
        w.Add("زن", 1234m);
        var bytes = w.ToArray(new DateTime(2026, 11, 21));
        Assert.Equal(0x03, bytes[0]);
        Assert.Equal(1, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(32 + 64 + 1, BitConverter.ToUInt16(bytes, 8));
        Assert.Equal(19, BitConverter.ToUInt16(bytes, 10));
        Assert.Equal(0x0D, bytes[96]);
        var record = bytes[97..116];
        Assert.Equal("20F6A52020202020202020202020203132333", Convert.ToHexString(record)[..37]);
        Assert.Equal(0x1A, bytes[^1]);
    }
}
