using Erp.SharedKernel.Domain;

namespace Erp.Treasury.Domain;

/// <summary>نوع دفترچه اسناد پرداختنی.</summary>
public enum InstrumentKind : byte
{
    ChequeBook = 1,        // دسته‌چک
    PromissoryNoteBook = 2 // دفتر سفته (بازه شماره سفته‌های خریداری‌شده)
}

public enum LeafStatus : byte
{
    Blank = 1,   // سفید، قابل استفاده
    Used = 2,    // استفاده‌شده (چک صادره یا سند تضمینی)
    Void = 3     // باطل (مخدوش، مفقود، ...)
}

/// <summary>
/// دسته‌چک یا دفتر سفته (trs.InstrumentBooks) با برگ‌هایش. چک صادره و سفته/چک تضمینی پرداختی فقط از برگ‌های
/// سفید همین دفترچه‌ها انتخاب می‌شوند تا مصرف هر برگ کنترل شود.
/// </summary>
public class InstrumentBook : Entity<int>
{
    public const int MaxLeaves = 1000;

    private readonly List<InstrumentLeaf> _leaves = [];

    private InstrumentBook() { } // EF

    public InstrumentBook(InstrumentKind kind, int? bankAccountId, string title, string? series, long fromNo, long toNo, byte digits,
        DateOnly receivedDate, decimal? maxAmount, string? notes)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع دفترچه معتبر نیست.");
        if (kind == InstrumentKind.ChequeBook && bankAccountId is null) throw new DomainException("حساب بانکی دسته‌چک را انتخاب کنید.");
        if (fromNo <= 0 || toNo < fromNo) throw new DomainException("بازه شماره برگ‌ها معتبر نیست (از ≤ تا).");
        if (toNo - fromNo + 1 > MaxLeaves) throw new DomainException($"هر دفترچه حداکثر {MaxLeaves} برگ است.");
        if (digits > 20) throw new DomainException("تعداد رقم شماره برگ حداکثر ۲۰ است.");
        if (maxAmount is < 0) throw new DomainException("سقف مبلغ نمی‌تواند منفی باشد.");
        Kind = kind;
        BankAccountId = kind == InstrumentKind.ChequeBook ? bankAccountId : null;
        Series = Texts.Optional(series, "سری", 20);
        FromNo = fromNo;
        ToNo = toNo;
        Digits = digits;
        Update(title, receivedDate, maxAmount, notes);
        for (var no = fromNo; no <= toNo; no++)
            _leaves.Add(new InstrumentLeaf(no, SerialOf(no)));
    }

    public InstrumentKind Kind { get; private set; }
    public int? BankAccountId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Series { get; private set; }
    public long FromNo { get; private set; }
    public long ToNo { get; private set; }
    /// <summary>طول شماره با صفر پیشرو (صفر = بدون صفر پیشرو).</summary>
    public byte Digits { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    /// <summary>سقف مبلغ سفته (چاپ‌شده روی سفته).</summary>
    public decimal? MaxAmount { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public IReadOnlyList<InstrumentLeaf> Leaves => _leaves.AsReadOnly();

    public void Update(string title, DateOnly receivedDate, decimal? maxAmount, string? notes)
    {
        Title = Texts.Required(title, "عنوان", 100);
        ReceivedDate = receivedDate;
        MaxAmount = Kind == InstrumentKind.PromissoryNoteBook && maxAmount is > 0 ? Math.Round(maxAmount.Value, 4) : null;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>شماره نمایشی برگ: سری/شماره با صفر پیشرو.</summary>
    public string SerialOf(long no)
    {
        var n = Digits > 0 ? no.ToString().PadLeft(Digits, '0') : no.ToString();
        return Series is null ? n : $"{Series}/{n}";
    }
}

/// <summary>برگ دسته‌چک یا سفته (trs.InstrumentLeaves).</summary>
public class InstrumentLeaf
{
    private InstrumentLeaf() { } // EF

    internal InstrumentLeaf(long no, string serialNo)
    {
        No = no;
        SerialNo = serialNo;
        Status = LeafStatus.Blank;
    }

    public long Id { get; private set; }
    public int BookId { get; private set; }
    public long No { get; private set; }
    public string SerialNo { get; private set; } = null!;
    public LeafStatus Status { get; private set; }
    public DateOnly? VoidDate { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>مصرف برگ در چک صادره یا سند تضمینی.</summary>
    public void Use()
    {
        if (Status != LeafStatus.Blank)
            throw new DomainException($"برگ {SerialNo} {(Status == LeafStatus.Void ? "باطل شده" : "قبلاً استفاده شده")} است.");
        Status = LeafStatus.Used;
    }

    /// <summary>برگشت برگ به سفید (حذف یا اصلاح فرمی که آن را مصرف کرده بود).</summary>
    public void Release()
    {
        if (Status == LeafStatus.Used) Status = LeafStatus.Blank;
    }

    public void MarkVoid(DateOnly date, string? reason)
    {
        if (Status != LeafStatus.Blank) throw new DomainException($"فقط برگ سفید باطل می‌شود؛ برگ {SerialNo} استفاده شده است.");
        Status = LeafStatus.Void;
        VoidDate = date;
        VoidReason = Texts.Optional(reason, "علت ابطال", 200);
    }

    public void UndoVoid()
    {
        if (Status != LeafStatus.Void) throw new DomainException($"برگ {SerialNo} باطل نشده است.");
        Status = LeafStatus.Blank;
        VoidDate = null;
        VoidReason = null;
    }
}

public enum BankGuaranteeStatus : byte
{
    Available = 1,   // نزد ما
    Delivered = 2,   // نزد ذی‌نفع (تحویل با سند تضمینی)
    Cancelled = 3    // باطل / عودت به بانک
}

/// <summary>
/// ضمانت‌نامه بانکی صادرشده برای شرکت (trs.BankGuarantees). اول از بانک تعریف می‌شود، بعد با سند تضمینی پرداختی
/// به ذی‌نفع تحویل و با استرداد دوباره نزد ما؛ در پایان باطل (عودت به بانک).
/// </summary>
public class BankGuarantee : Entity<int>
{
    private BankGuarantee() { } // EF

    public BankGuarantee(int bankAccountId, string number, decimal amount, DateOnly issueDate, DateOnly? expiryDate, string? subject,
        string? notes)
    {
        Status = BankGuaranteeStatus.Available;
        Update(bankAccountId, number, amount, issueDate, expiryDate, subject, notes);
    }

    public int BankAccountId { get; private set; }
    public string Number { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public string? Subject { get; private set; }
    public BankGuaranteeStatus Status { get; private set; }
    public DateOnly? CancelDate { get; private set; }
    public string? Notes { get; private set; }

    public void Update(int bankAccountId, string number, decimal amount, DateOnly issueDate, DateOnly? expiryDate, string? subject,
        string? notes)
    {
        if (Status == BankGuaranteeStatus.Delivered && (bankAccountId != BankAccountId || amount != Amount))
            throw new DomainException("ضمانت‌نامه نزد ذی‌نفع است؛ بانک و مبلغ آن عوض نمی‌شود.");
        if (bankAccountId <= 0) throw new DomainException("حساب بانکی صادرکننده را انتخاب کنید.");
        if (amount <= 0) throw new DomainException("مبلغ ضمانت‌نامه باید بزرگتر از صفر باشد.");
        if (expiryDate is { } e && e < issueDate) throw new DomainException("تاریخ اعتبار قبل از تاریخ صدور است.");
        BankAccountId = bankAccountId;
        Number = Texts.Required(number, "شماره ضمانت‌نامه", 50);
        Amount = Math.Round(amount, 4);
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        Subject = Texts.Optional(subject, "موضوع", 300);
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void Deliver(DateOnly date)
    {
        if (Status != BankGuaranteeStatus.Available)
            throw new DomainException($"ضمانت‌نامه {Number} {(Status == BankGuaranteeStatus.Cancelled ? "باطل شده" : "نزد ذی‌نفع دیگری")} است.");
        if (ExpiryDate is { } e && e < date) throw new DomainException($"اعتبار ضمانت‌نامه {Number} تمام شده است.");
        Status = BankGuaranteeStatus.Delivered;
    }

    public void TakeBack() => Status = BankGuaranteeStatus.Available;

    public void Cancel(DateOnly date)
    {
        if (Status != BankGuaranteeStatus.Available) throw new DomainException("فقط ضمانت‌نامه نزد ما باطل می‌شود؛ اول استرداد را ثبت کنید.");
        if (date < IssueDate) throw new DomainException("تاریخ ابطال قبل از تاریخ صدور است.");
        Status = BankGuaranteeStatus.Cancelled;
        CancelDate = date;
    }

    public void UndoCancel()
    {
        if (Status != BankGuaranteeStatus.Cancelled) throw new DomainException("ضمانت‌نامه باطل نشده است.");
        Status = BankGuaranteeStatus.Available;
        CancelDate = null;
    }
}
