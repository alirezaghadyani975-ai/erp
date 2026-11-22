using Erp.Database;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Infrastructure;

/// <summary>
/// ساخت مدل EF همه DbContextها در پس‌زمینه بعد از بالا آمدن برنامه، تا اولین باز شدن فرم‌ها (به‌خصوص انبار و برگشت‌ها)
/// منتظر ساخت مدل نماند. به دیتابیس وصل نمی‌شود؛ خطای هر context فقط لاگ می‌شود.
/// </summary>
public static class ModelWarmUp
{
    public static void Schedule(WebApplication app) =>
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => Run(app.Services, app.Logger)));

    private static void Run(IServiceProvider services, ILogger logger)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Erp.", StringComparison.Ordinal) == true)
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(DbContext).IsAssignableFrom(t))
            .ToList();
        var built = 0;
        foreach (var type in types)
        {
            try
            {
                using var scope = services.CreateScope();
                using var context = Create(scope.ServiceProvider, type);
                _ = context.Model;
                built++;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "گرم کردن مدل {Context} انجام نشد", type.Name);
            }
        }
        logger.LogInformation("مدل {Built} از {Total} DbContext در {Ms} میلی‌ثانیه آماده شد", built, types.Count, started.ElapsedMilliseconds);
        CheckSqlMemory(services, logger);
    }

    /// <summary>
    /// SQL Server بدون حداقل حافظه، زیر فشار حافظه ویندوز کوچک می‌شود و کوئری‌ها برای «memory grant» صف می‌کشند
    /// (انتظار RESOURCE_SEMAPHORE تا ~۲۵ ثانیه حتی برای کوئری کوچک). اینجا فقط هشدار لاگ می‌شود؛ تنظیم با مدیر سرور است.
    /// کاربر SQL کم‌دسترسی ممکن است VIEW SERVER STATE نداشته باشد؛ آن‌وقت بررسی بی‌صدا رد می‌شود.
    /// </summary>
    private static void CheckSqlMemory(IServiceProvider services, ILogger logger)
    {
        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Central");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT (SELECT CAST(value_in_use AS INT) FROM sys.configurations WHERE name = 'min server memory (MB)'),
                       (SELECT MAX(target_memory_kb) / 1024 FROM sys.dm_exec_query_resource_semaphores WHERE resource_semaphore_id = 0),
                       (SELECT MAX(waiter_count) FROM sys.dm_exec_query_resource_semaphores)
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return;
            var (minMb, grantMb, waiters) = (reader.GetInt32(0), reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)), reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)));
            if (minMb < 512 || grantMb < 100 || waiters > 0)
                logger.LogWarning("حافظه SQL Server کم است (حداقل حافظه {Min} مگابایت، حافظه کوئری {Grant} مگابایت، در صف {Waiters}). " +
                                  "کوئری‌ها ممکن است تا ۲۵ ثانیه منتظر بمانند؛ 'min server memory' را دست‌کم ۱۰۲۴ و 'max server memory' را مناسب سرور تنظیم کنید.",
                    minMb, grantMb, waiters);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "بررسی حافظه SQL Server انجام نشد");
        }
    }

    /// <summary>
    /// context شرکت بدون نشست کاربر ساخته نمی‌شود؛ پس با اتصال خالی (باز نمی‌شود) و همان تنظیمات `UseCompanyDatabase` ساخته می‌شود
    /// تا مدل در همان کش EF بنشیند. context مرکزی (بدون CompanyConnection) از خود DI می‌آید.
    /// </summary>
    private static DbContext Create(IServiceProvider sp, Type type)
    {
        if (!type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(CompanyConnection))))
            return (DbContext)sp.GetRequiredService(type);
        var connection = new CompanyConnection(new NoCompany());
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(type))!;
        builder.UseCompanySqlServer(connection.Connection).AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        return (DbContext)ActivatorUtilities.CreateInstance(sp, type, builder.Options, connection);
    }

    private sealed class NoCompany : ICompanyContext
    {
        public string CompanyKey => "warmup";
        public string ConnectionString => "";
    }
}
