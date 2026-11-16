using Erp.Database;
using Erp.SharedKernel.Abstractions;
using Erp.Treasury.Application;
using Erp.Treasury.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Treasury.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت ماژول خزانه‌داری. صدور سند و تفصیلی صندوق/بانک/تنخواه از قراردادهای حسابداری.</summary>
    public static IServiceCollection AddTreasuryModule(this IServiceCollection services)
    {
        services.AddTreasuryApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<TreasuryDbContext>((sp, options) =>
            options.UseCompanyDatabase(sp));
        services.AddScoped<ITreasuryDbContext>(sp => sp.GetRequiredService<TreasuryDbContext>());
        return services;
    }
}
