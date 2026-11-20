using Erp.Inventory.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Purchases.Domain;

/// <summary>طرف بستانکار فاکتور خرید: پرداختنی تجاری یا سایر پرداختنی‌ها.</summary>
public enum PayableKind : byte
{
    Trade = 1,
    Other = 2
}

/// <summary>ردیف ورودی فاکتور خرید.</summary>
public sealed record PurchaseLineInput(int ItemId, decimal Quantity, decimal UnitPrice, decimal Discount, decimal VatAmount, string? Description);

/// <summary>
/// فاکتور خرید داخلی کالا (pur.PurchaseInvoices). بدهکار معین خرید (موجودی) گروه هر کالا و ارزش افزوده دریافتنی،
/// بستانکار تأمین‌کننده. با ثبت، اصلاح و حذف آن سند حسابداری‌اش خودکار صادر، اصلاح و حذف می‌شود.
/// </summary>
public class PurchaseInvoice : AggregateRoot<long>
{
    public const string Module = "purchases";
    public const string DocType = "pur_invoice";

    private readonly List<PurchaseInvoiceLine> _lines = [];

    private PurchaseInvoice() { } // EF

    public PurchaseInvoice(int docNo, int createdBy, DateTime nowUtc)
    {
        if (docNo <= 0) throw new DomainException("شماره فرم نامعتبر است.");
        DocNo = docNo;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
    }

    public int DocNo { get; private set; }
    public DateOnly DocDate { get; private set; }
    public int SupplierId { get; private set; }
    public PayableKind PayableKind { get; private set; } = PayableKind.Trade;
    public string? InvoiceNo { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }
    public string? SellerTaxId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal VatAmount { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>مبلغ پس از تخفیف (بهای کالا).</summary>
    public decimal NetAmount => GrossAmount - DiscountAmount;
    /// <summary>جمع کل: بدهی به تأمین‌کننده.</summary>
    public decimal Total => NetAmount + VatAmount;

    public void SetHeader(DateOnly date, int supplierId, PayableKind payableKind, string? invoiceNo, DateOnly? invoiceDate,
        string? sellerTaxId, string? description)
    {
        if (supplierId <= 0) throw new DomainException("تأمین‌کننده الزامی است.");
        if (!Enum.IsDefined(payableKind)) throw new DomainException("طرف بستانکار معتبر نیست.");
        if (invoiceDate is { } d && d > date) throw new DomainException("تاریخ فاکتور فروشنده بعد از تاریخ فرم است.");
        var tax = PersianText.NormalizeOrNull(sellerTaxId)?.Replace(" ", "").ToUpperInvariant();
        if (tax is not null && (tax.Length != 22 || !tax.All(char.IsAsciiLetterOrDigit)))
            throw new DomainException("شماره مالیاتی صورتحساب فروشنده باید ۲۲ نویسه (رقم و حرف لاتین) باشد.");

        DocDate = date;
        SupplierId = supplierId;
        PayableKind = payableKind;
        InvoiceNo = Lines2.Optional(invoiceNo, "شماره فاکتور", 50);
        InvoiceDate = invoiceDate;
        SellerTaxId = tax;
        Description = Lines2.Optional(description, "شرح", 500);
    }

    /// <summary>جایگزینی کامل ردیف‌ها؛ مبلغ = مقدار × فی − تخفیف (به ریال).</summary>
    public void SetLines(IEnumerable<PurchaseLineInput> lines)
    {
        var list = lines.ToList();
        Lines2.CheckCount(list.Count, "کالا");
        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            var (gross, discount) = Lines2.Amounts(row, l.ItemId, l.Quantity, l.UnitPrice, l.Discount, l.VatAmount);
            _lines.Add(new PurchaseInvoiceLine(row, l.ItemId, Math.Round(l.Quantity, 6), Math.Round(l.UnitPrice, 4), discount,
                gross - discount, TradeMath.Rial(l.VatAmount), Lines2.Optional(l.Description, $"شرح ردیف {row}", 500)));
        }
        GrossAmount = _lines.Sum(l => l.Amount + l.Discount);
        DiscountAmount = _lines.Sum(l => l.Discount);
        VatAmount = _lines.Sum(l => l.VatAmount);
    }

    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>ردیف فاکتور خرید (pur.PurchaseInvoiceLines).</summary>
public class PurchaseInvoiceLine
{
    private PurchaseInvoiceLine() { } // EF

    internal PurchaseInvoiceLine(short rowNo, int itemId, decimal quantity, decimal unitPrice, decimal discount, decimal amount,
        decimal vatAmount, string? description)
    {
        RowNo = rowNo;
        ItemId = itemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Discount = discount;
        Amount = amount;
        VatAmount = vatAmount;
        Description = description;
    }

