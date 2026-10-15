using Erp.SharedKernel.Domain;

namespace Erp.Treasury.Domain;

/// <summary>نوع منبع یا مقصد وجه.</summary>
public enum FundKind : byte
{
    CashBox = 1,
    BankAccount = 2,
    Imprest = 3
}

/// <summary>انتقال وجه داخل سازمان (trs.Transfers) بین صندوق، حساب بانکی و تنخواه‌گردان؛ کارمزد اختیاری.</summary>
public class Transfer : TreasuryDocument
{
    public const string DocType = "trs_transfer";

    private Transfer() { } // EF

    public Transfer(int docNo, int createdBy, DateTime nowUtc) => Init(docNo, createdBy, nowUtc);

    public FundKind FromKind { get; private set; }
    public int FromId { get; private set; }
    public FundKind ToKind { get; private set; }
    public int ToId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal Fee { get; private set; }
    public int? FeeFactorId { get; private set; }
    public int? FeeCenterId { get; private set; }
    public string? RefNo { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public void Set(DateOnly date, FundKind fromKind, int fromId, FundKind toKind, int toId, decimal amount, decimal fee,
        int? feeFactorId, int? feeCenterId, string? refNo, string? description)
    {
        if (!Enum.IsDefined(fromKind) || !Enum.IsDefined(toKind)) throw new DomainException("نوع مبدأ یا مقصد معتبر نیست.");
        if (fromId <= 0) throw new DomainException("مبدأ را انتخاب کنید.");
        if (toId <= 0) throw new DomainException("مقصد را انتخاب کنید.");
        if (fromKind == toKind && fromId == toId) throw new DomainException("مبدأ و مقصد یکی است.");
        if (amount <= 0) throw new DomainException("مبلغ انتقال باید بزرگتر از صفر باشد.");
        if (fee < 0) throw new DomainException("کارمزد منفی نیست.");
        if (fee > 0 && feeFactorId is null) throw new DomainException("برای کارمزد، عامل هزینه (مثلاً کارمزدهای بانکی) را انتخاب کنید.");

        SetBase(date, description);
        (FromKind, FromId, ToKind, ToId) = (fromKind, fromId, toKind, toId);
        Amount = Math.Round(amount, 4);
        Fee = Math.Round(fee, 4);
        FeeFactorId = fee > 0 ? feeFactorId : null;
        FeeCenterId = fee > 0 ? feeCenterId : null;
        RefNo = Texts.Optional(refNo, "شماره پیگیری", 50);
    }

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>صورت‌هزینه تنخواه (trs.ImprestSettlements): هزینه‌های پرداخت‌شده از تنخواه با عامل و مرکز هزینه.</summary>
public class ImprestSettlement : TreasuryDocument
{
    public const string DocType = "trs_imprest";

    private readonly List<ImprestSettlementLine> _lines = [];

    private ImprestSettlement() { } // EF

    public ImprestSettlement(int docNo, int createdBy, DateTime nowUtc) => Init(docNo, createdBy, nowUtc);

    public int ImprestFundId { get; private set; }
    public decimal VatAmount { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<ImprestSettlementLine> Lines => _lines.AsReadOnly();
    public decimal LinesTotal => _lines.Sum(l => l.Amount);
    public decimal Total => LinesTotal + VatAmount;

    public void SetHeader(DateOnly date, int imprestFundId, decimal vatAmount, string? description)
    {
        if (imprestFundId <= 0) throw new DomainException("تنخواه‌گردان را انتخاب کنید.");
        if (vatAmount < 0) throw new DomainException("مبلغ ارزش افزوده منفی نیست.");
        SetBase(date, description);
        ImprestFundId = imprestFundId;
        VatAmount = Math.Round(vatAmount, 4);
    }

    public void ClearLines() => _lines.Clear();

    public void SetLines(IEnumerable<(int FactorId, int? CenterId, decimal Amount, string? Description)> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("حداقل یک ردیف هزینه لازم است.");
        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.FactorId <= 0) throw new DomainException($"ردیف {row}: عامل هزینه الزامی است.");
            if (l.Amount <= 0) throw new DomainException($"ردیف {row}: مبلغ باید بزرگتر از صفر باشد.");
            _lines.Add(new ImprestSettlementLine(row, l.FactorId, l.CenterId, Math.Round(l.Amount, 4),
                Texts.Optional(l.Description, $"شرح ردیف {row}", 500)));
        }
    }

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

public class ImprestSettlementLine
{
    private ImprestSettlementLine() { } // EF

    internal ImprestSettlementLine(short rowNo, int factorId, int? centerId, decimal amount, string? description)
    {
        RowNo = rowNo;
        CostFactorId = factorId;
        CostCenterId = centerId;
        Amount = amount;
        Description = description;
    }

    public long Id { get; private set; }
    public long SettlementId { get; private set; }
    public short RowNo { get; private set; }
    public int CostFactorId { get; private set; }
    public int? CostCenterId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
}

// ----------------------------------------------------------------------
// عملیات چک
// ----------------------------------------------------------------------

public enum ChequeSide : byte
{
    Receivable = 1,
    Payable = 2
}

public enum ChequeOperationType : byte
{
    Deposit = 1,          // واگذاری به بانک
    Collect = 2,          // وصول
    Bounce = 3,           // برگشت
    ReturnToDrawer = 4,   // عودت به صاحب چک
    Legal = 5,            // حقوقی
    Withdraw = 6,         // برگشت از واگذاری به صندوق
    Clear = 11,           // پاس چک صادره
    Cancel = 12           // ابطال / عودت چک صادره به ما
}

/// <summary>مسیرهای مجاز وضعیت چک در هر عملیات.</summary>
public static class ChequeRules
{
    public static IReadOnlyList<ReceivedChequeStatus> AllowedFrom(ChequeOperationType op) => op switch
    {
        ChequeOperationType.Deposit => [ReceivedChequeStatus.InBox],
        ChequeOperationType.Collect => [ReceivedChequeStatus.InBox, ReceivedChequeStatus.InCollection, ReceivedChequeStatus.Bounced, ReceivedChequeStatus.Legal],
        ChequeOperationType.Bounce => [ReceivedChequeStatus.InBox, ReceivedChequeStatus.InCollection],
        ChequeOperationType.ReturnToDrawer => [ReceivedChequeStatus.InBox, ReceivedChequeStatus.Bounced, ReceivedChequeStatus.Legal],
        ChequeOperationType.Legal => [ReceivedChequeStatus.Bounced],
        ChequeOperationType.Withdraw => [ReceivedChequeStatus.InCollection],
        _ => []
    };

    public static ReceivedChequeStatus Target(ChequeOperationType op) => op switch
    {
        ChequeOperationType.Deposit => ReceivedChequeStatus.InCollection,
        ChequeOperationType.Collect => ReceivedChequeStatus.Collected,
        ChequeOperationType.Bounce => ReceivedChequeStatus.Bounced,
        ChequeOperationType.ReturnToDrawer => ReceivedChequeStatus.Returned,
        ChequeOperationType.Legal => ReceivedChequeStatus.Legal,
        ChequeOperationType.Withdraw => ReceivedChequeStatus.InBox,
        _ => throw new DomainException("عملیات برای چک دریافتنی معتبر نیست.")
    };

    public static ChequeSide SideOf(ChequeOperationType op) => (byte)op >= 11 ? ChequeSide.Payable : ChequeSide.Receivable;
}

/// <summary>
/// سند عملیات چک (trs.ChequeOperations): یک عملیات (مثل واگذاری به بانک) روی یک یا چند چک با یک سند حسابداری.
/// برگرداندن فقط با حذف آخرین عملیات هر چک.
/// </summary>
public class ChequeOperation : TreasuryDocument
{
    public const string ReceivableDocType = "trs_cheque_in";
    public const string PayableDocType = "trs_cheque_out";

    private readonly List<ChequeOperationLine> _lines = [];

    private ChequeOperation() { } // EF

    public ChequeOperation(ChequeOperationType operation, int docNo, int createdBy, DateTime nowUtc)
    {
        if (!Enum.IsDefined(operation)) throw new DomainException("عملیات معتبر نیست.");
        Operation = operation;
        Side = ChequeRules.SideOf(operation);
        Init(docNo, createdBy, nowUtc);
    }

    public ChequeSide Side { get; private set; }
    public ChequeOperationType Operation { get; private set; }
    public int? CashBoxId { get; private set; }
    public int? BankAccountId { get; private set; }

    public IReadOnlyList<ChequeOperationLine> Lines => _lines.AsReadOnly();

    public string DocType => Side == ChequeSide.Receivable ? ReceivableDocType : PayableDocType;

    public void SetHeader(DateOnly date, int? cashBoxId, int? bankAccountId, string? description)
    {
        SetBase(date, description);
        CashBoxId = cashBoxId;
        BankAccountId = bankAccountId;
    }

    /// <summary>اعمال عملیات روی چک دریافتی و ثبت وضعیت قبلی برای برگرداندن.</summary>
    public void Apply(ReceivedCheque cheque)
    {
        if (Side != ChequeSide.Receivable) throw new DomainException("این عملیات برای چک دریافتنی نیست.");
        if (_lines.Any(l => l.ReceivedChequeId == cheque.Id)) throw new DomainException($"چک {cheque.SerialNo} دو بار انتخاب شده است.");
        if (!ChequeRules.AllowedFrom(Operation).Contains(cheque.Status))
            throw new DomainException($"چک {cheque.SerialNo} در وضعیت «{ChequeTitles.Of(cheque.Status)}» است و «{ChequeTitles.Of(Operation)}» برایش ممکن نیست.");

        int? box = cheque.CashBoxId, bank = cheque.BankAccountId;
        switch (Operation)
        {
            case ChequeOperationType.Deposit:
                if (BankAccountId is null) throw new DomainException("برای واگذاری، حساب بانکی را انتخاب کنید.");
                (box, bank) = (null, BankAccountId);
                break;
            case ChequeOperationType.Collect when cheque.Status == ReceivedChequeStatus.InCollection:
                (box, bank) = (null, cheque.BankAccountId);
                break;
            case ChequeOperationType.Collect:
                if ((BankAccountId is null) == (CashBoxId is null))
                    throw new DomainException("برای وصول، یکی از حساب بانکی یا صندوق را انتخاب کنید.");
                (box, bank) = (CashBoxId, BankAccountId);
                break;
            case ChequeOperationType.Withdraw:
                if (CashBoxId is null) throw new DomainException("برای برگشت از واگذاری، صندوق را انتخاب کنید.");
                (box, bank) = (CashBoxId, null);
                break;
        }

        _lines.Add(new ChequeOperationLine(cheque.Id, null, (byte)cheque.Status, (byte)ChequeRules.Target(Operation),
            cheque.CashBoxId, cheque.BankAccountId));
        cheque.MoveTo(ChequeRules.Target(Operation), box, bank);
    }

    public void Apply(IssuedCheque cheque)
    {
        if (Side != ChequeSide.Payable) throw new DomainException("این عملیات برای چک پرداختنی نیست.");
        if (_lines.Any(l => l.IssuedChequeId == cheque.Id)) throw new DomainException($"چک {cheque.SerialNo} دو بار انتخاب شده است.");
        if (cheque.Status != IssuedChequeStatus.Issued)
            throw new DomainException($"چک {cheque.SerialNo} در وضعیت «{ChequeTitles.Of(cheque.Status)}» است.");
        var target = Operation == ChequeOperationType.Clear ? IssuedChequeStatus.Cleared : IssuedChequeStatus.Cancelled;
        _lines.Add(new ChequeOperationLine(null, cheque.Id, (byte)cheque.Status, (byte)target, null, cheque.BankAccountId));
        cheque.MoveTo(target);
    }
}

public class ChequeOperationLine
{
    private ChequeOperationLine() { } // EF

    internal ChequeOperationLine(long? receivedChequeId, long? issuedChequeId, byte fromStatus, byte toStatus,
        int? fromCashBoxId, int? fromBankAccountId)
    {
        ReceivedChequeId = receivedChequeId;
        IssuedChequeId = issuedChequeId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        FromCashBoxId = fromCashBoxId;
        FromBankAccountId = fromBankAccountId;
    }

    public long Id { get; private set; }
    public long OperationId { get; private set; }
    public long? ReceivedChequeId { get; private set; }
    public long? IssuedChequeId { get; private set; }
    public byte FromStatus { get; private set; }
    public byte ToStatus { get; private set; }
    public int? FromCashBoxId { get; private set; }
    public int? FromBankAccountId { get; private set; }
}

// ----------------------------------------------------------------------
// اسناد تضمینی
// ----------------------------------------------------------------------

public enum GuaranteeDirection : byte
{
    Received = 1,  // دریافتی: سند دیگران نزد ما
    Issued = 2     // پرداختی: سند ما نزد دیگران
}

public enum GuaranteeKind : byte
{
    Cheque = 1,
    PromissoryNote = 2,   // سفته
    BankGuarantee = 3,    // ضمانت‌نامه بانکی
    Other = 4
}

/// <summary>سند تضمینی (trs.Guarantees)؛ ثبت و استرداد هر کدام سند انتظامی خودش را دارد.</summary>
public class Guarantee : TreasuryDocument
{
    public const string DocType = "trs_guarantee";
    public const string ReturnDocType = "trs_guarantee_return";

    private Guarantee() { } // EF

    public Guarantee(GuaranteeDirection direction, int docNo, int createdBy, DateTime nowUtc)
    {
        if (!Enum.IsDefined(direction)) throw new DomainException("جهت سند تضمینی معتبر نیست.");
        Direction = direction;
        Init(docNo, createdBy, nowUtc);
    }

    public GuaranteeDirection Direction { get; private set; }
    public GuaranteeKind Kind { get; private set; }
    public int PersonId { get; private set; }
    public int? BankAccountId { get; private set; }
    public string? Number { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? Subject { get; private set; }
    public bool IsReturned { get; private set; }
    public DateOnly? ReturnDate { get; private set; }

    public void Set(DateOnly date, GuaranteeKind kind, int personId, int? bankAccountId, string? number, decimal amount,
        DateOnly? dueDate, string? subject, string? description)
    {
        if (IsReturned) throw new DomainException("سند تضمینی مستردشده اصلاح نمی‌شود؛ ابتدا استرداد را لغو کنید.");
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع سند تضمینی معتبر نیست.");
        if (personId <= 0) throw new DomainException("شخص طرف سند تضمینی الزامی است.");
        if (amount <= 0) throw new DomainException("مبلغ باید بزرگتر از صفر باشد.");
        if (kind == GuaranteeKind.BankGuarantee && Direction == GuaranteeDirection.Issued && bankAccountId is null)
            throw new DomainException("برای ضمانت‌نامه بانکی ما، حساب بانکی صادرکننده را انتخاب کنید.");
        SetBase(date, description);
        Kind = kind;
        PersonId = personId;
        BankAccountId = kind == GuaranteeKind.BankGuarantee && Direction == GuaranteeDirection.Issued ? bankAccountId : null;
        Number = Texts.Optional(number, "شماره سند", 50);
        Amount = Math.Round(amount, 4);
        DueDate = dueDate;
        Subject = Texts.Optional(subject, "موضوع", 300);
    }

    public void MarkReturned(DateOnly date)
    {
        if (IsReturned) throw new DomainException("این سند قبلاً مسترد شده است.");
        if (date < DocDate) throw new DomainException("تاریخ استرداد قبل از تاریخ ثبت است.");
        IsReturned = true;
        ReturnDate = date;
    }

    public void UndoReturn()
    {
        if (!IsReturned) throw new DomainException("این سند مسترد نشده است.");
        IsReturned = false;
        ReturnDate = null;
    }
}

/// <summary>عنوان فارسی وضعیت‌ها و عملیات.</summary>
public static class ChequeTitles
{
    public static string Of(ReceivedChequeStatus s) => s switch
    {
        ReceivedChequeStatus.InBox => "در صندوق",
        ReceivedChequeStatus.InCollection => "در جریان وصول",
        ReceivedChequeStatus.Collected => "وصول‌شده",
        ReceivedChequeStatus.Bounced => "برگشتی",
        ReceivedChequeStatus.Legal => "حقوقی",
        ReceivedChequeStatus.Returned => "عودت‌شده",
        ReceivedChequeStatus.Spent => "خرج‌شده",
        _ => s.ToString()
    };

    public static string Of(IssuedChequeStatus s) => s switch
    {
        IssuedChequeStatus.Issued => "پاس‌نشده",
        IssuedChequeStatus.Cleared => "پاس‌شده",
        IssuedChequeStatus.Cancelled => "باطل/عودت‌شده",
        _ => s.ToString()
    };

    public static string Of(ChequeOperationType op) => op switch
    {
        ChequeOperationType.Deposit => "واگذاری به بانک",
        ChequeOperationType.Collect => "وصول",
        ChequeOperationType.Bounce => "برگشت",
        ChequeOperationType.ReturnToDrawer => "عودت به صاحب چک",
        ChequeOperationType.Legal => "اقدام حقوقی",
        ChequeOperationType.Withdraw => "برگشت از واگذاری",
        ChequeOperationType.Clear => "پاس شدن",
        ChequeOperationType.Cancel => "ابطال / عودت به ما",
        _ => op.ToString()
    };
}
