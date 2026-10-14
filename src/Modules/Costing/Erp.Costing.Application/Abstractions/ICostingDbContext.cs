using Erp.Costing.Domain;

namespace Erp.Costing.Application.Abstractions;

public interface ICostingDbContext
{
    IQueryable<CostFactor> CostFactors { get; }
    IQueryable<CostCenterType> CostCenterTypes { get; }
    IQueryable<CostCenter> CostCenters { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اقلام جدول پایه (مبنای تسهیم و جذب).</summary>
    IQueryable<LookupItemRef> LookupItems { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص (مسئول مرکز).</summary>
    IQueryable<PersonRef> Persons { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

/// <summary>قلم جدول پایه (base.LookupItems) با کد فهرستش.</summary>
public sealed class LookupItemRef
{
    public int Id { get; init; }
    public string CategoryCode { get; init; } = "";
    public string Title { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed class PersonRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
}

public static class CostLookupCodes
{
    /// <summary>فهرست «مبنای تسهیم و جذب» در جدول پایه.</summary>
    public const string CostBase = "cost_base";
}
