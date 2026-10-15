using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Expenses.Domain;

/// <summary>طرف بستانکار فاکتور: پرداختنی تجاری یا سایر پرداختنی‌ها.</summary>
public enum PayableKind : byte
{
    Trade = 1,
    Other = 2
}

/// <summary>
/// شناسایی هزینه (exp.ExpenseInvoices): فاکتور خدماتی یک تأمین‌کننده که هزینه را شناسایی و بدهی را ثبت می‌کند؛
/// پرداختش بعداً در خزانه‌داری. هر ردیف یک عامل هزینه و مرکز هزینه دارد و معین هزینه از روی همین دو تعیین می‌شود.
/// با ثبت، اصلاح و حذف آن، سند حسابداری‌اش خودکار صادر، اصلاح و حذف می‌شود.
/// </summary>
public class ExpenseInvoice : AggregateRoot<long>
{
    public const string Module = "expenses";
    public const string DocType = "expense_invoice";

    private readonly List<ExpenseInvoiceLine> _lines = [];

    private ExpenseInvoice() { } // EF

    public ExpenseInvoice(int docNo, int createdBy, DateTime nowUtc)
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
    public decimal VatAmount { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<ExpenseInvoiceLine> Lines => _lines.AsReadOnly();

    public decimal LinesTotal => _lines.Sum(l => l.Amount);
    /// <summary>جمع کل: هزینه‌ها + ارزش افزوده ردیف‌ها (VatAmount سربرگ = جمع ارزش افزوده ردیف‌ها).</summary>
    public decimal Total => LinesTotal + VatAmount;

    public void SetHeader(DateOnly date, int supplierId, PayableKind payableKind, string? invoiceNo, DateOnly? invoiceDate,
        string? description)
    {
        if (supplierId <= 0) throw new DomainException("تأمین‌کننده (طرف حساب) الزامی است.");
        if (!Enum.IsDefined(payableKind)) throw new DomainException("طرف بستانکار معتبر نیست.");
        if (invoiceDate is { } d && d > date) throw new DomainException("تاریخ فاکتور فروشنده بعد از تاریخ فرم است.");

        DocDate = date;
        SupplierId = supplierId;
        PayableKind = payableKind;
        InvoiceNo = Optional(invoiceNo, "شماره فاکتور", 50);
        InvoiceDate = invoiceDate;
        Description = Optional(description, "شرح", 500);
    }

    /// <summary>جایگزینی کامل ردیف‌ها؛ هر ردیف ارزش افزوده خودش را دارد (صفر = معاف).</summary>
    public void SetLines(IEnumerable<(int FactorId, int? CenterId, decimal Amount, decimal VatAmount, string? Description)> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("حداقل یک ردیف هزینه لازم است.");
        if (list.Count > 500) throw new DomainException("تعداد ردیف‌ها بیش از حد مجاز است.");

        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.FactorId <= 0) throw new DomainException($"ردیف {row}: عامل هزینه الزامی است.");
            if (l.Amount <= 0) throw new DomainException($"ردیف {row}: مبلغ باید بزرگتر از صفر باشد.");
            if (l.VatAmount < 0) throw new DomainException($"ردیف {row}: ارزش افزوده نمی‌تواند منفی باشد.");
            _lines.Add(new ExpenseInvoiceLine(row, l.FactorId, l.CenterId, Math.Round(l.Amount, 4), Math.Round(l.VatAmount, 4),
                Optional(l.Description, $"شرح ردیف {row}", 500)));
        }
        VatAmount = _lines.Sum(l => l.VatAmount);
    }

    /// <summary>حذف ردیف‌ها پیش از بازنویسی (تا شماره ردیف‌های تازه با قبلی‌ها برخورد نکند).</summary>
    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;

    private static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}

/// <summary>ردیف شناسایی هزینه (exp.ExpenseInvoiceLines).</summary>
public class ExpenseInvoiceLine
{
    private ExpenseInvoiceLine() { } // EF

    internal ExpenseInvoiceLine(short rowNo, int factorId, int? centerId, decimal amount, decimal vatAmount, string? description)
    {
        RowNo = rowNo;
        CostFactorId = factorId;
        CostCenterId = centerId;
        Amount = amount;
        VatAmount = vatAmount;
        Description = description;
    }

    public long Id { get; private set; }
    public long InvoiceId { get; private set; }
    public short RowNo { get; private set; }
    public int CostFactorId { get; private set; }
    public int? CostCenterId { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>مالیات و عوارض ارزش افزوده ردیف؛ صفر یعنی معاف.</summary>
    public decimal VatAmount { get; private set; }
    public string? Description { get; private set; }
}
