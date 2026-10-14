using Erp.Accounting.Application;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Infrastructure.Persistence;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Accounting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت کامل ماژول حسابداری (لایه کاربرد + دیتابیس).</summary>
    public static IServiceCollection AddAccountingModule(this IServiceCollection services)
    {
        services.AddAccountingApplication();

        services.AddDbContext<AccountingDbContext>((sp, options) =>
        {
            var company = sp.GetRequiredService<ICompanyContext>();
            options.UseSqlServer(company.ConnectionString);
        });

        services.AddScoped<IAccountingDbContext>(sp => sp.GetRequiredService<AccountingDbContext>());
        return services;
    }
}
