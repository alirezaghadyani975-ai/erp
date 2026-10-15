using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>
/// تراز آزمایشی و دفتر روی دیتابیس واقعی. اسناد نمونه (سال ۱۴۰۵):
///   افتتاحیه ۰۱/۰۱ (دائم):  صندوق ۲۶۰۳ بد ۵٬۰۰۰ | دریافتنی ۲۳۰۵/مشتری۱ بد ۵۰۰ | سرمایه ۳۰۰۱ بس ۵٬۵۰۰
///   ۰۲/۱۰ (دائم):           ۲۳۰۵/مشتری۱ بد ۱٬۰۰۰ | فروش ۶۱۰۱ بس ۱٬۰۰۰
///   ۰۳/۰۵ (موقت):           ۲۳۰۵/مشتری۲ بد ۳۰۰   | فروش ۶۱۰۱ بس ۳۰۰
///   ۰۳/۲۰ (دائم):           صندوق بد ۴۰۰         | ۲۳۰۵/مشتری۱ بس ۴۰۰
///   ۰۴/۰۱ (پیش‌نویس):       ۲۳۰۵/مشتری۱ بد ۹۹    | فروش بس ۹۹  ← نباید در گزارش بیاید
/// </summary>
[Collection(SqlCollection.Name)]
public class ReportTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(short YearId, int Customer1, int Customer2, short CustomerTypeId);

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;

            var yearId = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1405, 1, default));
            var types = await db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default));
            var customerType = types.Single(t => t.CodeFrom == "600001").Id;

            var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
            var customerRole = roles.Single(r => r.Code == RoleCodes.Customer).Id;

            var account = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("2411", default));
            await db.Run<AccountService>(s => s.SetDetailLevelAsync(account.Id, 1,
                new SetDetailLevelRequest(true, [customerType], [customerRole]), default));

            // تفصیلی اشخاص با تعریف شخص و نقش «مشتری» ساخته می‌شود
            var p1 = await db.Run<PersonService, int>(s => s.CreateAsync(Customer("مشتری", "یک", customerRole), default));
            var p2 = await db.Run<PersonService, int>(s => s.CreateAsync(Customer("مشتری", "دو", customerRole), default));
            var details = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(customerType, null, default));
            // صندوق تفصیلی صندوق می‌خواهد (قاعده پیش‌فرض معین 2801)
            await db.Run<Erp.Treasury.Application.TreasurySetupService, int>(s => s.SaveCashBoxAsync(null,
                new Erp.Treasury.Application.SaveCashBoxRequest("صندوق مرکزی", true, null), default));
            var (c1, code1) = details.Where(d => d.PersonId == p1).Select(d => (d.Id, d.Code)).Single();
            var (c2, code2) = details.Where(d => d.PersonId == p2).Select(d => (d.Id, d.Code)).Single();

            var opening = await Voucher(yearId, "1405/01/01", VoucherType.Opening,
                Line("2801", "100001", 5000, 0), Line("2411", code1, 500, 0), Line("3301", null, 0, 5500));
            var sale1 = await Voucher(yearId, "1405/02/10", VoucherType.Normal, Line("2411", code1, 1000, 0), Line("6001", null, 0, 1000));
            var sale2 = await Voucher(yearId, "1405/03/05", VoucherType.Normal, Line("2411", code2, 300, 0), Line("6001", null, 0, 300));
            var receipt = await Voucher(yearId, "1405/03/20", VoucherType.Normal, Line("2801", "100001", 400, 0), Line("2411", code1, 0, 400));
            await Voucher(yearId, "1405/04/01", VoucherType.Normal, Line("2411", code1, 99, 0), Line("6001", null, 0, 99));

            foreach (var id in new[] { opening, sale1, sale2, receipt })
                await db.Run<VoucherService>(s => s.MarkTemporaryAsync(id, default));
            foreach (var id in new[] { opening, sale1, receipt })
                await db.Run<VoucherService, int>(s => s.MarkPermanentAsync(id, default));

            return _seed = new Seed(yearId, c1, c2, customerType);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static SavePersonRequest Customer(string first, string last, short role) =>
        new() { Kind = PersonKind.Natural, FirstName = first, LastName = last, RoleIds = [role] };

    private static VoucherLineRequest Line(string account, string? detail, decimal debit, decimal credit) =>
        new(account, detail is null ? null : [detail], debit, credit);

    private Task<long> Voucher(short yearId, string date, VoucherType type, params VoucherLineRequest[] lines) =>
        db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(yearId, date, "تست گزارش", [.. lines], type), default));

    private Task<TrialBalanceDto> Trial(TrialBalanceRequest request) =>
        db.Run<ReportService, TrialBalanceDto>(s => s.TrialBalanceAsync(request, default));

    private Task<LedgerDto> Ledger(LedgerRequest request) =>
        db.Run<ReportService, LedgerDto>(s => s.LedgerAsync(request, default));

    private static TrialBalanceRow Row(TrialBalanceDto tb, string code) => tb.Rows.Single(r => r.Code == code);

    [Fact]
    public async Task Subsidiary_trial_balance_includes_temporary_and_skips_drafts()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId)));

        Assert.Equal(["2411", "2801", "3301", "6001"], tb.Rows.Select(r => r.Code));
        var ar = Row(tb, "2411");
        Assert.Equal((1800m, 400m, 1400m, 0m), (ar.TotalDebit, ar.TotalCredit, ar.EndingDebit, ar.EndingCredit));
        Assert.Equal((500m, 1300m, 400m), (ar.OpeningDebit, ar.PeriodDebit, ar.PeriodCredit)); // افتتاحیه = مانده ابتدای دوره
        Assert.Equal(1300m, Row(tb, "6001").EndingCredit);
        Assert.Equal(5500m, Row(tb, "3301").OpeningCredit);

        Assert.True(tb.IsBalanced);
        Assert.Equal((7200m, 7200m, 6800m, 6800m),
            (tb.Totals.TotalDebit, tb.Totals.TotalCredit, tb.Totals.EndingDebit, tb.Totals.EndingCredit));
    }

    [Fact]
    public async Task Permanent_only_excludes_temporary_vouchers()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId, IncludeTemporary: false)));

        Assert.Equal(1100m, Row(tb, "2411").EndingDebit);
        Assert.Equal(1000m, Row(tb, "6001").EndingCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public async Task Six_column_from_mid_year_moves_earlier_turnover_to_opening()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId, FromDate: "1405/03/01")));

        var ar = Row(tb, "2411");
        Assert.Equal((1500m, 0m), (ar.OpeningDebit, ar.OpeningCredit));
        Assert.Equal((300m, 400m), (ar.PeriodDebit, ar.PeriodCredit));
        Assert.Equal(1400m, ar.EndingDebit);
        Assert.Equal(1000m, Row(tb, "6001").OpeningCredit);
    }

    [Fact]
    public async Task To_date_cuts_later_vouchers()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId, ToDate: "1405/02/31")));

        Assert.Equal(1500m, Row(tb, "2411").EndingDebit);
        Assert.Equal(5000m, Row(tb, "2801").EndingDebit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public async Task Group_and_general_levels_roll_up_subsidiaries()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var groups = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId), TrialBalanceLevel.Group));
        Assert.Equal(["2", "3", "6"], groups.Rows.Select(r => r.Code));
        Assert.Equal((7200m, 400m, 6800m), (Row(groups, "2").TotalDebit, Row(groups, "2").TotalCredit, Row(groups, "2").EndingDebit));
        Assert.True(groups.IsBalanced);

        var generals = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId), TrialBalanceLevel.General, AccountCode: "2"));
        Assert.Equal(["24", "28"], generals.Rows.Select(r => r.Code));
        Assert.Equal(1400m, Row(generals, "24").EndingDebit);
    }

    [Fact]
    public async Task Detail_trial_balance_groups_by_detail_account()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId), TrialBalanceLevel.Detail,
            AccountCode: "2411", DetailTypeId: seed.CustomerTypeId));

        var c1 = tb.Rows.Single(r => r.DetailId == seed.Customer1);
        var c2 = tb.Rows.Single(r => r.DetailId == seed.Customer2);
        Assert.Equal((500m, 1000m, 400m, 1100m), (c1.OpeningDebit, c1.PeriodDebit, c1.PeriodCredit, c1.EndingDebit));
        Assert.Equal(300m, c2.EndingDebit);
        Assert.NotNull(c1.DetailTypeTitle);
    }

    [Fact]
    public async Task Voucher_number_range_uses_permanent_numbers()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        // شماره قطعی: افتتاحیه ۱، فروش ۲، دریافت ۳
        var tb = await Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId, FromVoucherNo: 2, ToVoucherNo: 3)));

        var ar = Row(tb, "2411");
        Assert.Equal((1000m, 400m), (ar.TotalDebit, ar.TotalCredit));
        Assert.DoesNotContain(tb.Rows, r => r.Code == "3301");
    }

    [Fact]
    public async Task Subsidiary_ledger_has_opening_and_running_balance()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var ledger = await Ledger(new LedgerRequest(new ReportFilter(seed.YearId, FromDate: "1405/02/01"), AccountCode: "2411"));

        Assert.Equal(500m, ledger.OpeningBalance);
        Assert.Equal([1500m, 1800m, 1400m], ledger.Rows.Select(r => r.Balance));
        Assert.Equal(["1405/02/10", "1405/03/05", "1405/03/20"], ledger.Rows.Select(r => r.Date));
        Assert.Equal((1300m, 400m, 1400m), (ledger.TotalDebit, ledger.TotalCredit, ledger.EndingBalance));
        Assert.Contains("2411", ledger.Title);
        Assert.All(ledger.Rows, r => Assert.NotNull(r.Details));
    }

    [Fact]
    public async Task Detail_ledger_spans_accounts_for_one_detail()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var ledger = await Ledger(new LedgerRequest(new ReportFilter(seed.YearId), DetailId: seed.Customer1));

        Assert.Equal(0m, ledger.OpeningBalance);
        Assert.Equal([500m, 1500m, 1100m], ledger.Rows.Select(r => r.Balance));
        Assert.Equal(VoucherStatus.Permanent, ledger.Rows[0].Status);
        Assert.Contains("مشتری یک", ledger.Title);
    }

    [Fact]
    public async Task Invalid_range_gives_persian_error()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Trial(new TrialBalanceRequest(new ReportFilter(seed.YearId, FromDate: "1405/05/01", ToDate: "1405/04/01"))));
        Assert.Contains("از تاریخ", ex.Message);

        await Assert.ThrowsAsync<DomainException>(() => Ledger(new LedgerRequest(new ReportFilter(seed.YearId))));
    }
    [Fact]
    public async Task Delete_rules_for_accounts_vouchers_years_and_persons()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();

        // حساب استاندارد حذف نمی‌شود؛ حساب اختصاصی با گردش هم نه، تا سندش حذف شود
        var cash = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("2801", default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<AccountService>(s => s.DeleteAsync(cash.Id, default)));
        var custom = await db.Run<AccountService, int>(s => s.CreateAsync(
            new CreateAccountRequest(cash.ParentId!.Value, "2898", "صندوق آزمایشی حذف", AccountNature.Debit, null), default));
        var draft = await Voucher(seed.YearId, "1405/04/02", VoucherType.Normal, Line("2898", null, 10, 0), Line("6001", null, 0, 10));
        var used = await Assert.ThrowsAsync<DomainException>(() => db.Run<AccountService>(s => s.DeleteAsync(custom, default)));
        Assert.Contains("2898", used.Message);

        // سال مالی دارای سند حذف نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<FiscalYearService>(s => s.DeleteAsync(seed.YearId, default)));

        // سند موقت حذف نمی‌شود؛ پیش‌نویس حذف می‌شود و بعد حساب هم
        await db.Run<VoucherService>(s => s.MarkTemporaryAsync(draft, default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService>(s => s.DeleteAsync(draft, default)));
        await db.Run<VoucherService>(s => s.ReturnToDraftAsync(draft, default));
        await db.Run<VoucherService>(s => s.DeleteAsync(draft, default));
        await db.Run<AccountService>(s => s.DeleteAsync(custom, default));

        // سال بدون سند حذف می‌شود
        var empty = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1410, 1, default));
        await db.Run<FiscalYearService>(s => s.DeleteAsync(empty, default));
        Assert.DoesNotContain(await db.Run<FiscalYearService, List<FiscalYearDto>>(s => s.GetAllAsync(default)), y => y.Id == empty);

        // شخص با تفصیلی دارای گردش حذف نمی‌شود
        var details = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(seed.CustomerTypeId, null, default));
        var personId = details.Single(d => d.Id == seed.Customer1).PersonId!.Value;
        var ex = await Assert.ThrowsAsync<DomainException>(() => db.Run<PersonService>(s => s.DeleteAsync(personId, default)));
        Assert.Contains("غیرفعال", ex.Message);
    }
}
