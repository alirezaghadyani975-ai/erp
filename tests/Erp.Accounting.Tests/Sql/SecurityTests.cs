using Erp.Security.Application;
using Erp.Security.Domain;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Data.SqlClient;

namespace Erp.Accounting.Tests.Sql;

/// <summary>دیتابیس مرکزی: کاربر admin اولیه، ورود و قفل حساب، تغییر رمز، اجتماع مجوز نقش‌ها و محافظت دیتابیس.</summary>
[Collection(CentralCollection.Name)]
public class SecurityTests(CentralFixture db)
{
    private async Task<int> CreateUserAsync(string userName, string password = "Passw0rd99", bool admin = false) =>
        await db.Run<UserAdminService, int>(s => s.CreateAsync(new CreateUserRequest(userName, $"کاربر {userName}", password, admin), default));

    private async Task<short> CompanyIdAsync(string key) =>
        (await db.Run<CompanyAdminService, List<CompanyDto>>(s => s.ListAsync(default))).Single(c => c.Key == key).Id;

    [Fact]
    public async Task Bootstrap_creates_admin_with_one_way_hash()
    {
        if (!db.Enabled) return;
        var hash = (string)(await db.ScalarAsync("SELECT PasswordHash FROM sec.Users WHERE UserName = 'admin'"))!;
        Assert.StartsWith("PBKDF2$SHA512$", hash);
        Assert.NotEqual("admin", hash);
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM sec.Users WHERE UserName = 'admin' AND IsSystemAdmin = 1 AND MustChangePassword = 1"));

        var login = await db.Run<AuthService, LoginResult>(s => s.LoginAsync("ADMIN", "admin", "1.2.3.4", default));
        Assert.Equal(LoginStatus.Ok, login.Status);
        Assert.True(login.MustChangePassword);
        Assert.Equal(2, login.Companies!.Count); // مدیر سیستم همه شرکت‌ها
    }

