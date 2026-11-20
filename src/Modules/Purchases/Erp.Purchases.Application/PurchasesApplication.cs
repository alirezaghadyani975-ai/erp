using Erp.Accounting.Contracts;
using Erp.Inventory.Contracts;
using Erp.Purchases.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Purchases.Application;

public interface IPurchasesDbContext
{
    IQueryable<PurchaseInvoice> PurchaseInvoices { get; }
    IQueryable<PurchaseReturn> PurchaseReturns { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص.</summary>
    IQueryable<PartyRef> Parties { get; }

    /// <summary>فقط‌خواندنی از کالا: کالا با معین‌های گروهش.</summary>
    IQueryable<ItemRef> Items { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class PartyRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class ItemRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public ItemKind Kind { get; init; }
    public bool IsActive { get; init; }
    public bool VatExempt { get; init; }
    public string UnitTitle { get; init; } = "";
    public int? PurchaseAccountId { get; init; }
}

public static class PurchasePermissions
{
    public const string InvoiceView = "pur.invoice.view";
    public const string InvoiceCreate = "pur.invoice.create";
    public const string InvoiceEdit = "pur.invoice.edit";
    public const string InvoiceDelete = "pur.invoice.delete";
    public const string ReturnView = "pur.return.view";
    public const string ReturnCreate = "pur.return.create";
    public const string ReturnEdit = "pur.return.edit";
    public const string ReturnDelete = "pur.return.delete";

    public static IPermissionSource List { get; } = new PermissionList("خرید")
        .Crud("pur.invoice", "فاکتور خرید")
        .Crud("pur.return", "برگشت از خرید");
}

/// <summary>سند حسابداری فرم؛ وضعیت: 1 پیش‌نویس، 2 موقت، 3 دائم.</summary>
public sealed record PurchaseVoucherDto(long VoucherId, int TempNo, int? VoucherNo, byte Status)
{
    public static PurchaseVoucherDto? Of(PostedVoucher? v) => v is null ? null : new(v.VoucherId, v.TempNo, v.VoucherNo, v.Status);
}

public sealed record PurchaseLineRequest(int ItemId, decimal Quantity, decimal UnitPrice, decimal Discount = 0, decimal VatAmount = 0,
    string? Description = null);

/// <summary>تاریخ‌ها شمسی، مثل 1405/07/21.</summary>
public sealed record SavePurchaseInvoiceRequest(string Date, int SupplierId, PayableKind PayableKind, string? InvoiceNo,
    string? InvoiceDate, string? SellerTaxId, string? Description, List<PurchaseLineRequest> Lines);

public sealed record PurchaseInvoiceListItemDto(long Id, int DocNo, string Date, int SupplierId, string SupplierName, string? InvoiceNo,
    decimal NetAmount, decimal DiscountAmount, decimal VatAmount, decimal Total, string? Description, PurchaseVoucherDto? Voucher);

public sealed record PurchaseLineDto(long Id, short RowNo, int ItemId, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal UnitPrice, decimal Discount, decimal Amount, decimal VatAmount, string? Description, decimal ReturnedQuantity);

public sealed record PurchaseInvoiceDto(long Id, int DocNo, string Date, int SupplierId, string SupplierName, PayableKind PayableKind,
    string? InvoiceNo, string? InvoiceDate, string? SellerTaxId, string? Description,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal VatAmount, decimal Total,
    List<PurchaseLineDto> Lines, PurchaseVoucherDto? Voucher, int ReturnCount);

public sealed record ReturnLineRequest(long InvoiceLineId, decimal Quantity);

public sealed record SavePurchaseReturnRequest(string Date, long InvoiceId, string? Description, List<ReturnLineRequest> Lines);

public sealed record PurchaseReturnListItemDto(long Id, int DocNo, string Date, long InvoiceId, int InvoiceDocNo, string SupplierName,
    decimal Amount, decimal VatAmount, decimal Total, string? Description, PurchaseVoucherDto? Voucher);

public sealed record PurchaseReturnLineDto(long InvoiceLineId, short RowNo, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal Amount, decimal VatAmount);

public sealed record PurchaseReturnDto(long Id, int DocNo, string Date, long InvoiceId, int InvoiceDocNo, string InvoiceDate,
    int SupplierId, string SupplierName, string? Description, decimal Amount, decimal VatAmount, decimal Total,
    List<PurchaseReturnLineDto> Lines, PurchaseVoucherDto? Voucher);

/// <summary>ردیف فاکتور با مانده قابل برگشت (مقدار برگشت‌شده در برگشت‌های دیگر کم شده است).</summary>
public sealed record ReturnableLineDto(long InvoiceLineId, short RowNo, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal UnitPrice, decimal Amount, decimal VatAmount, decimal ReturnedQuantity, decimal Remaining);

public static class DependencyInjection
{
    public static IServiceCollection AddPurchasesApplication(this IServiceCollection services)
    {
        services.AddSingleton(PurchasePermissions.List);
        services.AddScoped<PurchaseInvoiceService>();
        services.AddScoped<PurchaseReturnService>();
        return services;
    }
}
