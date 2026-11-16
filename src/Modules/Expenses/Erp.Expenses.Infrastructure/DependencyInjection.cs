using Erp.Database;
using Erp.Expenses.Application;
using Erp.Expenses.Infrastructure.Persistence;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Expenses.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت ماژول هزینه‌ها (شناسایی هزینه). صدور سند از طریق IVoucherPosting ماژول حسابداری.</summary>
    public static IServiceCollection AddExpensesModule(this IServiceCollection services)
    {
        services.AddExpensesApplication();
        services.AddCompanyDatabase();
        services.AddDbContext<ExpensesDbContext>((sp, options) =>
            options.UseCompanyDatabase(sp));
        services.AddScoped<IExpensesDbContext>(sp => sp.GetRequiredService<ExpensesDbContext>());
        return services;
    }
}
