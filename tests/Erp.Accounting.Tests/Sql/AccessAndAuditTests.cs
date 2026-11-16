using Erp.Accounting.Application;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.BaseData.Application;
using Erp.BaseData.Application.Banks;
using Erp.SharedKernel.Security;
using Microsoft.Data.SqlClient;

namespace Erp.Accounting.Tests.Sql;

/// <summary>کنترل مجوز و محدوده داده در سرویس‌ها، و لاگ تغییرات (audit.ChangeLog).</summary>
[Collection(SqlCollection.Name)]
public class AccessAndAuditTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static short? _year;

    private async Task<short> YearAsync()
    {
        await Lock.WaitAsync();
        try { return _year ??= await db.Run<FiscalYearService, short>(s => s.CreateAsync(1409, 1, default)); }
        finally { Lock.Release(); }
    }

    private Task<long> DraftAsync(short year, string description) =>
        db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(year, "1409/03/01", description, []), default));

    [Fact]
    public async Task Service_rejects_action_without_permission()
    {
        if (!db.Enabled) return;
        var year = await YearAsync();
        using (TestAccess.As(new Restricted(1, AccountingPermissions.VoucherView)))
        {
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => DraftAsync(year, "بدون مجوز"));
            Assert.Contains(AccountingPermissions.VoucherCreate, ex.Message);
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                db.Run<BankService, short>(s => s.CreateAsync(new SaveBankRequest("بانک بدون مجوز", null, null, true), default)));
            // جستجوها و فهرست‌های پایه برای فرم‌های دیگر آزادند
            Assert.NotEmpty(await db.Run<BankService, List<BankDto>>(s => s.GetAllAsync(false, default)));
        }
        using (TestAccess.As(new Restricted(1, AccountingPermissions.VoucherView, AccountingPermissions.VoucherCreate)))
            Assert.True(await DraftAsync(year, "با مجوز") > 0);
    }

    [Fact]
    public async Task Fiscal_year_scope_hides_other_years()
    {
        if (!db.Enabled) return;
        var year = await YearAsync();
        var all = await db.Run<FiscalYearService, List<FiscalYearDto>>(s => s.GetAllAsync(default));
        var other = all.First(y => y.Id != year).Id;

        using (TestAccess.As(new Restricted(1, AccountingPermissions.VoucherView).Limit(ScopeKind.FiscalYear, year)))
        {
            var visible = await db.Run<FiscalYearService, List<FiscalYearDto>>(s => s.GetAllAsync(default));
            Assert.Equal([year], visible.Select(y => y.Id).ToList());
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                db.Run<VoucherService, List<VoucherSummaryDto>>(s => s.ListAsync(other, null, default)));
        }
    }

    [Fact]
    public async Task Own_docs_only_user_sees_only_own_vouchers()
    {
        if (!db.Enabled) return;
        var year = await YearAsync();
        var id = await DraftAsync(year, "سند کاربر یک"); // ثبت‌کننده: کاربر ۱ (TestUser)

        using (TestAccess.As(new Restricted(2, AccountingPermissions.VoucherView) { OwnDocsOnly = true }))
        {
            var list = await db.Run<VoucherService, List<VoucherSummaryDto>>(s => s.ListAsync(year, null, default));
            Assert.DoesNotContain(list, v => v.Id == id);
            await Assert.ThrowsAsync<ForbiddenException>(() => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default)));
        }
        using (TestAccess.As(new Restricted(1, AccountingPermissions.VoucherView) { OwnDocsOnly = true }))
            Assert.Equal(id, (await db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default))).Id);
    }

    [Fact]
    public async Task Changes_are_logged_with_old_and_new_values()
    {
        if (!db.Enabled) return;
        var id = await db.Run<BankService, short>(s => s.CreateAsync(new SaveBankRequest("بانک لاگ", null, null, true), default));
        await db.Run<BankService>(s => s.UpdateAsync(id, new SaveBankRequest("بانک لاگ اصلاح‌شده", null, null, true), default));
        await db.Run<BankService>(s => s.DeleteAsync(id, default));

        await using var connection = new SqlConnection(SqlFixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT [Action], OldValue, NewValue, ActorType, ActorId, UserName, OperationId FROM audit.ChangeLog WHERE EntityName = 'Bank' AND EntityId = @id ORDER BY Id",
            connection);
        cmd.Parameters.AddWithValue("@id", (long)id);
        var rows = new List<(string Action, string? Old, string? New, byte ActorType, int? Actor, string? User, Guid Op)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetByte(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetGuid(6)));

        Assert.Equal(["I", "U", "D"], rows.Select(r => r.Action).ToList());
        Assert.Contains("بانک لاگ", rows[0].New);
        Assert.Contains("\"Title\":\"بانک لاگ\"", rows[1].Old);           // فقط فیلد تغییرکرده
        Assert.Contains("\"Title\":\"بانک لاگ اصلاح‌شده\"", rows[1].New);
        Assert.DoesNotContain("IbanCode", rows[1].New);
        Assert.NotNull(rows[2].Old);
        Assert.All(rows, r => Assert.Equal((byte)1, r.ActorType));
        Assert.All(rows, r => Assert.Equal(1, r.Actor));
        Assert.All(rows, r => Assert.Equal("test", r.User));
        Assert.Equal(3, rows.Select(r => r.Op).Distinct().Count()); // هر عملیات شناسه خودش

        await using var tamper = new SqlCommand("DELETE audit.ChangeLog WHERE EntityName = 'Bank'", connection);
        await Assert.ThrowsAsync<SqlException>(() => tamper.ExecuteNonQueryAsync());
    }
}