    [Fact]
    public async Task Database_rejects_plain_passwords_and_log_changes()
    {
        if (!db.Enabled) return;
        await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync("UPDATE sec.Users SET PasswordHash = '123456' WHERE UserName = 'admin'"));
        await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync("DELETE sec.SecurityLog"));
    }

    [Fact]
    public async Task Failed_logins_lock_the_account_until_unlocked()
    {
        if (!db.Enabled) return;
        await db.Run<SecuritySettingsService>(s => s.SaveAsync(new SecuritySettings(3, 10, 8, 30), default));
        var id = await CreateUserAsync("lock.test");

        Task<LoginResult> Login(string password) => db.Run<AuthService, LoginResult>(s => s.LoginAsync("lock.test", password, null, default));
        Assert.Equal(LoginStatus.Invalid, (await Login("wrong1")).Status);
        Assert.Equal(LoginStatus.Invalid, (await Login("wrong2")).Status);
        var third = await Login("wrong3");
        Assert.Equal(LoginStatus.LockedOut, third.Status);
        Assert.Contains("قفل", third.Message);
        Assert.Equal(LoginStatus.LockedOut, (await Login("Passw0rd99")).Status); // رمز درست هم در زمان قفل پذیرفته نمی‌شود

        await db.Run<UserAdminService>(s => s.UnlockAsync(id, default));
        Assert.Equal(LoginStatus.Ok, (await Login("Passw0rd99")).Status);

        // کاربر ناموجود و رمز نادرست یک پیام دارند (نام کاربری حدس زده نمی‌شود)
        var unknown = await db.Run<AuthService, LoginResult>(s => s.LoginAsync("nobody", "x", null, default));
        Assert.Equal(AuthService.InvalidMessage, unknown.Message);
        Assert.True((int)(await db.ScalarAsync("SELECT COUNT(*) FROM sec.SecurityLog WHERE EventType = 4 AND UserName = 'lock.test'"))! >= 1);
        await db.Run<SecuritySettingsService>(s => s.SaveAsync(SecuritySettingsService.Defaults, default));
    }

    [Fact]
    public async Task Inactive_user_cannot_login_and_password_change_renews_stamp()
    {
        if (!db.Enabled) return;
        var id = await CreateUserAsync("change.test");
        var first = await db.Run<AuthService, LoginResult>(s => s.LoginAsync("change.test", "Passw0rd99", null, default));
        Assert.True(first.MustChangePassword);

        await Assert.ThrowsAsync<DomainException>(() => db.Run<AuthService, Guid>(s => s.ChangePasswordAsync(id, "bad", "NewPass2026", null, default)));
        var stamp = await db.Run<AuthService, Guid>(s => s.ChangePasswordAsync(id, "Passw0rd99", "NewPass2026", null, default));
        Assert.NotEqual(first.Stamp, stamp);
        Assert.False(await db.Run<AuthService, bool>(s => s.IsValidAsync(id, first.Stamp, default)));
        Assert.True(await db.Run<AuthService, bool>(s => s.IsValidAsync(id, stamp, default)));

        var second = await db.Run<AuthService, LoginResult>(s => s.LoginAsync("change.test", "NewPass2026", null, default));
        Assert.Equal(LoginStatus.Ok, second.Status);
        Assert.False(second.MustChangePassword);

        await db.Run<UserAdminService>(s => s.UpdateAsync(id, new UpdateUserRequest("کاربر تغییر", false, false), default));
        Assert.Equal(LoginStatus.Inactive, (await db.Run<AuthService, LoginResult>(s => s.LoginAsync("change.test", "NewPass2026", null, default))).Status);
    }

    [Fact]
    public async Task Session_permissions_are_union_of_active_roles_with_scopes()
    {
        if (!db.Enabled) return;
        var roleA = await db.Run<RoleService, int>(s => s.SaveAsync(null, new SaveRoleRequest("صندوق‌دار تست", null, true, ["trs.payment.view"]), default));
        var roleB = await db.Run<RoleService, int>(s => s.SaveAsync(null, new SaveRoleRequest("ثبت پرداخت تست", null, true,
            ["trs.payment.create", "acc.voucher.view"]), default));
        var roleC = await db.Run<RoleService, int>(s => s.SaveAsync(null, new SaveRoleRequest("نقش غیرفعال تست", null, false, ["acc.voucher.delete"]), default));
        await Assert.ThrowsAsync<DomainException>(() =>
            db.Run<RoleService, int>(s => s.SaveAsync(null, new SaveRoleRequest("نقش بد", null, true, ["no.such.key"]), default)));

        var user = await CreateUserAsync("union.test");
        var test = await CompanyIdAsync("Test");
        await db.Run<UserAdminService>(s => s.SetAccessAsync(user,
        [
            new UserCompanyAccessDto(test, true, [roleA, roleB, roleC],
                [new ScopeLimitDto(ScopeKind.CashBox, [5, 6]), new ScopeLimitDto(ScopeKind.BankAccount, [])])
        ], default));

        var session = await db.Run<SessionLoader, SessionInfo>(s => s.LoadAsync(user, "Test", default));
        Assert.Equal(["acc.voucher.view", "trs.payment.create", "trs.payment.view"], session.Permissions.Order().ToList());
        Assert.True(session.OwnDocsOnly);
        Assert.Equal([5, 6], session.Scopes[ScopeKind.CashBox].Order().ToList());
        Assert.Empty(session.Scopes[ScopeKind.BankAccount]); // محدود به هیچ‌کدام
        Assert.False(session.Scopes.ContainsKey(ScopeKind.FiscalYear)); // همه
        Assert.Equal(SqlFixture.ConnectionString, session.ConnectionString);

        await Assert.ThrowsAsync<ForbiddenException>(() => db.Run<SessionLoader, SessionInfo>(s => s.LoadAsync(user, "Branch", default)));

        // نقشی که به کاربر داده شده حذف نمی‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<RoleService>(s => s.DeleteAsync(roleA, default)));
        var companies = await db.Run<AuthService, IReadOnlyList<CompanyOption>>(s => s.CompaniesAsync(user, default));
        Assert.Equal(["Test"], companies.Select(c => c.Key).ToList());
    }

    [Fact]
    public async Task Only_system_admin_can_make_system_admins()
    {
        if (!db.Enabled) return;
        using (TestAccess.As(new Restricted(99, SecurityPermissions.Users)))
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => CreateUserAsync("evil.admin", admin: true));
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                db.Run<UserAdminService>(s => s.ResetPasswordAsync(db.AdminId, "Hacked2026", default)));
        }
        using (TestAccess.As(new Restricted(99, SecurityPermissions.Log)))
            await Assert.ThrowsAsync<ForbiddenException>(() => db.Run<UserAdminService, List<UserListItemDto>>(s => s.ListAsync(null, default)));

        // آخرین مدیر سیستم فعال غیرفعال نمی‌شود
        using (TestAccess.As(new FullAccess(userId: 999)))
            await Assert.ThrowsAsync<DomainException>(() =>
                db.Run<UserAdminService>(s => s.UpdateAsync(db.AdminId, new UpdateUserRequest("مدیر سیستم", false, true), default)));
    }
}
