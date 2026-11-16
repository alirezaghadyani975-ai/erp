using Erp.SharedKernel.Abstractions;
using Erp.Security.Domain;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Security.Application;

/// <summary>تنظیمات امنیتی (sec.Settings) با مقدار پیش‌فرض برای کلیدهای ناموجود.</summary>
public class SecuritySettingsService(ISecurityDbContext db, IAccessControl access, IClientInfo client, TimeProvider clock)
{
    public static readonly SecuritySettings Defaults = new(5, 15, 8, 30);

    public async Task<SecuritySettings> GetAsync(CancellationToken ct)
    {
        var values = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        int Get(string key, int fallback) => values.TryGetValue(key, out var v) ? v : fallback;
        return new SecuritySettings(
            Get(nameof(SecuritySettings.MaxFailedLogins), Defaults.MaxFailedLogins),
            Get(nameof(SecuritySettings.LockoutMinutes), Defaults.LockoutMinutes),
            Get(nameof(SecuritySettings.PasswordMinLength), Defaults.PasswordMinLength),
            Get(nameof(SecuritySettings.IdleMinutes), Defaults.IdleMinutes));
    }

    public async Task<List<SettingDto>> ListAsync(CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Settings);
        return await db.Settings.AsNoTracking().OrderBy(s => s.Key)
            .Select(s => new SettingDto(s.Key, s.Title, s.Value)).ToListAsync(ct);
    }

    public async Task SaveAsync(SecuritySettings settings, CancellationToken ct)
    {
        access.Demand(SecurityPermissions.Settings);
        if (settings.MaxFailedLogins is < 0 or > 50) throw new DomainException("تعداد ورود ناموفق باید بین ۰ (بدون قفل) تا ۵۰ باشد.");
        if (settings.LockoutMinutes is < 1 or > 1440) throw new DomainException("مدت قفل باید بین ۱ تا ۱۴۴۰ دقیقه باشد.");
        if (settings.PasswordMinLength is < 6 or > 64) throw new DomainException("حداقل طول رمز باید بین ۶ تا ۶۴ باشد.");
        if (settings.IdleMinutes is < 5 or > 720) throw new DomainException("زمان خروج خودکار باید بین ۵ تا ۷۲۰ دقیقه باشد.");

        var rows = await db.Settings.ToListAsync(ct);
        var changes = new List<string>();
        void Set(string key, int value)
        {
            var row = rows.FirstOrDefault(r => r.Key == key);
            if (row is null) return;
            if (row.Value != value) changes.Add($"{row.Title}: {row.Value} ← {value}");
            row.Set(value);
        }
        Set(nameof(SecuritySettings.MaxFailedLogins), settings.MaxFailedLogins);
        Set(nameof(SecuritySettings.LockoutMinutes), settings.LockoutMinutes);
        Set(nameof(SecuritySettings.PasswordMinLength), settings.PasswordMinLength);
        Set(nameof(SecuritySettings.IdleMinutes), settings.IdleMinutes);
        if (changes.Count == 0) return;

        db.Add(new SecurityLogEntry(SecurityEvent.SettingsChanged, null, null, access.UserId, null, client.Ip,
            string.Join("؛ ", changes), clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// ورود، تغییر رمز و ثبت رویدادهای ورود/خروج. بعد از چند ورود ناموفق پشت سر هم حساب برای مدتی قفل می‌شود.
/// پیام خطای ورود برای «کاربر ناموجود» و «رمز نادرست» یکی است تا نام‌های کاربری حدس زده نشوند.
/// </summary>
public class AuthService(ISecurityDbContext db, SecuritySettingsService settings, TimeProvider clock, LoginThrottle throttle)
{
    public const string InvalidMessage = "نام کاربری یا رمز عبور نادرست است.";

    public async Task<LoginResult> LoginAsync(string? userName, string? password, string? ip, CancellationToken ct)
    {
        if (throttle.IsBlocked(ip))
            return new LoginResult(LoginStatus.LockedOut,
                "تلاش ناموفق از این دستگاه بیش از حد مجاز بود؛ چند دقیقه دیگر دوباره امتحان کنید.");
        var result = await LoginCoreAsync(userName, password, ip, ct);
        if (result.Status is LoginStatus.Invalid or LoginStatus.LockedOut) throttle.RegisterFailure(ip);
        return result;
    }

    private async Task<LoginResult> LoginCoreAsync(string? userName, string? password, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        string normalized;
        try { normalized = User.NormalizeUserName(userName); }
        catch (DomainException)
        {
            PasswordHasher.SimulateVerify(password);
            return await FailAsync(null, userName, ip, "نام کاربری نامعتبر", ct);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == normalized, ct);
        if (user is null)
        {
            PasswordHasher.SimulateVerify(password);
            return await FailAsync(null, normalized, ip, "کاربر ناموجود", ct);
        }

        if (user.IsLockedOut(now))
        {
            PasswordHasher.SimulateVerify(password);
            Log(SecurityEvent.LoginFailed, user, ip, "تلاش در زمان قفل بودن حساب", now);
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.LockedOut, LockedMessage(user.LockoutUntil!.Value, now));
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash, out var needsRehash))
        {
            var cfg = await settings.GetAsync(ct);
            var locked = user.RegisterFailure(now, cfg.MaxFailedLogins, cfg.LockoutMinutes);
            Log(SecurityEvent.LoginFailed, user, ip, "رمز نادرست", now);
            if (locked) Log(SecurityEvent.LockedOut, user, ip, $"قفل به مدت {cfg.LockoutMinutes} دقیقه بعد از {cfg.MaxFailedLogins} ورود ناموفق", now);
            await db.SaveChangesAsync(ct);
            return locked
                ? new LoginResult(LoginStatus.LockedOut, LockedMessage(user.LockoutUntil!.Value, now))
                : new LoginResult(LoginStatus.Invalid, InvalidMessage);
        }

        if (!user.IsActive)
        {
            Log(SecurityEvent.LoginFailed, user, ip, "حساب غیرفعال", now);
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.Inactive, "حساب کاربری شما غیرفعال است؛ با مدیر سیستم تماس بگیرید.");
        }

        if (needsRehash) user.UpgradeHash(PasswordHasher.Hash(password!));
        user.RegisterLogin(now);
        Log(SecurityEvent.LoginSucceeded, user, ip, null, now);
        await db.SaveChangesAsync(ct);

        return new LoginResult(LoginStatus.Ok, null, user.Id, user.UserName, user.DisplayName, user.SecurityStamp,
            user.MustChangePassword, await CompaniesAsync(user, ct));
    }

    /// <summary>تغییر رمز توسط خود کاربر (رمز فعلی لازم است). نشست‌های دیگر کاربر باطل می‌شوند.</summary>
    public async Task<Guid> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword, string? ip, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("کاربر پیدا نشد.");
        if (!user.IsActive) throw new DomainException("حساب کاربری غیرفعال است.");
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash, out _))
            throw new DomainException("رمز فعلی نادرست است.");
        if (newPassword == currentPassword) throw new DomainException("رمز جدید باید با رمز فعلی فرق داشته باشد.");

        var cfg = await settings.GetAsync(ct);
        PasswordPolicy.Validate(newPassword, user.UserName, cfg.PasswordMinLength);

        var now = clock.GetUtcNow().UtcDateTime;
        user.SetPassword(PasswordHasher.Hash(newPassword!), mustChange: false, now);
        Log(SecurityEvent.PasswordChanged, user, ip, null, now);
        await db.SaveChangesAsync(ct);
        return user.SecurityStamp;
    }

    /// <summary>نشست هنوز معتبر است؟ (کاربر فعال و مهر امنیتی عوض نشده: رمز، غیرفعال شدن، تغییر مدیر سیستم بودن)</summary>
    public async Task<bool> IsValidAsync(int userId, Guid stamp, CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && u.SecurityStamp == stamp, ct);

    public async Task<IReadOnlyList<CompanyOption>> CompaniesAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? [] : await CompaniesAsync(user, ct);
    }

    private async Task<IReadOnlyList<CompanyOption>> CompaniesAsync(User user, CancellationToken ct)
    {
        var query = db.Companies.AsNoTracking().Where(c => c.IsActive);
        if (!user.IsSystemAdmin)
            query = query.Where(c => db.UserCompanies.Any(uc => uc.UserId == user.Id && uc.CompanyId == c.Id));
        return await query.OrderBy(c => c.Title).Select(c => new CompanyOption(c.Id, c.Key, c.Title)).ToListAsync(ct);
    }

    /// <summary>ثبت رویداد ورود/خروج/انتخاب شرکت از رابط کاربری.</summary>
    public async Task LogAsync(SecurityEvent eventType, int? userId, string? userName, string? companyKey, string? ip,
        string? details, CancellationToken ct)
    {
        db.Add(new SecurityLogEntry(eventType, userId, userName, userId, companyKey, ip, details, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }

    private async Task<LoginResult> FailAsync(int? userId, string? userName, string? ip, string reason, CancellationToken ct)
    {
        db.Add(new SecurityLogEntry(SecurityEvent.LoginFailed, userId, userName, null, null, ip, reason, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStatus.Invalid, InvalidMessage);
    }

    private void Log(SecurityEvent e, User user, string? ip, string? details, DateTime now) =>
        db.Add(new SecurityLogEntry(e, user.Id, user.UserName, user.Id, null, ip, details, now));

    private static string LockedMessage(DateTime until, DateTime now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return $"حساب شما به‌دلیل ورود ناموفق مکرر قفل شده است؛ {minutes} دقیقه دیگر دوباره امتحان کنید یا از مدیر سیستم بخواهید قفل را باز کند.";
    }
}
