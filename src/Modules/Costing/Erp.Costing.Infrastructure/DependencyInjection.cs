using Erp.Costing.Application;
using Erp.Costing.Application.Abstractions;
using Erp.Costing.Infrastructure.Persistence;
using Erp.Database;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Costing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت ماژول بهای تمام‌شده (انواع هزینه، عوامل و مراکز هزینه).</summary>
    public static IServiceCollection AddCostingModule(this IServiceCollection services)
    {
        services.AddCostingApplication();
        services.AddCompanyDatabase();

        services.AddDbContext<CostingDbContext>((sp, options) =>
            options.UseCompanyDatabase(sp));
        services.AddScoped<ICostingDbContext>(sp => sp.GetRequiredService<CostingDbContext>());
        return services;
    }
}
