using Erp.Inventory.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Sales.Domain;

/// <summary>نوع صورتحساب در سامانه مودیان.</summary>
public enum TaxInvoiceType : byte
{
    /// <summary>نوع اول: با مشخصات خریدار.</summary>
    WithBuyer = 1,
    /// <summary>نوع دوم: بدون مشخصات کامل خریدار (مصرف‌کننده نهایی).</summary>
    WithoutBuyer = 2
}

/// <summary>روش تسویه برای سامانه مودیان. دریافت وجه همیشه از خزانه‌داری ثبت می‌شود.</summary>
public enum SettlementMethod : byte
{
    Cash = 1,
    Credit = 2,
    Mixed = 3
}

/// <summary>وضعیت ارسال به سامانه مودیان (ارسال در مرحله بعد ساخته می‌شود).</summary>
public enum TaxStatus : byte
{
    NotSent = 0
}

/// <param name="IssueLineId">کالا: ردیف حواله فروش؛ خدمت: خالی.</param>
public sealed record SalesLineInput(int ItemId, decimal Quantity, decimal UnitPrice, decimal Discount, decimal VatAmount, string? Description,
    long? IssueLineId = null);

/// <summary>
/// فاکتور فروش (sls.SalesInvoices). سند: بدهکار مشتری (دریافتنی تجاری) و تخفیف فروش، بستانکار معین فروش گروه هر کالا
/// (مبلغ ناخالص) و ارزش افزوده پرداختنی. با ثبت، اصلاح و حذف آن سند حسابداری‌اش خودکار صادر، اصلاح و حذف می‌شود.
/// </summary>
public class SalesInvoice : AggregateRoot<long>
{
    public const string Module = "sales";
    public const string DocType = "sls_invoice";

    private readonly List<SalesInvoiceLine> _lines = [];

    private SalesInvoice() { } // EF

    public SalesInvoice(int docNo, int createdBy, DateTime nowUtc)
    {
        if (docNo <= 0) throw new DomainException("شماره فرم نامعتبر است.");
        DocNo = docNo;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
    }

    public int DocNo { get; private set; }
    public DateOnly DocDate { get; private set; }
    public int CustomerId { get; private set; }
    public TaxInvoiceType InvoiceType { get; private set; } = TaxInvoiceType.WithBuyer;
    public SettlementMethod SettlementMethod { get; private set; } = SettlementMethod.Credit;
    public decimal? CashAmount { get; private set; }
    public int? ProjectDetailId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal VatAmount { get; private set; }
    public TaxStatus TaxStatus { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<SalesInvoiceLine> Lines => _lines.AsReadOnly();

    public decimal NetAmount => GrossAmount - DiscountAmount;
    /// <summary>جمع کل: طلب از مشتری.</summary>
    public decimal Total => NetAmount + VatAmount;

    public void SetHeader(DateOnly date, int customerId, TaxInvoiceType type, SettlementMethod settlement, int? projectDetailId,
        string? description)
    {
        if (customerId <= 0) throw new DomainException("مشتری الزامی است.");
        if (!Enum.IsDefined(type)) throw new DomainException("نوع صورتحساب معتبر نیست.");
        if (!Enum.IsDefined(settlement)) throw new DomainException("روش تسویه معتبر نیست.");
        DocDate = date;
        CustomerId = customerId;
        InvoiceType = type;
        SettlementMethod = settlement;
        ProjectDetailId = projectDetailId;
        Description = Lines3.Optional(description, "شرح", 500);
    }

    /// <summary>جایگزینی کامل ردیف‌ها؛ مبلغ = مقدار × فی − تخفیف (به ریال). سهم نقد بعد از ردیف‌ها کنترل می‌شود.</summary>
    public void SetLines(IEnumerable<SalesLineInput> lines, decimal? cashAmount)
    {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("حداقل یک ردیف کالا لازم است.");
        if (list.Count > 500) throw new DomainException("تعداد ردیف‌ها بیش از حد مجاز است.");
        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.ItemId <= 0) throw new DomainException($"ردیف {row}: کالا الزامی است.");
            if (l.Quantity <= 0) throw new DomainException($"ردیف {row}: مقدار باید بزرگتر از صفر باشد.");
            if (l.UnitPrice <= 0) throw new DomainException($"ردیف {row}: فی باید بزرگتر از صفر باشد.");
            if (l.Discount < 0) throw new DomainException($"ردیف {row}: تخفیف نمی‌تواند منفی باشد.");
            if (l.VatAmount < 0) throw new DomainException($"ردیف {row}: ارزش افزوده نمی‌تواند منفی باشد.");
            var gross = TradeMath.Gross(l.Quantity, l.UnitPrice);
            var discount = TradeMath.Rial(l.Discount);
            if (discount > gross) throw new DomainException($"ردیف {row}: تخفیف از مبلغ ردیف بیشتر است.");
            _lines.Add(new SalesInvoiceLine(row, l.ItemId, Math.Round(l.Quantity, 6), Math.Round(l.UnitPrice, 4), discount, gross - discount,
                TradeMath.Rial(l.VatAmount), Lines3.Optional(l.Description, $"شرح ردیف {row}", 500), l.IssueLineId));
        }
        GrossAmount = _lines.Sum(l => l.Amount + l.Discount);
        DiscountAmount = _lines.Sum(l => l.Discount);
        VatAmount = _lines.Sum(l => l.VatAmount);

