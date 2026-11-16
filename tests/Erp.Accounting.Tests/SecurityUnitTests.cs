using System.Security.Cryptography;
using System.Text;
using Erp.Accounting.Application;
using Erp.BaseData.Application;
using Erp.Costing.Application;
using Erp.Expenses.Application;
using Erp.Security.Application;
using Erp.Security.Domain;
using Erp.SharedKernel.Domain;
using Erp.Treasury.Application;

namespace Erp.Accounting.Tests;

/// <summary>هش رمز، قواعد رمز، قفل حساب و فهرست مجوزها (بدون دیتابیس).</summary>
public class SecurityUnitTests
{
    [Fact]
    public void Password_hash_is_one_way_salted_and_verifiable()
    {
        var h1 = PasswordHasher.Hash("Secret123");
        var h2 = PasswordHasher.Hash("Secret123");

        Assert.StartsWith($"PBKDF2$SHA512${PasswordHasher.Iterations}$", h1);
        Assert.DoesNotContain("Secret123", h1);
        Assert.NotEqual(h1, h2); // salt تصادفی: هش یک رمز برای دو کاربر یکی نیست
        Assert.True(PasswordHasher.Verify("Secret123", h1, out var rehash));
        Assert.False(rehash);
        Assert.False(PasswordHasher.Verify("secret123", h1, out _));
        Assert.False(PasswordHasher.Verify("", h1, out _));
        Assert.False(PasswordHasher.Verify("Secret123", "plain-text", out _));
    }

    [Fact]
    public void Persian_digits_in_password_equal_latin_digits()
    {
        var hash = PasswordHasher.Hash("Pass۱۲۳۴");
        Assert.True(PasswordHasher.Verify("Pass1234", hash, out _));
    }

    [Fact]
    public void Old_hash_parameters_ask_for_rehash()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var weak = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("Secret123"), salt, 1000, HashAlgorithmName.SHA512, 64);
        var stored = $"PBKDF2$SHA512$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(weak)}";

        Assert.True(PasswordHasher.Verify("Secret123", stored, out var rehash));
        Assert.True(rehash);
    }

    [Theory]
    [InlineData("short1", "حداقل")]
    [InlineData("onlyletters", "هم حرف")]
    [InlineData("12345678", "هم حرف")]
    [InlineData("ali.reza2026", "نام کاربری")]
    public void Password_policy(string password, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => PasswordPolicy.Validate(password, "ali.reza", 8));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Lockout_after_max_failures()
    {
        var now = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var user = new User("Ali.Reza", "علیرضا", PasswordHasher.Hash("Secret123"), false, now);
        Assert.Equal("ali.reza", user.UserName);

        Assert.False(user.RegisterFailure(now, 3, 15));
        Assert.False(user.RegisterFailure(now, 3, 15));
        Assert.True(user.RegisterFailure(now, 3, 15));
        Assert.True(user.IsLockedOut(now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(now.AddMinutes(16)));

        user.Unlock();
        Assert.False(user.IsLockedOut(now));
    }

    [Fact]
    public void User_requires_hashed_password_and_valid_name()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => new User("ali", "علی", "plain", false, now));
        Assert.Throws<DomainException>(() => new User("علی", "علی", PasswordHasher.Hash("Secret123"), false, now));
    }

    [Fact]
    public void Permission_keys_are_unique_across_modules()
    {
        var catalog = new PermissionCatalog([AccountingPermissions.List, BaseDataPermissions.List, CostingPermissions.List,
            ExpensePermissions.List, TreasuryPermissions.List, SecurityPermissions.List]);

        Assert.True(catalog.All.Count > 60);
        Assert.All(catalog.All, p => Assert.Matches(@"^[a-z]+\.[a-z]+\.[a-z]+$", p.Key));
        Assert.True(catalog.Contains(TreasuryPermissions.PaymentCreate));
        Assert.Equal("پرداخت - ایجاد", catalog.Title(TreasuryPermissions.PaymentCreate));
        // هر عملیات چک مجوز تعریف‌شده دارد
        Assert.All(Enum.GetValues<Erp.Treasury.Domain.ChequeOperationType>(), op => Assert.True(catalog.Contains(TreasuryPermissions.Operation(op))));
    }

    [Fact]
    public void Any_action_implies_view_of_the_same_form()
    {
        var catalog = new PermissionCatalog([AccountingPermissions.List, TreasuryPermissions.List]);
        var result = catalog.WithImplied([TreasuryPermissions.PaymentCreate, AccountingPermissions.VoucherPermanent, AccountingPermissions.TrialBalance]);

        Assert.Contains(TreasuryPermissions.PaymentView, result);
        Assert.Contains(AccountingPermissions.VoucherView, result);
        Assert.DoesNotContain("acc.report.view", result); // گزارش‌ها کلید مشاهده جدا ندارند
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void Login_throttle_blocks_an_ip_after_too_many_failures()
    {
        var throttle = new LoginThrottle(TimeProvider.System);
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++) throttle.RegisterFailure("10.1.1.1");
        Assert.False(throttle.IsBlocked("10.1.1.1"));
        throttle.RegisterFailure("10.1.1.1");
        Assert.True(throttle.IsBlocked("10.1.1.1"));
        Assert.False(throttle.IsBlocked("10.1.1.2"));
        Assert.False(throttle.IsBlocked(null));
    }
}
