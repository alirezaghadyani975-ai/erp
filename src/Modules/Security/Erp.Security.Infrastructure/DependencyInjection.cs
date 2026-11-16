using Erp.Database;
using Erp.Security.Application;
using Erp.Security.Domain;
using Erp.Security.Infrastructure.Persistence;
using Erp.SharedKernel.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Erp.Security.Infrastructure;

/// <summary>رشته اتصال دیتابیس مرکزی (ConnectionStrings:Central).</summary>
public sealed record CentralDatabase(string ConnectionString);

public static class DependencyInjection
{
    /// <summary>ماژول امنیت: کاربران، نقش‌ها، شرکت‌ها و لاگ امنیتی در دیتابیس مرکزی.</summary>
    public static IServiceCollection AddSecurityModule(this IServiceCollection services, string centralConnectionString)
    {
        services.AddSecurityApplication();
        services.AddSingleton(new CentralDatabase(centralConnectionString));
        services.AddDbContext<SecurityDbContext>(o => o.UseSqlServer(centralConnectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<ISecurityDbContext>(sp => sp.GetRequiredService<SecurityDbContext>());
        services.TryAddSingleton<DatabaseMigrator>();
        services.AddScoped<ICompanyProvisioner, CompanyProvisioner>();
        services.AddScoped<IScopeItemSource, ScopeItemSource>();
        services.AddScoped<SecurityBootstrapper>();
        return services;
    }
}

internal sealed class CompanyProvisioner(DatabaseMigrator migrator) : ICompanyProvisioner
{
    public async Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken ct) =>
        (await migrator.MigrateAsync(connectionString, ct)).AppliedScripts;
}

/// <summary>خواندن فهرست صندوق/بانک/تنخواه/سال مالی از دیتابیس یک شرکت (فقط خواندنی).</summary>
internal sealed class ScopeItemSource : IScopeItemSource
{
    public async Task<IReadOnlyDictionary<ScopeKind, IReadOnlyList<ScopeItem>>> GetAsync(string connectionString, CancellationToken ct)
    {
        const string sql = """
            SELECT 1, Id, Title, IsActive FROM trs.CashBoxes
            UNION ALL SELECT 2, Id, Title, IsActive FROM trs.BankAccounts
            UNION ALL SELECT 3, Id, Title, IsActive FROM trs.ImprestFunds
            UNION ALL SELECT 4, Id, Title, CAST(CASE WHEN [Status] = 3 THEN 0 ELSE 1 END AS BIT) FROM fin.FiscalYears
            """;
        var result = Enum.GetValues<ScopeKind>().ToDictionary(k => k, _ => new List<ScopeItem>());
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[(ScopeKind)reader.GetInt32(0)].Add(new ScopeItem(Convert.ToInt32(reader.GetValue(1)), reader.GetString(2), reader.GetBoolean(3)));
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ScopeItem>)kv.Value.OrderBy(i => i.Title).ToList());
    }
}

/// <summary>
/// آماده‌سازی دیتابیس مرکزی در شروع برنامه: اجرای اسکریپت‌های database/central، ثبت شرکت‌های appsettings
/// (بخش Companies) در کاتالوگ اگر نباشند، و ساخت کاربر admin (مدیر سیستم) اگر هیچ کاربری نباشد.
/// رمز اولیه admin باید در اولین ورود عوض شود.
/// </summary>
public sealed class SecurityBootstrapper(DatabaseMigrator migrator, CentralDatabase central, SecurityDbContext db,
    TimeProvider clock, ILogger<SecurityBootstrapper> logger)
{
    public const string AdminUserName = "admin";

    public async Task<IReadOnlyList<Company>> InitializeAsync(IReadOnlyDictionary<string, string> configuredCompanies,
        string initialAdminPassword, CancellationToken ct = default)
    {
        var result = await migrator.MigrateAsync(central.ConnectionString, ScriptSet.Central, ct);
        if (result.AppliedScripts.Count > 0)
            logger.LogInformation("دیتابیس مرکزی: اسکریپت‌های اجراشده: {Scripts}", string.Join("، ", result.AppliedScripts));

        foreach (var (key, connectionString) in configuredCompanies)
        {
            if (await db.Companies.AnyAsync(c => c.Key == key, ct)) continue;
            db.Set<Company>().Add(new Company(key, key == "Default" ? "شرکت پیش‌فرض" : key, connectionString));
            logger.LogInformation("شرکت {Company} از تنظیمات به کاتالوگ مرکزی اضافه شد.", key);
        }
        await db.SaveChangesAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var admin = new User(AdminUserName, "مدیر سیستم", PasswordHasher.Hash(initialAdminPassword), isSystemAdmin: true, now);
            db.Set<User>().Add(admin);
            db.Add(new SecurityLogEntry(SecurityEvent.UserCreated, null, AdminUserName, null, null, null,
                "ساخت خودکار مدیر سیستم در اولین اجرا؛ رمز اولیه باید در اولین ورود عوض شود", now));
            await db.SaveChangesAsync(ct);
            logger.LogWarning("کاربر {User} (مدیر سیستم) ساخته شد؛ در اولین ورود رمزش را عوض کنید.", AdminUserName);
        }

        return await db.Companies.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
    }
}
