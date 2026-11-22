using Erp.Accounting.Infrastructure;
using Erp.BaseData.Infrastructure;
using Erp.Costing.Infrastructure;
using Erp.Expenses.Infrastructure;
using Erp.Inventory.Infrastructure.Persistence;
using Erp.Payroll.Infrastructure;
using Erp.Personnel.Infrastructure;
using Erp.Production.Infrastructure;
using Erp.Purchases.Infrastructure;
using Erp.Sales.Infrastructure;
using Erp.Treasury.Infrastructure;
using Erp.Database;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Accounting.Tests.Sql;

/// <summary>
/// تست با SQL Server واقعی. رشته اتصال از متغیر محیطی ERP_TEST_SQL خوانده می‌شود
/// (tools\run-test.bat آن را روی نمونه .\ERP و دیتابیس ErpCompany_Test تنظیم می‌کند).
/// اگر تنظیم نشده باشد، این تست‌ها کاری نمی‌کنند و رد نمی‌شوند.
/// دیتابیس تست هر بار از صفر ساخته می‌شود؛ هرگز آن را به دیتابیس واقعی اشاره ندهید.
/// </summary>
public sealed class SqlFixture : IAsyncLifetime
{
    public static readonly string? ConnectionString = Environment.GetEnvironmentVariable("ERP_TEST_SQL");

    private ServiceProvider? _services;

    public bool Enabled => ConnectionString is not null;

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("ERP_TEST_SQL تنظیم نشده است.");

    public async Task InitializeAsync()
    {
        if (ConnectionString is null) return;

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        if (!builder.InitialCatalog.Contains("Test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("نام دیتابیس تست باید شامل Test باشد تا دیتابیس واقعی پاک نشود.");

        await DropDatabaseAsync(builder);
        await new DatabaseMigrator(NullLogger<DatabaseMigrator>.Instance).MigrateAsync(ConnectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICompanyContext>(new TestCompany(ConnectionString));
        services.AddSingleton<ICurrentUser>(new TestUser());
        services.AddSingleton<IAccessControl>(TestAccess.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddBaseDataModule();
        services.AddCostingModule();
        services.AddAccountingModule();
        services.AddExpensesModule();
        services.AddInventoryModule();
        services.AddPurchasesModule();
        services.AddSalesModule();
        services.AddTreasuryModule();
        services.AddPersonnelModule();
        services.AddPayrollModule();
        services.AddProductionModule();
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
    }

    /// <summary>اجرای یک عملیات با scope و DbContext تازه (مثل هر درخواست واقعی).</summary>
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

    internal static async Task DropDatabaseAsync(SqlConnectionStringBuilder builder)
    {
        var name = builder.InitialCatalog;
        var master = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{name}];
            END
            """;
        await cmd.ExecuteNonQueryAsync();
        SqlConnection.ClearAllPools();
    }

    private sealed record TestCompany(string ConnectionString) : ICompanyContext
    {
        public string CompanyKey => "Test";
    }

    private sealed class TestUser : ICurrentUser
    {
        public int UserId => 1;
        public string? UserName => "test";
    }
}

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>
{
    public const string Name = "sql";
}
