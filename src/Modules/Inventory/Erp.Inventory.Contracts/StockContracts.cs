namespace Erp.Inventory.Contracts;

/// <summary>نوع سند انبار (inv.StockDocs.DocType). رسیدها زیر ۱۰، حواله‌ها از ۱۱.</summary>
public enum StockDocType : byte
{
    /// <summary>موجودی اول دوره.</summary>
    Opening = 1,
    /// <summary>رسید خرید (قبل از فاکتور خرید).</summary>
    PurchaseReceipt = 2,
    /// <summary>رسید برگشت از فروش (از ردیف حواله فروش).</summary>
    SalesReturnReceipt = 3,
    /// <summary>حواله فروش (قبل از فاکتور فروش).</summary>
    SalesIssue = 11,
    /// <summary>حواله برگشت از خرید (از ردیف رسید خرید).</summary>
    PurchaseReturnIssue = 12,
    /// <summary>حواله مصرف غیرتولیدی (با عامل و مرکز هزینه).</summary>
    Consumption = 13
}

/// <summary>
/// نگاشت فقط‌خواندنی ردیف سند انبار با سربرگش برای ماژول‌های خرید و فروش (ToSqlQuery روی inv.StockDocLines).
/// </summary>
public sealed class StockLineRef
{
    public const string Sql = """
        SELECT l.Id, l.DocId, d.DocType, d.DocNo, d.DocDate, d.WarehouseId, w.Title AS WarehouseTitle, d.PartyId,
               l.RowNo, l.ItemId, l.Quantity, l.SourceLineId, l.EnteredPrice
        FROM inv.StockDocLines l JOIN inv.StockDocs d ON d.Id = l.DocId JOIN inv.Warehouses w ON w.Id = d.WarehouseId
        """;

    public long Id { get; init; }
    public long DocId { get; init; }
    public StockDocType DocType { get; init; }
    public int DocNo { get; init; }
    public DateOnly DocDate { get; init; }
    public int WarehouseId { get; init; }
    public string WarehouseTitle { get; init; } = "";
    public int? PartyId { get; init; }
    public short RowNo { get; init; }
    public int ItemId { get; init; }
    public decimal Quantity { get; init; }
    public long? SourceLineId { get; init; }
    public decimal? EnteredPrice { get; init; }

    /// <summary>مثل «رسید خرید 000003 ردیف 2».</summary>
    public string Title => $"{DocType.Title()} {DocNo:000000} ردیف {RowNo}";
}

/// <summary>ردیف سند انباری که هنوز فاکتور نشده (برای انتخاب در فاکتور یا برگشت).</summary>
/// <param name="InvoiceLineId">برگشت: ردیف فاکتوری که این برگشت انباری به آن مربوط است.</param>
/// <param name="Price">رسید خرید: فی تقریبی؛ حواله فروش: قیمت فروش کالا؛ برگشت: فی ردیف فاکتور.</param>
public sealed record PendingStockLineDto(long LineId, long DocId, StockDocType DocType, int DocNo, string Date, string Warehouse, short RowNo,
    int ItemId, string ItemCode, string ItemTitle, string UnitTitle, decimal Quantity, decimal? Price, long? InvoiceLineId = null)
{
    public string Title => $"{DocType.Title()} {DocNo:000000} ردیف {RowNo}";
}

public static class StockDocTypes
{
    public static bool IsReceipt(this StockDocType type) => (byte)type < 10;

    /// <summary>طرف حساب (تأمین‌کننده یا مشتری) دارد.</summary>
    public static bool HasParty(this StockDocType type) =>
        type is StockDocType.PurchaseReceipt or StockDocType.SalesReturnReceipt or StockDocType.SalesIssue or StockDocType.PurchaseReturnIssue;

    /// <summary>برگشت است و به ردیف سند مبدأ ارجاع دارد.</summary>
    public static bool IsReturn(this StockDocType type) =>
        type is StockDocType.SalesReturnReceipt or StockDocType.PurchaseReturnIssue;

    /// <summary>نوع سند مبدأ برگشت.</summary>
    public static StockDocType? SourceType(this StockDocType type) => type switch
    {
        StockDocType.SalesReturnReceipt => StockDocType.SalesIssue,
        StockDocType.PurchaseReturnIssue => StockDocType.PurchaseReceipt,
        _ => null
    };

    public static string Title(this StockDocType type) => type switch
    {
        StockDocType.Opening => "موجودی اول دوره",
        StockDocType.PurchaseReceipt => "رسید خرید",
        StockDocType.SalesReturnReceipt => "رسید برگشت از فروش",
        StockDocType.SalesIssue => "حواله فروش",
        StockDocType.PurchaseReturnIssue => "حواله برگشت از خرید",
        StockDocType.Consumption => "حواله مصرف",
        _ => "سند انبار"
    };

    /// <summary>عنوان طرف حساب در فرم.</summary>
    public static string PartyTitle(this StockDocType type) =>
        type is StockDocType.PurchaseReceipt or StockDocType.PurchaseReturnIssue ? "تأمین‌کننده" : "مشتری";
}
