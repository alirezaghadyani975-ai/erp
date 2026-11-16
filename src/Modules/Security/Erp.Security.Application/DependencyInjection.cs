using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Security.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSecurityApplication(this IServiceCollection services)
    {
        services.AddSingleton(SecurityPermissions.List);
        services.AddSingleton<PermissionCatalog>();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<SecuritySettingsService>();
        services.AddScoped<AuthService>();
        services.AddScoped<SessionLoader>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleService>();
        services.AddScoped<CompanyAdminService>();
        services.AddScoped<SecurityLogService>();
        return services;
    }
}
