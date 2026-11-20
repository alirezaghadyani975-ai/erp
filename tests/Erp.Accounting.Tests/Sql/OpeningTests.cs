using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>سند افتتاحیه (یکی در سال، روز اول سال) و بررسی ردیف‌ها پیش از ورود از اکسل.</summary>
[Collection(SqlCollection.Name)]
public class OpeningTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static short? _year;

    private async Task<short> YearAsync()
    {
        await Lock.WaitAsync();
        try { return _year ??= await db.Run<FiscalYearService, short>(s => s.CreateAsync(1411, 1, default)); }
        finally { Lock.Release(); }
    }

    private static VoucherLineRequest Line(string account, decimal debit, decimal credit, params string?[] details) =>
        new(account, details.Length == 0 ? null : [.. details], debit, credit);

    [Fact]
    public async Task Opening_voucher_is_unique_per_year_and_dated_on_first_day()
    {
        if (!db.Enabled) return;
        var year = await YearAsync();
        var lines = new List<VoucherLineRequest> { Line("3701", 0, 100), Line("6001", 100, 0) };

        var wrongDate = await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService, long>(s =>
            s.CreateAsync(new SaveVoucherRequest(year, "1411/01/02", null, lines, VoucherType.Opening), default)));
        Assert.Contains("روز اول سال", wrongDate.Message);

        var id = await db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, "1411/01/01", "افتتاحیه", lines, VoucherType.Opening), default));
        var second = await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService, long>(s =>
            s.CreateAsync(new SaveVoucherRequest(year, "1411/01/01", null, lines, VoucherType.Opening), default)));
        Assert.Contains("سند افتتاحیه دارد", second.Message);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService>(s =>
            s.UpdateAsync(id, new SaveVoucherRequest(year, "1411/02/01", null, lines), default)));

        // سند بستن دستی ساخته نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<VoucherService, long>(s =>
            s.CreateAsync(new SaveVoucherRequest(year, "1411/12/29", null, lines, VoucherType.Closing), default)));

        // بعد از ابطال، افتتاحیه جدید مجاز است
        await db.Run<VoucherService>(s => s.VoidAsync(id, default));
        var again = await db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, "1411/01/01", null, lines, VoucherType.Opening), default));
        Assert.True(again > id);
        await db.Run<VoucherService>(s => s.DeleteAsync(again, default));
    }

    [Fact]
    public async Task Check_reports_every_bad_row_without_saving()
    {
        if (!db.Enabled) return;
        var year = await YearAsync();
        var request = new SaveVoucherRequest(year, "1411/03/01", "ورود از اکسل",
        [
            Line("3701", 0, 500),
            Line("9999", 100, 0),             // حساب ناموجود
            Line("2411", 200, 0),             // تفصیلی اجباری ندارد
            Line("2411", 200, 0, "999999"),   // تفصیلی ناموجود
            Line("3701", 10, 20),             // هم بدهکار هم بستانکار
        ], VoucherType.Normal);
        var before = await db.Run<VoucherService, List<VoucherSummaryDto>>(s => s.ListAsync(year, null, default));
        var check = await db.Run<VoucherService, VoucherCheckResult>(s => s.CheckAsync(request, default));
        Assert.Equal(4, check.Errors.Count);
        Assert.Equal(["ردیف 2", "ردیف 3", "ردیف 4", "ردیف 5"], check.Errors.Select(e => e[..e.IndexOf(':')]));
        Assert.False(check.IsBalanced);
        Assert.Equal(before.Count, (await db.Run<VoucherService, List<VoucherSummaryDto>>(s => s.ListAsync(year, null, default))).Count);

        var ok = await db.Run<VoucherService, VoucherCheckResult>(s => s.CheckAsync(request with
        {
            Lines = [Line("3701", 0, 500), Line("6001", 500, 0)]
        }, default));
        Assert.True(ok.IsValid && ok.IsBalanced);

        var badDate = await db.Run<VoucherService, VoucherCheckResult>(s => s.CheckAsync(request with { Date = "1411/13/01" }, default));
        Assert.Single(badDate.Errors);
    }
}
