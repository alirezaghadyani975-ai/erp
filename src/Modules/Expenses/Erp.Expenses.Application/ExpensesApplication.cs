using Erp.Costing.Contracts;
using Erp.Expenses.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Expenses.Application;

public interface IExpensesDbContext
{
    IQueryable<ExpenseInvoice> ExpenseInvoices { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص.</summary>
    IQueryable<SupplierRef> Suppliers { get; }

    /// <summary>فقط‌خواندنی از بهای تمام‌شده: عوامل هزینه.</summary>
    IQueryable<FactorRef> Factors { get; }

    /// <summary>فقط‌خواندنی از بهای تمام‌شده: مراکز هزینه با وظیفه نوعشان.</summary>
    IQueryable<CenterRef> Centers { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class SupplierRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class FactorRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public CostNature Nature { get; init; }
    public bool IsActive { get; init; }
}

public sealed class CenterRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public CostFunction Function { get; init; }
    public bool IsActive { get; init; }
}

public static class DependencyInjection
{
    public static IServiceCollection AddExpensesApplication(this IServiceCollection services)
    {
        services.AddSingleton(ExpensePermissions.List);
        services.AddScoped<ExpenseInvoiceService>();
        return services;
    }
}
