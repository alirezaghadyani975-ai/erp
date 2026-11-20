using Erp.Accounting.Application.Closing;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Persons;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>
/// بستن سال ۱۴۱۲ و صورت‌های مالی. اسناد:
///   افتتاحیه: دریافتنی ۲۴۱۱/مشتری بد ۱٬۰۰۰ | سود انباشته ۳۷۰۱ بس ۱٬۰۰۰
///   فروش:     دریافتنی بد ۵٬۰۰۰ | فروش ۶۰۰۱ بس ۵٬۰۰۰
///   بهای تمام‌شده: ۷۰۰۱ بد ۳٬۰۰۰ | دارایی نگهداری‌شده برای فروش ۲۹۰۱ بس ۳٬۰۰۰
/// سود سال ۲٬۰۰۰.
/// </summary>
[Collection(SqlCollection.Name)]
public class ClosingTests(SqlFixture db)
{
    [Fact]
    public async Task Statements_close_undo_and_finalize_a_year()
    {
        if (!db.Enabled) return;
        var year = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1412, 1, default));
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
        var person = await db.Run<PersonService, int>(s => s.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "مشتری", LastName = "بستن سال", RoleIds = [roles.Single(r => r.Code == RoleCodes.Customer).Id]
        }, default));
        var detail = (await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync((short?)null, "بستن سال", default)))
            .Single(d => d.PersonId == person);

        async Task<long> Voucher(string date, VoucherType type, params VoucherLineRequest[] lines)
        {
            var id = await db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, date, "تست بستن", [.. lines], type), default));
            await db.Run<VoucherService>(s => s.MarkTemporaryAsync(id, default));
            await db.Run<VoucherService, int>(s => s.MarkPermanentAsync(id, default));
            return id;
        }
        VoucherLineRequest L(string account, decimal d, decimal c, string? det = null) => new(account, det is null ? null : [det], d, c);
        await Voucher("1412/01/01", VoucherType.Opening, L("2411", 1000, 0, detail.Code), L("3701", 0, 1000));
        await Voucher("1412/03/01", VoucherType.Normal, L("2411", 5000, 0, detail.Code), L("6001", 0, 5000));
        await Voucher("1412/06/01", VoucherType.Normal, L("7001", 3000, 0), L("2901", 0, 3000));

        // صورت‌های مالی
        var bs = await db.Run<StatementService, StatementDto>(s => s.BalanceSheetAsync(new ReportFilter(year), default));
        Assert.Equal(0m, bs.Difference);
        Assert.Equal((6000m, 1000m), Values(bs, "24"));
        Assert.Equal((-3000m, 0m), Values(bs, "29"));
        var profit = bs.Rows.Single(r => r.Title.StartsWith("سود (زیان) دوره"));
        Assert.Equal(2000m, profit.Current);
        Assert.Equal(3000m, bs.Rows.Single(r => r.Title == "جمع حقوق مالکانه و بدهی‌ها").Current);

        var pl = await db.Run<StatementService, StatementDto>(s => s.IncomeStatementAsync(new ReportFilter(year), default));
        Assert.Equal(5000m, Values(pl, "60").Current);
        Assert.Equal(-3000m, Values(pl, "70").Current);
        Assert.Equal(2000m, pl.Rows.Single(r => r.Title == "سود (زیان) ناخالص").Current);
        Assert.Equal(2000m, pl.Rows.Single(r => r.Title == "سود (زیان) خالص").Current);

        // مانع: سند پیش‌نویس
        var draft = await db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, "1412/07/01", null, [L("6001", 0, 1)]), default));
        var status = await db.Run<ClosingService, ClosingStatusDto>(s => s.StatusAsync(year, default));
        Assert.Equal(ClosingStage.Open, status.Stage);
        Assert.Contains(status.Blockers, b => b.Contains("پیش‌نویس"));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ClosingService, ClosingStatusDto>(s => s.CloseAsync(year, default)));
        await db.Run<VoucherService>(s => s.DeleteAsync(draft, default));
        Assert.Equal(2000m, (await db.Run<ClosingService, ClosingStatusDto>(s => s.StatusAsync(year, default))).NetIncome);

        // صدور اسناد بستن
        var closed = await db.Run<ClosingService, ClosingStatusDto>(s => s.CloseAsync(year, default));
        Assert.Equal(ClosingStage.Pending, closed.Stage);
        Assert.Equal("1413", closed.NextYearTitle);
        Assert.Equal([VoucherType.ClosingTemporary, VoucherType.Closing, VoucherType.Opening], closed.Vouchers.Select(v => v.Type));
        var closeTemp = await Get(closed.Vouchers[0].Id);
        Assert.Equal(["6001+5000", "7001-3000", "3801-2000"], closeTemp.Lines.Select(Text));
        var closing = await Get(closed.Vouchers[1].Id);
        Assert.Equal(["2411-6000", "2901+3000", "3701+1000", "3801+2000"], closing.Lines.Select(Text).Order());
        Assert.Equal(detail.Code, closing.Lines.Single(l => l.AccountCode == "2411").Details[0]!.Code);
        var opening = await Get(closed.Vouchers[2].Id);
        Assert.Equal(["2411+6000", "2901-3000", "3701-3000"], opening.Lines.Select(Text).Order());
        Assert.Equal("1413/01/01", opening.Date);
        Assert.Equal(VoucherStatus.Temporary, opening.Status);

        // سال قفل است و اسناد بستن دستی تغییر نمی‌کنند
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService, long>(s =>
            s.CreateAsync(new SaveVoucherRequest(year, "1412/12/01", null, [L("6001", 0, 1), L("7001", 1, 0)]), default)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService>(s => s.VoidAsync(closing.Id, default)));

        // برگشت و دوباره
        var undone = await db.Run<ClosingService, ClosingStatusDto>(s => s.UndoAsync(year, default));
        Assert.Equal(ClosingStage.Open, undone.Stage);
        Assert.Empty(undone.Vouchers);
        await db.Run<ClosingService, ClosingStatusDto>(s => s.CloseAsync(year, default));

        // قطعی
        var final = await db.Run<ClosingService, ClosingStatusDto>(s => s.FinalizeAsync(year, default));
        Assert.Equal(ClosingStage.Closed, final.Stage);
        Assert.Equal([4, 5], final.Vouchers.Take(2).Select(v => v.VoucherNo!.Value));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ClosingService, ClosingStatusDto>(s => s.UndoAsync(year, default)));
        var years = await db.Run<FiscalYearService, List<FiscalYearDto>>(s => s.GetAllAsync(default));
        Assert.Equal(PeriodStatus.Closed, years.Single(y => y.Id == year).Status);

        // تراز بعد از اختتامیه صفر است؛ بدون اسناد بستن همان مانده‌ها
        var tb = await db.Run<ReportService, TrialBalanceDto>(s => s.TrialBalanceAsync(new TrialBalanceRequest(new ReportFilter(year)), default));
        Assert.All(tb.Rows, r => Assert.Equal(0m, r.EndingDebit + r.EndingCredit));
        var before = await db.Run<ReportService, TrialBalanceDto>(s => s.TrialBalanceAsync(
            new TrialBalanceRequest(new ReportFilter(year, ExcludeClosing: true)), default));
        Assert.Equal(6000m, before.Rows.Single(r => r.Code == "2411").EndingDebit);
        // صورت‌ها بعد از بستن همان قبلی‌اند
        Assert.Equal(2000m, (await db.Run<StatementService, StatementDto>(s => s.IncomeStatementAsync(new ReportFilter(year), default)))
            .Rows.Single(r => r.Title == "سود (زیان) خالص").Current);
    }

    private Task<VoucherDto> Get(long id) => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default));

    private static string Text(VoucherLineDto l) => $"{l.AccountCode}{(l.Debit > 0 ? "+" + l.Debit.ToString("0") : "-" + l.Credit.ToString("0"))}";

    private static (decimal? Current, decimal? Compare) Values(StatementDto s, string code)
    {
        var r = s.Rows.Single(x => x.Code == code);
        return (r.Current, r.Compare);
    }
}
