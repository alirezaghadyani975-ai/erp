using Erp.Costing.Application.Centers;
using Erp.Costing.Application.Factors;
using Erp.Costing.Application.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Costing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCostingApplication(this IServiceCollection services)
    {
        services.AddScoped<CostSetupService>();
        services.AddScoped<CostFactorService>();
        services.AddScoped<CostCenterService>();
        return services;
    }
}
