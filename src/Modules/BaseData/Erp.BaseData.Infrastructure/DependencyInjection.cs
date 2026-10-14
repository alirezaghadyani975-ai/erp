using Erp.BaseData.Application;
using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Infrastructure.Persistence;
using Erp.Database;
using Erp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.BaseData.Infrastructure;

public static class DependencyInjection
{
    /// <summary>ثبت ماژول اطلاعات پایه (اشخاص، جداول پایه، بانک‌ها).</summary>
    public static IServiceCollection AddBaseDataModule(this IServiceCollection services)
    {
        services.AddBaseDataApplication();
        services.TryAddScoped<CompanyConnection>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CompanyConnection>());

        services.AddDbContext<BaseDataDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetRequiredService<CompanyConnection>().Connection));
        services.AddScoped<IBaseDataDbContext>(sp => sp.GetRequiredService<BaseDataDbContext>());
        return services;
    }
}
