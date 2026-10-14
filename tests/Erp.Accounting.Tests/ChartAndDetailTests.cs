using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests;

public class ChartAndDetailTests
{
    [Fact]
    public void Detail_code_must_be_in_type_range_and_same_length()
    {
        var customers = TestData.CustomersNatural();
        Assert.Throws<DomainException>(() => new DetailAccount(customers, "03001", "خارج از بازه", personId: 1));
        Assert.Throws<DomainException>(() => new DetailAccount(customers, "1001", "طول اشتباه", personId: 1));
    }

    [Fact]
    public void Person_detail_requires_a_person()
    {
        Assert.Throws<DomainException>(() => new DetailAccount(TestData.CustomersNatural(), "01001", "بدون شخص"));
    }

    [Fact]
    public void Next_code_keeps_leading_zeros_and_respects_range()
    {
        var type = TestData.CostCenters();
        Assert.Equal("00901", type.NextCode(null));
        Assert.Equal("00902", type.NextCode("00901"));
        Assert.Throws<DomainException>(() => type.NextCode("00920"));
    }

    [Fact]
    public void Detail_type_range_must_be_valid()
    {
        Assert.Throws<DomainException>(() => new DetailType("بد", OwnerModules.Manual, "0100", "00200"));
        Assert.Throws<DomainException>(() => new DetailType("بد", OwnerModules.Manual, "00200", "00100"));
        Assert.Throws<DomainException>(() => new DetailType("بد", "unknown", "00100", "00200"));
    }

    [Fact]
    public void Custom_account_code_must_start_with_parent_code()
    {
        var general = TestData.General23();
        var ok = Account.CreateCustom(general, "2391", "دریافتنی اختصاصی", AccountNature.Debit, AccountType.BalanceSheet);
        Assert.False(ok.IsStandard);
        Assert.Equal(AccountLevel.Subsidiary, ok.Level);
        Assert.Throws<DomainException>(() => Account.CreateCustom(general, "2491", "کد اشتباه", AccountNature.Debit, AccountType.BalanceSheet));
    }

    [Fact]
    public void Custom_subsidiary_requires_nature_and_type()
    {
        Assert.Throws<DomainException>(() => Account.CreateCustom(TestData.General23(), "2391", "بدون ماهیت"));
    }

    [Fact]
    public void Detail_type_in_use_cannot_be_removed_from_level()
    {
        var account = TestData.Receivable();
        var inUse = new Dictionary<int, IReadOnlySet<short>> { [1] = new HashSet<short> { 8 } };
        Assert.Throws<DomainException>(() => account.SetDetailLevel(1, true, [(short)9], inUse));
        account.SetDetailLevel(1, true, [(short)8, (short)9], inUse); // اضافه کردن نوع جدید مجاز است
        Assert.True(account.AcceptsDetail(1, 9));
    }

    [Theory]
    [InlineData(1405, 1, 1, 2026, 3, 21)]
    [InlineData(1405, 7, 21, 2026, 10, 13)]
    public void Persian_date_conversion(int py, int pm, int pd, int gy, int gm, int gd)
    {
        var date = PersianDate.FromPersian(py, pm, pd);
        Assert.Equal(new DateOnly(gy, gm, gd), date);
        Assert.Equal($"{py:0000}/{pm:00}/{pd:00}", PersianDate.ToPersian(date));
        Assert.Equal(date, PersianDate.Parse($"{py}/{pm}/{pd}"));
    }
}

public class PersianTextTests
{
    [Theory]
    [InlineData("علي  رضايي", "علی رضایی")]
    [InlineData("شركت كيان", "شرکت کیان")]
    [InlineData("۰۰۱۲۳۴", "001234")]
    [InlineData("  فاصله   زیاد  ", "فاصله زیاد")]
    public void Normalizes_arabic_letters_digits_and_spaces(string input, string expected) =>
        Assert.Equal(expected, Erp.SharedKernel.Text.PersianText.Normalize(input));
}
