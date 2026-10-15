using Erp.SharedKernel.Domain;

namespace Erp.Treasury.Domain;

/// <summary>ابزار دریافت یا پرداخت در هر ردیف.</summary>
public enum Instrument : byte
{
    Cash = 1,          // نقد (صندوق)
    Bank = 2,          // بانکی: واریز، کارت‌خوان، انتقال
    Cheque = 3,        // چک (دریافتی یا صادره)
    SpendCheque = 4    // خرج چک دریافتی (فقط در پرداخت)
}

/// <summary>وضعیت چک دریافتی.</summary>
public enum ReceivedChequeStatus : byte
{
    InBox = 1,         // در صندوق
    InCollection = 2,  // در جریان وصول (واگذارشده به بانک)
    Collected = 3,     // وصول‌شده
    Bounced = 4,       // برگشتی
    Legal = 5,         // حقوقی (اقدام قانونی)
    Returned = 6,      // عودت به صاحب چک
    Spent = 7          // خرج‌شده (پرداخت به دیگری)
}

/// <summary>وضعیت چک صادره.</summary>
public enum IssuedChequeStatus : byte
{
    Issued = 1,     // صادره، پاس‌نشده
    Cleared = 2,    // پاس‌شده
    Cancelled = 3   // باطل / عودت به ما
}

/// <summary>مشخصات چک در فرم دریافت یا پرداخت.</summary>
public sealed record ChequeInfo(string SerialNo, string? SayadId, DateOnly DueDate, short? BankId = null, string? Branch = null,
    string? DrawerName = null, string? AccountNo = null);

// ----------------------------------------------------------------------
// دریافت
// ----------------------------------------------------------------------

/// <summary>
/// دریافت (trs.Receipts): از یک شخص با یک نوع دریافت (معین طرف مقابل)، با چند ردیف نقد، بانکی یا چک.
/// چک‌های دریافتی همین‌جا ساخته می‌شوند و در صندوق ردیف نگهداری می‌شوند.
/// </summary>
public class Receipt : TreasuryDocument
{
    public const string DocType = "trs_receipt";

    private readonly List<ReceiptLine> _lines = [];

    private Receipt() { } // EF

    public Receipt(int docNo, int createdBy, DateTime nowUtc) => Init(docNo, createdBy, nowUtc);

    public int PersonId { get; private set; }
    public short TypeId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<ReceiptLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(l => l.Amount);

    public void SetHeader(DateOnly date, int personId, short typeId, string? description)
    {
        if (personId <= 0) throw new DomainException("پرداخت‌کننده (طرف حساب) الزامی است.");
        if (typeId <= 0) throw new DomainException("نوع دریافت الزامی است.");
        SetBase(date, description);
        PersonId = personId;
        TypeId = typeId;
    }

    public void ClearLines() => _lines.Clear();

    public ReceiptLine AddLine(Instrument instrument, int? cashBoxId, int? bankAccountId, decimal amount, string? refNo,
        string? description, ChequeInfo? cheque, int counterAccountId)
    {
        var row = (short)(_lines.Count + 1);
        if (amount <= 0) throw new DomainException($"ردیف {row}: مبلغ باید بزرگتر از صفر باشد.");
        switch (instrument)
        {
            case Instrument.Cash or Instrument.Cheque when cashBoxId is null:
                throw new DomainException($"ردیف {row}: صندوق را انتخاب کنید.");
            case Instrument.Bank when bankAccountId is null:
                throw new DomainException($"ردیف {row}: حساب بانکی را انتخاب کنید.");
            case Instrument.SpendCheque:
                throw new DomainException($"ردیف {row}: این ابزار در دریافت معنی ندارد.");
        }
        if (instrument == Instrument.Cheque && cheque is null)
            throw new DomainException($"ردیف {row}: مشخصات چک لازم است.");

        var line = new ReceiptLine(row, instrument, instrument == Instrument.Bank ? null : cashBoxId,
            instrument == Instrument.Bank ? bankAccountId : null, Math.Round(amount, 4),
            Texts.Optional(refNo, $"شماره پیگیری ردیف {row}", 50), Texts.Optional(description, $"شرح ردیف {row}", 500));
        if (instrument == Instrument.Cheque)
            line.Cheque = new ReceivedCheque(cheque!, PersonId, counterAccountId, line.Amount, cashBoxId!.Value, row);
        _lines.Add(line);
        return line;
    }

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

public class ReceiptLine
{
    private ReceiptLine() { } // EF

