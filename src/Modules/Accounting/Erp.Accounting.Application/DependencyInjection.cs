using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Lookups;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Erp.BaseData.Contracts;
using Erp.Costing.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Accounting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountingApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<DetailService>();
        services.AddScoped<IPersonLinks, PersonDetailLinks>();
        services.AddScoped<ICostDetailLinks, CostDetailLinks>();
        services.AddScoped<FiscalYearService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<FinanceLookupService>();
        services.AddScoped<ReportService>();
        return services;
    }
}
