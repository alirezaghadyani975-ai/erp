using Erp.Accounting.Contracts;
using Erp.Inventory.Contracts;
using Erp.Sales.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Sales.Application;

public interface ISalesDbContext
{
    IQueryable<SalesInvoice> SalesInvoices { get; }
    IQueryable<SalesReturn> SalesReturns { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص.</summary>
    IQueryable<CustomerRef> Customers { get; }

    /// <summary>فقط‌خواندنی از کالا: کالا با معین‌های فروش و برگشت گروهش.</summary>
    IQueryable<SalesItemRef> Items { get; }

    /// <summary>فقط‌خواندنی از حسابداری: عنوان حساب تفصیلی (پروژه).</summary>
    IQueryable<DetailRef> Details { get; }

    /// <summary>فقط‌خواندنی از انبار: ردیف‌های رسید و حواله با سربرگشان.</summary>
    IQueryable<StockLineRef> StockLines { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class CustomerRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class SalesItemRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public ItemKind Kind { get; init; }
    public bool IsActive { get; init; }
    public bool VatExempt { get; init; }
    public string UnitTitle { get; init; } = "";
    public int SalesAccountId { get; init; }
    public int SalesReturnAccountId { get; init; }
    public decimal? SalesPrice { get; init; }
}

public sealed class DetailRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public static class SalesPermissions
{
    public const string InvoiceView = "sls.invoice.view";
    public const string InvoiceCreate = "sls.invoice.create";
    public const string InvoiceEdit = "sls.invoice.edit";
    public const string InvoiceDelete = "sls.invoice.delete";
    public const string ReturnView = "sls.return.view";
    public const string ReturnCreate = "sls.return.create";
    public const string ReturnEdit = "sls.return.edit";
    public const string ReturnDelete = "sls.return.delete";
    public const string ReportView = "sls.report.view";

    public static IPermissionSource List { get; } = new PermissionList("فروش")
        .Crud("sls.invoice", "فاکتور فروش")
        .Crud("sls.return", "برگشت از فروش")
        .Add("sls.report", "گزارش‌های فروش", ("view", "مشاهده"));
}

public sealed record SalesVoucherDto(long VoucherId, int TempNo, int? VoucherNo, byte Status)
{
    public static SalesVoucherDto? Of(PostedVoucher? v) => v is null ? null : new(v.VoucherId, v.TempNo, v.VoucherNo, v.Status);
}

/// <param name="IssueLineId">کالا: ردیف حواله فروش (کالا و مقدار باید همان حواله باشد)؛ خدمت: خالی.</param>
public sealed record SalesLineRequest(int ItemId, decimal Quantity, decimal UnitPrice, decimal Discount = 0, decimal VatAmount = 0,
    string? Description = null, long? IssueLineId = null);

/// <param name="ProjectDetailId">تفصیلی پروژه (برای معین‌هایی مثل درآمد خدمات که پروژه می‌خواهند).</param>
public sealed record SaveSalesInvoiceRequest(string Date, int CustomerId, TaxInvoiceType InvoiceType, SettlementMethod Settlement,
    decimal? CashAmount, int? ProjectDetailId, string? Description, List<SalesLineRequest> Lines);

public sealed record SalesInvoiceListItemDto(long Id, int DocNo, string Date, int CustomerId, string CustomerName,
    decimal GrossAmount, decimal DiscountAmount, decimal VatAmount, decimal Total, SettlementMethod Settlement, string? Description,
    SalesVoucherDto? Voucher);

public sealed record SalesLineDto(long Id, short RowNo, int ItemId, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal UnitPrice, decimal Discount, decimal Amount, decimal VatAmount, string? Description, decimal ReturnedQuantity,
    long? IssueLineId, string? Issue);

public sealed record SalesInvoiceDto(long Id, int DocNo, string Date, int CustomerId, string CustomerName, TaxInvoiceType InvoiceType,
    SettlementMethod Settlement, decimal? CashAmount, int? ProjectDetailId, string? ProjectTitle, string? Description,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal VatAmount, decimal Total, TaxStatus TaxStatus,
    List<SalesLineDto> Lines, SalesVoucherDto? Voucher, int ReturnCount);

/// <param name="ReceiptLineId">کالا: ردیف رسید برگشت از فروش (مقدار باید همان رسید باشد)؛ خدمت: خالی.</param>
public sealed record SalesReturnLineRequest(long InvoiceLineId, decimal Quantity, long? ReceiptLineId = null);

public sealed record SaveSalesReturnRequest(string Date, long InvoiceId, string? Description, List<SalesReturnLineRequest> Lines);

public sealed record SalesReturnListItemDto(long Id, int DocNo, string Date, long InvoiceId, int InvoiceDocNo, string CustomerName,
    decimal NetAmount, decimal VatAmount, decimal Total, string? Description, SalesVoucherDto? Voucher);

public sealed record SalesReturnLineDto(long InvoiceLineId, short RowNo, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal GrossAmount, decimal Discount, decimal Amount, decimal VatAmount, long? ReceiptLineId, string? Receipt);

public sealed record SalesReturnDto(long Id, int DocNo, string Date, long InvoiceId, int InvoiceDocNo, string InvoiceDate,
    int CustomerId, string CustomerName, string? Description, decimal GrossAmount, decimal DiscountAmount, decimal VatAmount, decimal Total,
    List<SalesReturnLineDto> Lines, SalesVoucherDto? Voucher);

public sealed record SalesReturnableLineDto(long InvoiceLineId, short RowNo, string ItemCode, string ItemTitle, string UnitTitle,
    decimal Quantity, decimal UnitPrice, decimal Amount, decimal VatAmount, decimal ReturnedQuantity, decimal Remaining, long? IssueLineId = null);

public static class DependencyInjection
{
    public static IServiceCollection AddSalesApplication(this IServiceCollection services)
    {
        services.AddSingleton(SalesPermissions.List);
        services.AddScoped<SalesInvoiceService>();
        services.AddScoped<SalesReturnService>();
        services.AddScoped<SalesReportService>();
        return services;
    }
}
