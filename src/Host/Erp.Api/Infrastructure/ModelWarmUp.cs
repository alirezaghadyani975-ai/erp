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
        builder.UseSqlServer(connection.Connection).AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        return (DbContext)ActivatorUtilities.CreateInstance(sp, type, builder.Options, connection);
    }

    private sealed class NoCompany : ICompanyContext
    {
        public string CompanyKey => "warmup";
        public string ConnectionString => "";
    }
}
