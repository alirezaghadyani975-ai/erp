using Erp.Costing.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Application;

public interface IInventoryDbContext
{
    IQueryable<ItemGroup> ItemGroups { get; }
    IQueryable<Item> Items { get; }
    IQueryable<ItemCategory> ItemCategories { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: واحدهای سنجش.</summary>
    IQueryable<UnitRef> Units { get; }

    /// <summary>فقط‌خواندنی از حسابداری: معین‌ها (سطح ۳).</summary>
    IQueryable<AccountRef> Accounts { get; }

    IQueryable<Warehouse> Warehouses { get; }
    IQueryable<StockDoc> StockDocs { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص (تأمین‌کننده، مشتری، انباردار).</summary>
    IQueryable<PartyRef> Parties { get; }

    /// <summary>فقط‌خواندنی از بهای تمام‌شده: عوامل و مراکز هزینه (حواله مصرف).</summary>
    IQueryable<FactorRef> Factors { get; }
    IQueryable<CenterRef> Centers { get; }

    /// <summary>فقط‌خواندنی از حسابداری: سال‌های مالی (تاریخ موجودی اول دوره).</summary>
    IQueryable<FiscalYearRef> FiscalYears { get; }
    IQueryable<FiscalPeriodRef> FiscalPeriods { get; }

    IQueryable<CostRun> CostRuns { get; }
    IQueryable<CostBalance> CostBalances { get; }

    /// <summary>مبلغ (پس از تخفیف) ردیف فاکتور خریدی که هر ردیف رسید را فاکتور کرده.</summary>
    Task<Dictionary<long, decimal>> ReceiptInvoiceAmountsAsync(IReadOnlyCollection<long> receiptLineIds, CancellationToken ct);

    /// <summary>نوشتن بهای محاسبه‌شده ردیف‌ها (UnitCost، Amount و عنصر مواد در inv.StockLineCosts).</summary>
    Task WriteCostsAsync(IReadOnlyCollection<(long LineId, decimal UnitCost, decimal Amount)> rows, CancellationToken ct);

    /// <summary>اجراهای بعد از تاریخ «نیازمند محاسبه دوباره» می‌شوند.</summary>
    Task MarkStaleAfterAsync(DateOnly date, CancellationToken ct);

    /// <summary>کالا در فاکتور خرید یا فروش یا سند انبار آمده است؟</summary>
    Task<bool> IsItemUsedAsync(int itemId, CancellationToken ct);

    /// <summary>فاکتور یا برگشتی که هر ردیف سند انبار در آن آمده (ردیف‌های فاکتورنشده در نتیجه نیستند).</summary>
    Task<Dictionary<long, InvoiceLink>> InvoiceLinksAsync(IReadOnlyCollection<long> stockLineIds, CancellationToken ct);

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class UnitRef
{
    public short Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class AccountRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class PartyRef
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
    public bool IsActive { get; init; }
}

public sealed class FiscalYearRef
{
    public short Id { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

public sealed class FiscalPeriodRef
{
    public int Id { get; init; }
    public short FiscalYearId { get; init; }
    public byte PeriodNo { get; init; }
    public string Title { get; init; } = "";
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

/// <param name="Status">null = محاسبه نشده.</param>
/// <param name="HasDocs">در این ماه سند انبار هست.</param>
public sealed record CostPeriodDto(int PeriodId, byte PeriodNo, string Title, string StartDate, string EndDate, bool HasDocs,
    CostRunStatus? Status, string? RunAt, long? VoucherId, int? VoucherTempNo, byte? VoucherStatus, decimal IssuedAmount, decimal ReceivedAmount);

/// <summary>نتیجه اجرای محاسبه یک ماه.</summary>
/// <param name="Warnings">اطلاعات ناقص که بهایش صفر حساب شد (مثلاً رسید خرید بدون فاکتور).</param>
public sealed record CostRunResultDto(int RunId, int LineCount, decimal IssuedAmount, decimal ReceivedAmount, long? VoucherId, int? VoucherTempNo,
    List<CostWarningDto> Warnings, string PeriodTitle);

/// <summary>ردیفی که اطلاعاتش برای محاسبه بها ناقص بود و بهایش صفر لحاظ شد.</summary>
public sealed record CostWarningDto(string Date, StockDocType Type, long DocId, int DocNo, short RowNo, string Item, decimal Quantity, string Problem);

/// <summary>نوع فرمی که ردیف سند انبار را فاکتور کرده است.</summary>
public enum InvoiceLinkKind : byte
{
    PurchaseInvoice = 1,
    PurchaseReturn = 2,
    SalesInvoice = 3,
    SalesReturn = 4
}

public sealed record InvoiceLink(long StockLineId, InvoiceLinkKind Kind, long DocId, int DocNo)
{
    public string Title => Kind switch
    {
        InvoiceLinkKind.PurchaseInvoice => "فاکتور خرید",
        InvoiceLinkKind.PurchaseReturn => "برگشت از خرید",
        InvoiceLinkKind.SalesInvoice => "فاکتور فروش",
        _ => "برگشت از فروش"
    };

    public string Url => Kind switch
    {
        InvoiceLinkKind.PurchaseInvoice => $"/purchases/{DocId}",
        InvoiceLinkKind.PurchaseReturn => $"/purchases/returns/{DocId}",
        InvoiceLinkKind.SalesInvoice => $"/sales/{DocId}",
        _ => $"/sales/returns/{DocId}"
    };
}

public static class InventoryPermissions
{
    public const string ItemView = "inv.item.view";
    public const string ItemCreate = "inv.item.create";
    public const string ItemEdit = "inv.item.edit";
    public const string ItemDelete = "inv.item.delete";
    public const string Groups = "inv.group.edit";
    public const string Warehouses = "inv.warehouse.edit";
    public const string ReceiptView = "inv.receipt.view";
    public const string ReceiptCreate = "inv.receipt.create";
    public const string ReceiptEdit = "inv.receipt.edit";
    public const string ReceiptDelete = "inv.receipt.delete";
    public const string IssueView = "inv.issue.view";
    public const string IssueCreate = "inv.issue.create";
    public const string IssueEdit = "inv.issue.edit";
    public const string IssueDelete = "inv.issue.delete";
    public const string Kardex = "inv.kardex.view";
    public const string Costing = "inv.costing.run";

    public static IPermissionSource List { get; } = new PermissionList("کالا و انبار")
        .Crud("inv.item", "کالا و خدمات")
        .Add("inv.group", "گروه‌های کالا", ("edit", "تعریف و اصلاح"))
        .Add("inv.warehouse", "انبارها", ("edit", "تعریف و اصلاح"))
        .Crud("inv.receipt", "رسید انبار")
        .Crud("inv.issue", "حواله انبار")
        .Add("inv.kardex", "کاردکس و موجودی", ("view", "مشاهده"))
        .Add("inv.costing", "محاسبه بهای تمام‌شده", ("run", "اجرا و مشاهده"));

    /// <summary>پیشوند مجوز فرم رسید یا حواله (برای FormToolbar).</summary>
    public static string Prefix(StockDocType type) => type.IsReceipt() ? "inv.receipt" : "inv.issue";
}

/// <param name="DocCount">تعداد سندهای انبار؛ انبار دارای سند حذف نمی‌شود.</param>
public sealed record WarehouseDto(int Id, string Code, string Title, int? KeeperId, string? KeeperName, bool IsActive, string? Notes, int DocCount);

public sealed record SaveWarehouseRequest(string Code, string Title, int? KeeperId = null, string? Notes = null, bool IsActive = true);

public sealed record StockLineRequest(int ItemId, decimal Quantity, long? SourceLineId = null, int? CostFactorId = null,
    int? CostCenterId = null, decimal? EnteredPrice = null, string? Description = null);

public sealed record SaveStockDocRequest(StockDocType Type, string Date, int WarehouseId, int? PartyId, string? Description,
    List<StockLineRequest> Lines);

/// <param name="Invoiced">ردیف‌های فاکتورشده از کل ردیف‌ها (فقط برای رسید خرید، حواله فروش و برگشت‌ها).</param>
public sealed record StockDocListItemDto(long Id, StockDocType Type, int DocNo, string Date, int WarehouseId, string Warehouse,
    int? PartyId, string? PartyName, int LineCount, decimal TotalQuantity, int Invoiced, string? Description);

/// <param name="Source">برگشت: «حواله فروش 000003 ردیف 2».</param>
/// <param name="Invoice">فاکتوری که این ردیف را فاکتور کرده (null = فاکتورنشده).</param>
public sealed record StockLineDto(long Id, short RowNo, int ItemId, string ItemCode, string ItemTitle, string UnitTitle, decimal Quantity,
    long? SourceLineId, string? Source, int? CostFactorId, string? CostFactor, int? CostCenterId, string? CostCenter,
    decimal? EnteredPrice, decimal? UnitCost, decimal? Amount, string? Description, InvoiceLink? Invoice, decimal ReturnedQuantity);

public sealed record StockDocDto(long Id, StockDocType Type, int DocNo, string Date, int WarehouseId, string Warehouse,
    int? PartyId, string? PartyName, string? Description, List<StockLineDto> Lines)
{
    /// <summary>ماژول سازنده سند (مثلاً production) و شناسه فرم آن؛ چنین سندی فقط از همان فرم تغییر می‌کند.</summary>
    public string? SourceModule { get; init; }
    public long? SourceId { get; init; }

    /// <summary>سندی که ردیف فاکتورشده یا برگشت‌خورده دارد یا از ماژول دیگری ساخته شده اصلاح و حذف نمی‌شود.</summary>
    public string? LockReason => SourceModule is not null
        ? "این سند را گزارش تولید ساخته است؛ برای اصلاح یا حذف، همان گزارش تولید را تغییر دهید."
        : Lines.FirstOrDefault(l => l.Invoice is not null) is { } li
        ? $"ردیف {li.RowNo} این سند در {li.Invoice!.Title} {li.Invoice.DocNo:000000} آمده است؛ برای اصلاح یا حذف، اول آن را از فاکتور بردارید."
        : Lines.FirstOrDefault(l => l.ReturnedQuantity > 0) is { } lr
            ? $"ردیف {lr.RowNo} این سند برگشت خورده است؛ برای اصلاح یا حذف، اول سند برگشت را حذف کنید."
            : null;
}

/// <summary>ردیف مبدأ قابل برگشت (حواله فروش برای رسید برگشت از فروش، رسید خرید برای حواله برگشت از خرید).</summary>
public sealed record ReturnSourceDto(long LineId, long DocId, int DocNo, string Date, int WarehouseId, string Warehouse, short RowNo,
    int ItemId, string ItemCode, string ItemTitle, string UnitTitle, decimal Quantity, decimal Returned, decimal Remaining, InvoiceLink Invoice);

/// <param name="UnitCost">بها پس از محاسبه ماهانه؛ null = محاسبه‌نشده.</param>
/// <param name="BalanceAmount">مانده ریالی؛ null اگر ردیفی تا اینجا محاسبه‌نشده باشد.</param>
public sealed record KardexRowDto(string Date, StockDocType Type, long DocId, int DocNo, string Warehouse, string? PartyName,
    string? Description, decimal In, decimal Out, decimal Balance, decimal? UnitCost = null, decimal? Amount = null, decimal? BalanceAmount = null);

public sealed record KardexDto(int ItemId, string ItemCode, string ItemTitle, string UnitTitle, decimal Opening, List<KardexRowDto> Rows,
    decimal TotalIn, decimal TotalOut, decimal Closing, decimal? OpeningAmount = null, decimal? ClosingAmount = null);

public sealed record StockBalanceDto(int ItemId, string ItemCode, string ItemTitle, string GroupTitle, string UnitTitle,
    int WarehouseId, string Warehouse, decimal Quantity);

/// <param name="SalesAccount">«کد - عنوان» معین فروش.</param>
public sealed record ItemGroupDto(int Id, int? ParentId, string Title, ItemKind Kind,
    int SalesAccountId, string SalesAccount, int SalesReturnAccountId, string SalesReturnAccount,
    int? PurchaseAccountId, string? PurchaseAccount, bool IsActive, int ItemCount, int? CostAccountId = null, string? CostAccount = null);

public sealed record SaveItemGroupRequest(string Title, ItemKind Kind, int SalesAccountId, int SalesReturnAccountId,
    int? PurchaseAccountId, int? ParentId, bool IsActive = true, int? CostAccountId = null);

public sealed record ItemDto(int Id, string Code, string Title, ItemKind Kind, int GroupId, string GroupTitle,
    short UnitId, string UnitTitle, string? TaxItemId, bool VatExempt, string? Barcode, decimal? SalesPrice, bool IsActive, string? Notes,
    byte? CategoryId = null, string? CategoryTitle = null);

/// <param name="Code">خالی = کد بعدی خودکار.</param>
public sealed record SaveItemRequest(string? Code, string Title, int GroupId, short UnitId, string? TaxItemId = null,
    bool VatExempt = false, string? Barcode = null, decimal? SalesPrice = null, bool IsActive = true, string? Notes = null,
    byte? CategoryId = null);

public sealed record ItemUnitDto(short Id, string Title);

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        services.AddSingleton(InventoryPermissions.List);
        services.AddScoped<ItemGroupService>();
        services.AddScoped<ItemService>();
        services.AddScoped<ItemCategoryService>();
        services.AddScoped<IProductionStock, ProductionStock>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<StockDocService>();
        services.AddScoped<StockReportService>();
        services.AddScoped<StockCostingService>();
        return services;
    }
}

public sealed record ItemCategoryDto(byte Id, string Title, int? InventoryAccountId, string? InventoryAccount, bool CanPurchase, bool CanSell,
    bool CanConsume, bool CanProductionInput, bool CanProductionOutput, bool IsScrap, int ItemCount);

public sealed record SaveItemCategoryRequest(int? InventoryAccountId, bool CanPurchase, bool CanSell, bool CanConsume, bool CanProductionInput,
    bool CanProductionOutput);