    internal ReceiptLine(short rowNo, Instrument instrument, int? cashBoxId, int? bankAccountId, decimal amount, string? refNo, string? description)
    {
        RowNo = rowNo;
        Instrument = instrument;
        CashBoxId = cashBoxId;
        BankAccountId = bankAccountId;
        Amount = amount;
        RefNo = refNo;
        Description = description;
    }

    public long Id { get; private set; }
    public long ReceiptId { get; private set; }
    public short RowNo { get; private set; }
    public Instrument Instrument { get; private set; }
    public int? CashBoxId { get; private set; }
    public int? BankAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? RefNo { get; private set; }
    public string? Description { get; private set; }
    public ReceivedCheque? Cheque { get; internal set; }
}

/// <summary>چک دریافتی (trs.ReceivedCheques) با وضعیت و محل فعلی (صندوق یا بانک).</summary>
public class ReceivedCheque
{
    private ReceivedCheque() { } // EF

    internal ReceivedCheque(ChequeInfo info, int personId, int counterAccountId, decimal amount, int cashBoxId, short row)
    {
        SerialNo = Texts.Required(info.SerialNo, $"شماره چک ردیف {row}", 30);
        SayadId = Texts.Digits(info.SayadId, $"شناسه صیادی ردیف {row}", 16);
        DueDate = info.DueDate;
        BankId = info.BankId;
        Branch = Texts.Optional(info.Branch, "شعبه", 100);
        DrawerName = Texts.Optional(info.DrawerName, "صاحب حساب", 150);
        AccountNo = Texts.Digits(info.AccountNo, "شماره حساب چک");
        PersonId = personId;
        CounterAccountId = counterAccountId;
        Amount = amount;
        CashBoxId = cashBoxId;
        Status = ReceivedChequeStatus.InBox;
    }

    public long Id { get; private set; }
    public long ReceiptLineId { get; private set; }
    public int PersonId { get; private set; }
    public int CounterAccountId { get; private set; }
    public short? BankId { get; private set; }
    public string? Branch { get; private set; }
    public string? DrawerName { get; private set; }
    public string? AccountNo { get; private set; }
    public string SerialNo { get; private set; } = null!;
    public string? SayadId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public ReceivedChequeStatus Status { get; private set; }
    public int? CashBoxId { get; private set; }
    public int? BankAccountId { get; private set; }
    public string? Description { get; private set; }

    /// <summary>تغییر وضعیت با عملیات چک یا پرداخت؛ کنترل مسیر مجاز در <see cref="ChequeRules"/>.</summary>
    public void MoveTo(ReceivedChequeStatus status, int? cashBoxId, int? bankAccountId)
    {
        Status = status;
        CashBoxId = cashBoxId;
        BankAccountId = bankAccountId;
    }
}

// ----------------------------------------------------------------------
// پرداخت
// ----------------------------------------------------------------------

/// <summary>
/// پرداخت (trs.Payments): به یک شخص با یک نوع پرداخت، با ردیف‌های نقد، بانکی، چک صادره یا خرج چک دریافتی.
/// </summary>
public class Payment : TreasuryDocument
{
    public const string DocType = "trs_payment";

    private readonly List<PaymentLine> _lines = [];

    private Payment() { } // EF

    public Payment(int docNo, int createdBy, DateTime nowUtc) => Init(docNo, createdBy, nowUtc);

    public int PersonId { get; private set; }
    public short TypeId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PaymentLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(l => l.Amount);

    public void SetHeader(DateOnly date, int personId, short typeId, string? description)
    {
        if (personId <= 0) throw new DomainException("دریافت‌کننده (طرف حساب) الزامی است.");
        if (typeId <= 0) throw new DomainException("نوع پرداخت الزامی است.");
        SetBase(date, description);
        PersonId = personId;
        TypeId = typeId;
    }

    public void ClearLines() => _lines.Clear();

