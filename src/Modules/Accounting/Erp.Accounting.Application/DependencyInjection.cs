using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Lookups;
using Erp.Accounting.Application.Persons;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Accounting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountingApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<DetailService>();
        services.AddScoped<PersonService>();
        services.AddScoped<FiscalYearService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<LookupService>();
        services.AddScoped<ReportService>();
        return services;
    }
}