    public long Id { get; private set; }
    public long InvoiceId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Discount { get; private set; }
    /// <summary>مبلغ پس از تخفیف.</summary>
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }
    public string? Description { get; private set; }
}

/// <summary>ردیف محاسبه‌شده برگشت (سهم مبلغ و ارزش افزوده را سرویس از ردیف فاکتور حساب می‌کند).</summary>
public sealed record PurchaseReturnLineInput(long InvoiceLineId, decimal Quantity, decimal Amount, decimal VatAmount);

/// <summary>
/// برگشت از خرید (pur.PurchaseReturns): همیشه با ارجاع به فاکتور خرید؛ مقدار برگشتی هر ردیف از مانده ردیف فاکتور
/// بیشتر نمی‌شود. سند: بدهکار تأمین‌کننده، بستانکار معین خرید گروه کالا و ارزش افزوده دریافتنی.
/// </summary>
public class PurchaseReturn : AggregateRoot<long>
{
    public const string DocType = "pur_return";

    private readonly List<PurchaseReturnLine> _lines = [];

    private PurchaseReturn() { } // EF

    public PurchaseReturn(int docNo, long invoiceId, int createdBy, DateTime nowUtc)
    {
        if (docNo <= 0) throw new DomainException("شماره فرم نامعتبر است.");
        DocNo = docNo;
        InvoiceId = invoiceId;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
    }

    public int DocNo { get; private set; }
    public DateOnly DocDate { get; private set; }
    public long InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<PurchaseReturnLine> Lines => _lines.AsReadOnly();
    public decimal Total => Amount + VatAmount;

    public void SetHeader(DateOnly date, DateOnly invoiceDate, string? description)
    {
        if (date < invoiceDate) throw new DomainException("تاریخ برگشت پیش از تاریخ فاکتور خرید است.");
        DocDate = date;
        Description = Lines2.Optional(description, "شرح", 500);
    }

    public void SetLines(IEnumerable<PurchaseReturnLineInput> lines)
    {
        var list = lines.Where(l => l.Quantity != 0).ToList();
        if (list.Count == 0) throw new DomainException("مقدار برگشتی حداقل یک ردیف را وارد کنید.");
        if (list.Select(l => l.InvoiceLineId).Distinct().Count() != list.Count) throw new DomainException("یک ردیف فاکتور دو بار آمده است.");
        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.Quantity < 0) throw new DomainException($"ردیف {row}: مقدار برگشتی نمی‌تواند منفی باشد.");
            _lines.Add(new PurchaseReturnLine(row, l.InvoiceLineId, Math.Round(l.Quantity, 6), l.Amount, l.VatAmount));
        }
        Amount = _lines.Sum(l => l.Amount);
        VatAmount = _lines.Sum(l => l.VatAmount);
    }

    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>ردیف برگشت از خرید (pur.PurchaseReturnLines).</summary>
public class PurchaseReturnLine
{
    private PurchaseReturnLine() { } // EF

    internal PurchaseReturnLine(short rowNo, long invoiceLineId, decimal quantity, decimal amount, decimal vatAmount)
    {
        RowNo = rowNo;
        InvoiceLineId = invoiceLineId;
        Quantity = quantity;
        Amount = amount;
        VatAmount = vatAmount;
    }

    public long Id { get; private set; }
    public long ReturnId { get; private set; }
    public short RowNo { get; private set; }
    public long InvoiceLineId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }
}

/// <summary>کنترل‌های مشترک ردیف‌های فاکتور.</summary>
internal static class Lines2
{
    public static void CheckCount(int count, string what)
    {
        if (count == 0) throw new DomainException($"حداقل یک ردیف {what} لازم است.");
        if (count > 500) throw new DomainException("تعداد ردیف‌ها بیش از حد مجاز است.");
    }

    /// <summary>کنترل ردیف و برگرداندن (ناخالص، تخفیف) به ریال.</summary>
    public static (decimal Gross, decimal Discount) Amounts(short row, int itemId, decimal quantity, decimal unitPrice, decimal discount, decimal vat)
    {
        if (itemId <= 0) throw new DomainException($"ردیف {row}: کالا الزامی است.");
        if (quantity <= 0) throw new DomainException($"ردیف {row}: مقدار باید بزرگتر از صفر باشد.");
        if (unitPrice <= 0) throw new DomainException($"ردیف {row}: فی باید بزرگتر از صفر باشد.");
        if (discount < 0) throw new DomainException($"ردیف {row}: تخفیف نمی‌تواند منفی باشد.");
        if (vat < 0) throw new DomainException($"ردیف {row}: ارزش افزوده نمی‌تواند منفی باشد.");
        var gross = TradeMath.Gross(quantity, unitPrice);
        var d = TradeMath.Rial(discount);
        if (d > gross) throw new DomainException($"ردیف {row}: تخفیف از مبلغ ردیف بیشتر است.");
        return (gross, d);
    }

    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}