    public PaymentLine AddLine(Instrument instrument, int? cashBoxId, int? bankAccountId, ReceivedCheque? spentCheque, decimal amount,
        string? refNo, string? description, ChequeInfo? cheque, int counterAccountId)
    {
        var row = (short)(_lines.Count + 1);
        switch (instrument)
        {
            case Instrument.Cash when cashBoxId is null:
                throw new DomainException($"ردیف {row}: صندوق را انتخاب کنید.");
            case Instrument.Bank or Instrument.Cheque when bankAccountId is null:
                throw new DomainException($"ردیف {row}: حساب بانکی را انتخاب کنید.");
            case Instrument.SpendCheque when spentCheque is null:
                throw new DomainException($"ردیف {row}: چک دریافتی را انتخاب کنید.");
        }
        if (instrument == Instrument.SpendCheque)
        {
            if (spentCheque!.Status != ReceivedChequeStatus.InBox)
                throw new DomainException($"ردیف {row}: چک {spentCheque.SerialNo} در صندوق نیست و خرج نمی‌شود.");
            amount = spentCheque.Amount;
        }
        if (amount <= 0) throw new DomainException($"ردیف {row}: مبلغ باید بزرگتر از صفر باشد.");
        if (instrument == Instrument.Cheque && cheque is null)
            throw new DomainException($"ردیف {row}: مشخصات چک لازم است.");

        var line = new PaymentLine(row, instrument,
            instrument == Instrument.Cash ? cashBoxId : null,
            instrument is Instrument.Bank or Instrument.Cheque ? bankAccountId : null,
            instrument == Instrument.SpendCheque ? spentCheque!.Id : null,
            Math.Round(amount, 4), Texts.Optional(refNo, $"شماره پیگیری ردیف {row}", 50),
            Texts.Optional(description, $"شرح ردیف {row}", 500));
        if (instrument == Instrument.Cheque)
            line.Cheque = new IssuedCheque(cheque!, PersonId, counterAccountId, bankAccountId!.Value, line.Amount, row);
        _lines.Add(line);
        return line;
    }

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

public class PaymentLine
{
    private PaymentLine() { } // EF

    internal PaymentLine(short rowNo, Instrument instrument, int? cashBoxId, int? bankAccountId, long? receivedChequeId,
        decimal amount, string? refNo, string? description)
    {
        RowNo = rowNo;
        Instrument = instrument;
        CashBoxId = cashBoxId;
        BankAccountId = bankAccountId;
        ReceivedChequeId = receivedChequeId;
        Amount = amount;
        RefNo = refNo;
        Description = description;
    }

    public long Id { get; private set; }
    public long PaymentId { get; private set; }
    public short RowNo { get; private set; }
    public Instrument Instrument { get; private set; }
    public int? CashBoxId { get; private set; }
    public int? BankAccountId { get; private set; }
    public long? ReceivedChequeId { get; private set; }
    public decimal Amount { get; private set; }
    public string? RefNo { get; private set; }
    public string? Description { get; private set; }
    public IssuedCheque? Cheque { get; internal set; }
}

/// <summary>چک صادره (trs.IssuedCheques) از یک حساب بانکی شرکت.</summary>
public class IssuedCheque
{
    private IssuedCheque() { } // EF

    internal IssuedCheque(ChequeInfo info, int personId, int counterAccountId, int bankAccountId, decimal amount, short row)
    {
        SerialNo = Texts.Required(info.SerialNo, $"شماره چک ردیف {row}", 30);
        SayadId = Texts.Digits(info.SayadId, $"شناسه صیادی ردیف {row}", 16);
        DueDate = info.DueDate;
        PersonId = personId;
        CounterAccountId = counterAccountId;
        BankAccountId = bankAccountId;
        Amount = amount;
        Status = IssuedChequeStatus.Issued;
    }

    public long Id { get; private set; }
    public long PaymentLineId { get; private set; }
    public int PersonId { get; private set; }
    public int CounterAccountId { get; private set; }
    public int BankAccountId { get; private set; }
    public string SerialNo { get; private set; } = null!;
    public string? SayadId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public IssuedChequeStatus Status { get; private set; }
    public string? Description { get; private set; }

    public void MoveTo(IssuedChequeStatus status) => Status = status;
}