        if (SettlementMethod == SettlementMethod.Mixed)
        {
            var cash = TradeMath.Rial(cashAmount ?? 0);
            if (cash <= 0 || cash >= Total)
                throw new DomainException("در روش «نقد و نسیه» مبلغ نقد باید بیشتر از صفر و کمتر از جمع فاکتور باشد.");
            CashAmount = cash;
        }
        else CashAmount = null;
    }

    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>ردیف فاکتور فروش (sls.SalesInvoiceLines).</summary>
public class SalesInvoiceLine
{
    private SalesInvoiceLine() { } // EF

    internal SalesInvoiceLine(short rowNo, int itemId, decimal quantity, decimal unitPrice, decimal discount, decimal amount,
        decimal vatAmount, string? description, long? issueLineId)
    {
        RowNo = rowNo;
        ItemId = itemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Discount = discount;
        Amount = amount;
        VatAmount = vatAmount;
        Description = description;
        IssueLineId = issueLineId;
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

    /// <summary>ردیف حواله فروش (inv.StockDocLines)؛ خدمت ندارد.</summary>
    public long? IssueLineId { get; private set; }

    public decimal GrossAmount => Amount + Discount;
}

/// <summary>ردیف محاسبه‌شده برگشت از فروش (سهم‌ها را سرویس از ردیف فاکتور حساب می‌کند).</summary>
/// <param name="ReceiptLineId">کالا: ردیف رسید برگشت از فروش؛ خدمت: خالی.</param>
public sealed record SalesReturnLineInput(long InvoiceLineId, decimal Quantity, decimal GrossAmount, decimal Discount, decimal VatAmount,
    long? ReceiptLineId = null);

/// <summary>
/// برگشت از فروش (sls.SalesReturns) با ارجاع به فاکتور فروش؛ در سامانه مودیان صورتحساب «برگشت از فروش» با ارجاع به
/// شماره مالیاتی فاکتور اصلی. سند: بدهکار معین برگشت گروه کالا و ارزش افزوده، بستانکار تخفیف و مشتری.
/// </summary>
public class SalesReturn : AggregateRoot<long>
{
    public const string DocType = "sls_return";

    private readonly List<SalesReturnLine> _lines = [];

    private SalesReturn() { } // EF

    public SalesReturn(int docNo, long invoiceId, int createdBy, DateTime nowUtc)
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
    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal VatAmount { get; private set; }
    public TaxStatus TaxStatus { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<SalesReturnLine> Lines => _lines.AsReadOnly();
    public decimal NetAmount => GrossAmount - DiscountAmount;
    public decimal Total => NetAmount + VatAmount;

    public void SetHeader(DateOnly date, DateOnly invoiceDate, string? description)
    {
        if (date < invoiceDate) throw new DomainException("تاریخ برگشت پیش از تاریخ فاکتور فروش است.");
        DocDate = date;
        Description = Lines3.Optional(description, "شرح", 500);
    }

    public void SetLines(IEnumerable<SalesReturnLineInput> lines)
    {
        var list = lines.Where(l => l.Quantity != 0).ToList();
        if (list.Count == 0) throw new DomainException("مقدار برگشتی حداقل یک ردیف را وارد کنید.");
        if (list.Select(l => (l.InvoiceLineId, l.ReceiptLineId)).Distinct().Count() != list.Count) throw new DomainException("یک ردیف دو بار آمده است.");
        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.Quantity < 0) throw new DomainException($"ردیف {row}: مقدار برگشتی نمی‌تواند منفی باشد.");
            _lines.Add(new SalesReturnLine(row, l.InvoiceLineId, Math.Round(l.Quantity, 6), l.GrossAmount, l.Discount,
                l.GrossAmount - l.Discount, l.VatAmount, l.ReceiptLineId));
        }
        GrossAmount = _lines.Sum(l => l.GrossAmount);
        DiscountAmount = _lines.Sum(l => l.Discount);
        VatAmount = _lines.Sum(l => l.VatAmount);
    }

    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>ردیف برگشت از فروش (sls.SalesReturnLines).</summary>
public class SalesReturnLine
{
    private SalesReturnLine() { } // EF

    internal SalesReturnLine(short rowNo, long invoiceLineId, decimal quantity, decimal grossAmount, decimal discount, decimal amount,
        decimal vatAmount, long? receiptLineId)
    {
        RowNo = rowNo;
        InvoiceLineId = invoiceLineId;
        Quantity = quantity;
        GrossAmount = grossAmount;
        Discount = discount;
        Amount = amount;
        VatAmount = vatAmount;
        ReceiptLineId = receiptLineId;
    }

    public long Id { get; private set; }
    public long ReturnId { get; private set; }
    public short RowNo { get; private set; }
    public long InvoiceLineId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }
    /// <summary>ردیف رسید برگشت از فروش (inv.StockDocLines)؛ خدمت ندارد.</summary>
    public long? ReceiptLineId { get; private set; }
}

internal static class Lines3
{
    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}
