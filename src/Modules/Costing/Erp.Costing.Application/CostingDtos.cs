using Erp.Costing.Contracts;

namespace Erp.Costing.Application;

public sealed record ExpenseTypeDto(short Id, string Title, CostNature Nature, int SortOrder, bool IsActive, int FactorCount);

public sealed record SaveExpenseTypeRequest(string Title, CostNature Nature, int SortOrder, bool IsActive);

public sealed record CostFactorDto(int Id, string? Code, string Title, short ExpenseTypeId, string ExpenseTypeTitle,
    CostNature Nature, CostBehavior Behavior, decimal? VariablePercent, bool IsActive, string? Notes);

public sealed record SaveCostFactorRequest(string Title, short ExpenseTypeId, CostBehavior Behavior, decimal? VariablePercent,
    bool IsActive, string? Notes);

public sealed record CostCenterTypeDto(short Id, string Title, CostFunction Function, int SortOrder, bool IsActive, bool IsSystem,
    int CenterCount);

public sealed record SaveCostCenterTypeRequest(string Title, CostFunction Function, int SortOrder, bool IsActive);

public sealed record CostBaseDto(int Id, string Title, bool IsActive);

public sealed record CostCenterListItemDto(int Id, string? Code, string Title, short CenterTypeId, string TypeTitle,
    CostFunction Function, int? ParentId, string? ParentTitle, string? ManagerName, string? AllocationBaseTitle,
    short? AllocationOrder, string? AbsorptionBaseTitle, int TargetCount, bool IsActive);

public sealed class AllocationTargetDto
{
    public int? CenterId { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>ثبت یا ویرایش مرکز هزینه؛ فرم رابط کاربری مستقیماً به همین کلاس وصل است.</summary>
public class SaveCostCenterRequest
{
    public string Title { get; set; } = "";
    public short? CenterTypeId { get; set; }
    public int? ParentId { get; set; }
    public int? ManagerPersonId { get; set; }
    public int? AllocationBaseId { get; set; }
    public short? AllocationOrder { get; set; }
    public int? AbsorptionBaseId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<AllocationTargetDto> Allocations { get; set; } = [];
}

public sealed class CostCenterDetailsDto : SaveCostCenterRequest
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? ManagerName { get; set; }
}
