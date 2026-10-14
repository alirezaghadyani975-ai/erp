using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests;

public class VoucherTests
{
    private static Voucher NewVoucher(FiscalYear? year = null) =>
        Voucher.Create(year ?? TestData.Year1405(), 1, PersianDate.FromPersian(1405, 7, 21), "فروش نسیه", 1, TestData.Now);

    private static Voucher Balanced()
    {
        var v = NewVoucher();
        v.AddLine(TestData.Receivable(), [TestData.Customer()], 1_000_000, 0);
        v.AddLine(TestData.Sales(), [], 0, 1_000_000);
        return v;
    }

    [Fact]
    public void Draft_to_temporary_to_permanent_assigns_final_number()
    {
        var v = Balanced();
        v.MarkTemporary();
        v.MarkPermanent(TestData.Year1405(), 7, approvedBy: 1, TestData.Now);

        Assert.Equal(VoucherStatus.Permanent, v.Status);
        Assert.Equal(7, v.VoucherNo);
        Assert.Equal(1, v.ApprovedBy);
        Assert.Single(v.DomainEvents);
    }

    [Fact]
    public void Draft_cannot_jump_to_permanent()
    {
        var v = Balanced();
        Assert.Throws<DomainException>(() => v.MarkPermanent(TestData.Year1405(), 1, 1, TestData.Now));
    }

    [Fact]
    public void Unbalanced_voucher_cannot_be_temporary()
    {
        var v = NewVoucher();
        v.AddLine(TestData.Receivable(), [TestData.Customer()], 1_000_000, 0);
        v.AddLine(TestData.Sales(), [], 0, 900_000);

        var ex = Assert.Throws<DomainException>(v.MarkTemporary);
        Assert.Contains("تراز", ex.Message);
    }

    [Fact]
    public void Required_detail_level_is_enforced()
    {
        var v = NewVoucher();
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Receivable(), [], 1000, 0));
    }

    [Fact]
    public void Detail_of_wrong_type_is_rejected()
    {
        var v = NewVoucher();
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Receivable(), [TestData.CostCenter()], 1000, 0));
    }

    [Fact]
    public void Detail_levels_cannot_have_gaps()
    {
        var sales = TestData.Sales();
        sales.SetDetailLevel(1, false, [TestData.CostCenters().Id]);
        sales.SetDetailLevel(2, false, [TestData.CostCenters().Id]);

        var v = NewVoucher();
        Assert.Throws<DomainException>(() => v.AddLine(sales, [null, TestData.CostCenter()], 0, 1000));
    }

    [Fact]
    public void Level_cannot_be_defined_before_previous_level()
    {
        var sales = TestData.Sales();
        Assert.Throws<DomainException>(() => sales.SetDetailLevel(2, false, [TestData.CostCenters().Id]));
    }

    [Fact]
    public void Line_must_have_exactly_one_side()
    {
        var v = NewVoucher();
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Sales(), [], 100, 100));
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Sales(), [], 0, 0));
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Sales(), [], -5, 0));
    }

    [Fact]
    public void Only_subsidiary_accounts_can_be_used()
    {
        var v = NewVoucher();
        Assert.Throws<DomainException>(() => v.AddLine(TestData.General23(), [], 100, 0));
    }

    [Fact]
    public void Date_outside_fiscal_year_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            Voucher.Create(TestData.Year1405(), 1, PersianDate.FromPersian(1406, 1, 1), null, 1, TestData.Now));
    }

    [Fact]
    public void Date_in_locked_period_is_rejected()
    {
        var year = TestData.Year1405(firstPeriod: PeriodStatus.Locked);
        Assert.Throws<DomainException>(() =>
            Voucher.Create(year, 1, PersianDate.FromPersian(1405, 1, 15), null, 1, TestData.Now));
    }

    [Fact]
    public void Temporary_voucher_is_not_editable_but_can_return_to_draft()
    {
        var v = Balanced();
        v.MarkTemporary();
        Assert.Throws<DomainException>(() => v.AddLine(TestData.Sales(), [], 0, 1));

        v.ReturnToDraft();
        v.AddLine(TestData.Sales(), [], 0, 1);
        Assert.Equal(3, v.Lines.Count);
    }

    [Fact]
    public void Permanent_voucher_cannot_be_voided()
    {
        var v = Balanced();
        v.MarkTemporary();
        v.MarkPermanent(TestData.Year1405(), 1, 1, TestData.Now);
        Assert.Throws<DomainException>(v.Void);
    }

    [Fact]
    public void Draft_voucher_can_be_voided()
    {
        var v = Balanced();
        v.Void();
        Assert.Equal(VoucherStatus.Void, v.Status);
    }

    [Fact]
    public void Removing_line_renumbers_rows()
    {
        var v = NewVoucher();
        v.AddLine(TestData.Sales(), [], 0, 1);
        v.AddLine(TestData.Sales(), [], 0, 2);
        v.AddLine(TestData.Sales(), [], 0, 3);
        v.RemoveLine(1);

        Assert.Equal([(short)1, (short)2], v.Lines.Select(l => l.RowNo));
    }

    [Fact]
    public void Currency_and_quantity_are_kept_on_line()
    {
        var v = NewVoucher();
        var line = v.AddLine(TestData.Sales(), [], 0, 5_000_000,
            new LineExtras("فروش ارزی", new CurrencyAmount(2, 100, 50_000), new LineQuantity(10, 1), "INV-12"));

        Assert.Equal((short)2, line.CurrencyId);
        Assert.Equal(50_000, line.ExchangeRate);
        Assert.Equal(10, line.Quantity);
        Assert.Equal((short)1, line.UnitId);
        Assert.Equal("INV-12", line.FollowUpNo);
    }
}
