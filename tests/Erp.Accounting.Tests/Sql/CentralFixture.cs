using Erp.Security.Infrastructure;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Accounting.Tests.Sql;

/// <summary>
/// دیتابیس مرکزی تست (ErpCentral_Test، کنار ErpCompany_Test روی همان سرور). هر بار از صفر ساخته می‌شود.
/// </summary>
public sealed class CentralFixture : IAsyncLifetime
{
    public static readonly string? ConnectionString = SqlFixture.ConnectionString is { } cs
        ? new SqlConnectionStringBuilder(cs) { InitialCatalog = "ErpCentral_Test" }.ConnectionString
        : null;

    private ServiceProvider? _services;

    public bool Enabled => ConnectionString is not null;

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("ERP_TEST_SQL تنظیم نشده است.");

    public int AdminId { get; private set; }

    public async Task InitializeAsync()
    {
        if (ConnectionString is null) return;
        await SqlFixture.DropDatabaseAsync(new SqlConnectionStringBuilder(ConnectionString));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessControl>(TestAccess.Instance);
        services.AddSingleton<IClientInfo>(new TestClient());
        services.AddSecurityModule(ConnectionString);
        // مجوزهای ماژول‌ها (در برنامه هر ماژول خودش ثبت می‌کند)
        services.AddSingleton(Erp.Accounting.Application.AccountingPermissions.List);
        services.AddSingleton(Erp.Treasury.Application.TreasuryPermissions.List);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityBootstrapper>()
            .InitializeAsync(new Dictionary<string, string> { ["Test"] = SqlFixture.ConnectionString!, ["Branch"] = SqlFixture.ConnectionString! }, "admin");
        AdminId = 1;
    }

    public async Task DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
    }

    public async Task<T> Run<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task Run<TService>(Func<TService, Task> operation) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        return await cmd.ExecuteScalarAsync();
    }

    private sealed class TestClient : IClientInfo
    {
        public string? Ip => "10.0.0.1";
    }
}

[CollectionDefinition(Name)]
public sealed class CentralCollection : ICollectionFixture<CentralFixture>
{
    public const string Name = "central";
}
