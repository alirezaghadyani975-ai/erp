using Erp.Accounting.Application;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Infrastructure.Persistence;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Erp.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Accounting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت کامل ماژول حسابداری (لایه کاربرد + دیتابیس).</summary>
    public static IServiceCollection AddAccountingModule(this IServiceCollection services)
    {
        services.AddAccountingApplication();

        services.TryAddScoped<CompanyConnection>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CompanyConnection>());
        services.AddDbContext<AccountingDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetRequiredService<CompanyConnection>().Connection));

        services.AddScoped<IAccountingDbContext>(sp => sp.GetRequiredService<AccountingDbContext>());
        return services;
    }
}
