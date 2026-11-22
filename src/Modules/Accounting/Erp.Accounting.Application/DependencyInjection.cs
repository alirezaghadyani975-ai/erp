using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Lookups;
using Erp.Accounting.Application.Posting;
using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Contracts;
using Erp.BaseData.Contracts;
using Erp.Costing.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Accounting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountingApplication(this IServiceCollection services)
    {
        services.AddSingleton(AccountingPermissions.List);
        services.AddScoped<AccountService>();
        services.AddScoped<DetailService>();
        services.AddScoped<IPersonLinks, PersonDetailLinks>();
        services.AddScoped<ICostDetailLinks, CostDetailLinks>();
        services.AddScoped<ITreasuryDetailLinks, TreasuryDetailLinks>();
        services.AddScoped<FiscalYearService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<VoucherPrintService>();
        services.AddScoped<Closing.ClosingService>();
        services.AddScoped<Reports.StatementService>();
        services.AddScoped<Reports.LegalBookService>();
        services.AddScoped<VoucherNumberingService>();
        services.AddScoped<FinanceLookupService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AccountAnalysisService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<IVoucherPosting, PostingService>();
        services.AddScoped<PostingTemplateService>();
        return services;
    }
}
