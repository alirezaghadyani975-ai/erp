using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>دائم کردن گروهی به ترتیب تاریخ، شماره‌گذاری مجدد اسناد دائم و دفاتر روزنامه و کل (سال ۱۴۱۴).</summary>
[Collection(SqlCollection.Name)]
public class NumberingTests(SqlFixture db)
{
    [Fact]
    public async Task Permanent_numbers_follow_dates_and_books_use_them()
    {
        if (!db.Enabled) return;
        var year = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1414, 1, default));
        async Task<long> Temp(string date, decimal amount)
        {
            var id = await db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, date, $"سند {date}",
                [new("7001", null, amount, 0), new("6001", null, 0, amount)]), default));
            await db.Run<VoucherService>(s => s.MarkTemporaryAsync(id, default));
            return id;
        }
        async Task<Dictionary<long, int?>> Numbers() =>
            (await db.Run<VoucherService, List<VoucherSummaryDto>>(s => s.ListAsync(year, null, default))).ToDictionary(v => v.Id, v => v.VoucherNo);

        var march = await Temp("1414/03/01", 300);
        var jan = await Temp("1414/01/15", 100);
        var feb = await Temp("1414/02/10", 200);

        var status = await db.Run<VoucherNumberingService, NumberingStatusDto>(s => s.StatusAsync(year, "1414/02/31", default));
        Assert.Equal(2, status.Temporary);
        Assert.Equal(2, await db.Run<VoucherNumberingService, int>(s => s.MakePermanentAsync(year, "1414/02/31", default)));
        var n = await Numbers();
        Assert.Equal((1, 2, (int?)null), (n[jan]!.Value, n[feb]!.Value, n[march]));

        await db.Run<VoucherService, int>(s => s.MarkPermanentAsync(march, default)); // 3
        var late = await Temp("1414/01/20", 50);
        await db.Run<VoucherService, int>(s => s.MarkPermanentAsync(late, default)); // 4، خارج از ترتیب
        status = await db.Run<VoucherNumberingService, NumberingStatusDto>(s => s.StatusAsync(year, null, default));
        Assert.Equal(3, status.OutOfOrder);
        Assert.Equal(2, status.FirstOutOfOrderNo);

        Assert.Equal(3, await db.Run<VoucherNumberingService, int>(s => s.RenumberAsync(year, default)));
        n = await Numbers();
        Assert.Equal([1, 2, 3, 4], new[] { jan, late, feb, march }.Select(id => n[id]!.Value));
        Assert.Equal(0, (await db.Run<VoucherNumberingService, NumberingStatusDto>(s => s.StatusAsync(year, null, default))).OutOfOrder);

        // سند دائم جز شماره همچنان تغییر نمی‌کند
        await Assert.ThrowsAnyAsync<Exception>(() => db.Run<VoucherService>(s => s.VoidAsync(feb, default)));

        // دفتر روزنامه: هر سند اول بدهکار (۷۰) بعد بستانکار (۶۰)، به ترتیب شماره
        var journal = await db.Run<LegalBookService, JournalDto>(s => s.JournalAsync(new ReportFilter(year), default));
        Assert.Equal(4, journal.VoucherCount);
        Assert.Equal(journal.TotalDebit, journal.TotalCredit);
        Assert.Equal(650m, journal.TotalDebit);
        Assert.Equal([(1, "70"), (1, "60"), (2, "70"), (2, "60")], journal.Rows.Take(4).Select(r => (r.VoucherNo, r.GeneralCode)));
        Assert.Equal("1414/01/20", journal.Rows[2].Date);

        // دفتر کل: از ۱۴۱۴/۰۲/۰۱ با نقل از قبل
        var ledger = await db.Run<LegalBookService, GeneralLedgerDto>(s => s.GeneralLedgerAsync(
            new ReportFilter(year, FromDate: "1414/02/01"), "60", "70", default));
        Assert.Equal(["60", "70"], ledger.Accounts.Select(a => a.Code));
        var sales = ledger.Accounts[0];
        Assert.Equal(-150m, sales.Opening);
        Assert.Equal([3, 4], sales.Rows.Select(r => r.VoucherNo));
        Assert.Equal(-650m, sales.Ending);
    }
}
