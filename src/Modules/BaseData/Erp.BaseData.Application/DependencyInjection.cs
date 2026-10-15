using Erp.BaseData.Application.Banks;
using Erp.BaseData.Application.Lookups;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Application.Taxes;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.BaseData.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBaseDataApplication(this IServiceCollection services)
    {
        services.AddScoped<PersonService>();
        services.AddScoped<LookupService>();
        services.AddScoped<BankService>();
        services.AddScoped<VatRateService>();
        return services;
    }
}
