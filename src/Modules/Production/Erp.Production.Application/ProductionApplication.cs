using Erp.Inventory.Contracts;
using Erp.Personnel.Contracts;
using Erp.Production.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Production.Application;

public interface IProductionDbContext
{
    IQueryable<Bom> Boms { get; }
    IQueryable<ProductionReport> Reports { get; }

    /// <summary>فقط‌خواندنی: کالاها با قاعده‌های نوع کالا.</summary>
    IQueryable<ProductionItemRef> Items { get; }
    IQueryable<WarehouseRef> Warehouses { get; }
    IQueryable<CenterRef> Centers { get; }
    IQueryable<EmployeeRef> Employees { get; }
    IQueryable<StockDocRef> StockDocs { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class ProductionItemRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string Unit { get; init; } = "";
    public byte? CategoryId { get; init; }
    public string? Category { get; init; }
    public bool CanInput { get; init; }
    public bool CanOutput { get; init; }
    public bool IsScrap { get; init; }
    public bool IsActive { get; init; }
}

public sealed class WarehouseRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class CenterRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class StockDocRef
{
    public long Id { get; init; }
    public StockDocType DocType { get; init; }
    public int DocNo { get; init; }
    public int WarehouseId { get; init; }
}

public static class ProductionPermissions
{
    public const string BomView = "prd.bom.view";
    public const string BomCreate = "prd.bom.create";
    public const string BomEdit = "prd.bom.edit";
    public const string BomDelete = "prd.bom.delete";
    public const string ReportView = "prd.report.view";
    public const string ReportCreate = "prd.report.create";
    public const string ReportEdit = "prd.report.edit";
    public const string ReportDelete = "prd.report.delete";

    public static IPermissionSource List { get; } = new PermissionList("تولید")
        .Crud("prd.bom", "فرمول ساخت")
        .Crud("prd.report", "گزارش تولید");
}

// ---------------- DTOs ----------------

public sealed record ProductionItemDto(int Id, string Code, string Title, string Unit, string? Category, bool CanInput, bool CanOutput, bool IsScrap);

public sealed record LookupDto(int Id, string Title);

public sealed record BomListItemDto(int Id, string Code, string Title, int ProductId, string Product, decimal BaseQuantity, string Unit,
    bool IsDefault, bool IsActive, string? ValidFrom, string? ValidTo, int MaterialCount, int ReportCount);

public sealed record BomDto(int Id, string Code, string Title, int ProductId, decimal BaseQuantity, int OutputWarehouseId, int IssueWarehouseId,
    string? ValidFrom, string? ValidTo, bool IsDefault, bool IsActive, string? Notes, List<BomMaterialInput> Materials, List<BomScrapInput> Scraps,
    List<BomOperationInput> Operations, int ReportCount);

public sealed record SaveBomRequest(string Code, string Title, int ProductId, decimal BaseQuantity, int OutputWarehouseId, int IssueWarehouseId,
    string? ValidFrom, string? ValidTo, bool IsDefault, bool IsActive, string? Notes, List<BomMaterialInput> Materials, List<BomScrapInput> Scraps,
    List<BomOperationInput> Operations);

/// <summary>یک گزینه جایگزین ردیف فرمول: یک یا چند ماده با ضریب نسبت به ۱ واحد ماده اصلی.</summary>
public sealed record SubstituteOptionDto(byte OptionNo, List<BomSubstituteInput> Items);

/// <summary>ردیف مواد پیش‌نویس گزارش تولید از روی فرمول.</summary>
public sealed record DraftMaterialDto(int BomMaterialId, int ItemId, int WarehouseId, decimal StandardQty, List<SubstituteOptionDto> Options);

public sealed record ProductionDraftDto(int BomId, int ProductId, decimal Quantity, int OutputWarehouseId, List<DraftMaterialDto> Materials,
    List<ProductionScrapInput> Scraps, List<ProductionOperationInput> Operations);

public sealed record ProductionReportListItemDto(long Id, int DocNo, string Date, int ProductId, string Product, string Bom, decimal Quantity, string Unit,
    int OperatorCount, decimal OperatorHours, string? Description);

public sealed record StockDocLinkDto(long Id, StockDocType Type, int DocNo, string Warehouse);

public sealed record ProductionReportDto(long Id, int DocNo, string Date, int BomId, int ProductId, decimal Quantity, int OutputWarehouseId,
    string? Description, List<ProductionMaterialInput> Materials, List<ProductionScrapInput> Scraps, List<ProductionOperationInput> Operations,
    List<ProductionOperatorInput> Operators, List<StockDocLinkDto> StockDocs);

public sealed record SaveProductionReportRequest(string Date, int BomId, decimal Quantity, int OutputWarehouseId, string? Description,
    List<ProductionMaterialInput> Materials, List<ProductionScrapInput> Scraps, List<ProductionOperationInput> Operations,
    List<ProductionOperatorInput> Operators);

public static class DependencyInjection
{
    public static IServiceCollection AddProductionApplication(this IServiceCollection services)
    {
        services.AddSingleton(ProductionPermissions.List);
        services.AddScoped<BomService>();
        services.AddScoped<ProductionReportService>();
        return services;
    }
}
