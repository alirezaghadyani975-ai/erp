using Erp.SharedKernel.Text;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.Vouchers;

/// <summary>
/// سند حسابداری (fin.Vouchers). همه زیرسیستم‌ها در نهایت از طریق این موجودیت سند صادر می‌کنند.
/// شماره موقت (<see cref="TempNo"/>) از ابتدا داده می‌شود؛ شماره قطعی (<see cref="VoucherNo"/>)
/// فقط هنگام دائم شدن، پیوسته و بدون جای خالی در هر سال مالی.
/// </summary>
public class Voucher : AggregateRoot<long>
{
    private readonly List<VoucherLine> _lines = [];

    private Voucher() { } // EF

    public static Voucher Create(FiscalYear fiscalYear, int tempNo, DateOnly date, string? description, int createdBy,
        DateTime nowUtc, VoucherType type = VoucherType.Normal, SourceDocument? source = null)
    {
        fiscalYear.EnsureCanPostOn(date);
        if (tempNo <= 0)
            throw new DomainException("شماره موقت سند نامعتبر است.");

        return new Voucher
        {
            FiscalYearId = fiscalYear.Id,
            TempNo = tempNo,
            VoucherDate = date,
            Description = Clean(description),
            Type = type,
            Status = VoucherStatus.Draft,
            SourceModule = source?.Module,
            SourceDocType = source?.DocType,
            SourceDocumentId = source?.DocumentId,
            CreatedBy = createdBy,
            CreatedAt = nowUtc
        };
    }

    public short FiscalYearId { get; private set; }
    public int TempNo { get; private set; }
    public int? VoucherNo { get; private set; }
    public DateOnly VoucherDate { get; private set; }
    public VoucherType Type { get; private set; }
    public VoucherStatus Status { get; private set; }

    /// <summary>زیرسیستم صادرکننده (مثلاً sales). خالی یعنی سند دستی.</summary>
    public string? SourceModule { get; private set; }
    public string? SourceDocType { get; private set; }
    public long? SourceDocumentId { get; private set; }

    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }

    /// <summary>کنترل همزمانی: دو نفر یک سند را همزمان ویرایش نکنند.</summary>
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<VoucherLine> Lines => _lines.AsReadOnly();

    public decimal TotalDebit => _lines.Sum(l => l.Debit);
    public decimal TotalCredit => _lines.Sum(l => l.Credit);
    public bool IsBalanced => TotalDebit == TotalCredit;
    public bool IsSystemGenerated => SourceModule is not null;

    /// <summary>
    /// افزودن ردیف. <paramref name="details"/> به ترتیب سطح ۱ تا ۶ است؛
    /// موجودیت کامل تفصیلی لازم است تا قاعده معین بررسی شود.
    /// </summary>
    public VoucherLine AddLine(Account account, IReadOnlyList<DetailAccount?> details, decimal debit, decimal credit,
        LineExtras? extras = null)
    {
        EnsureEditable();
        extras ??= LineExtras.None;
        ValidateAccount(account);
        ValidateAmounts(debit, credit);
        var detailIds = ValidateDetails(account, details);

        if (extras.Currency is { } c && (c.Amount <= 0 || c.ExchangeRate <= 0))
            throw new DomainException("مبلغ ارزی و نرخ تبدیل باید بزرگتر از صفر باشند.");
        if (extras.Quantity is { } q && q.Value <= 0)
            throw new DomainException("مقدار باید بزرگتر از صفر باشد.");
        if (_lines.Count >= short.MaxValue)
            throw new DomainException("تعداد ردیف‌های سند بیش از حد مجاز است.");

        var line = new VoucherLine((short)(_lines.Count + 1), account.Id, detailIds, debit, credit,
            extras with { Description = Clean(extras.Description) });
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(int rowNo)
    {
        EnsureEditable();
        var line = _lines.FirstOrDefault(l => l.RowNo == rowNo)
                   ?? throw new DomainException($"ردیف {rowNo} در سند وجود ندارد.");
        _lines.Remove(line);
        for (var i = 0; i < _lines.Count; i++)
            _lines[i].RowNo = (short)(i + 1);
    }

    public void ClearLines()
    {
        EnsureEditable();
        _lines.Clear();
    }

    public void UpdateHeader(FiscalYear fiscalYear, DateOnly date, string? description)
    {
        EnsureEditable();
        if (fiscalYear.Id != FiscalYearId)
            throw new DomainException("تغییر سال مالی سند مجاز نیست.");
        fiscalYear.EnsureCanPostOn(date);
        VoucherDate = date;
        Description = Clean(description);
    }

    /// <summary>پیش‌نویس ← موقت</summary>
    public void MarkTemporary()
    {
        if (Status != VoucherStatus.Draft)
            throw new DomainException("فقط سند پیش‌نویس را می‌توان موقت کرد.");
        EnsureValidForPosting();
        Status = VoucherStatus.Temporary;
    }

    /// <summary>موقت ← پیش‌نویس</summary>
    public void ReturnToDraft()
    {
        if (Status != VoucherStatus.Temporary)
            throw new DomainException("فقط سند موقت را می‌توان به پیش‌نویس برگرداند.");
        Status = VoucherStatus.Draft;
    }

    /// <summary>موقت ← دائم. شماره قطعی را سرویس به‌صورت پیوسته تعیین می‌کند.</summary>
    public void MarkPermanent(FiscalYear fiscalYear, int voucherNo, int approvedBy, DateTime nowUtc)
    {
        if (Status != VoucherStatus.Temporary)
            throw new DomainException("سند باید ابتدا موقت شود، سپس دائم.");
        if (fiscalYear.Id != FiscalYearId)
            throw new DomainException("سال مالی سند نامعتبر است.");
        fiscalYear.EnsureCanPostOn(VoucherDate);
        EnsureValidForPosting();

        Status = VoucherStatus.Permanent;
        VoucherNo = voucherNo;
        ApprovedBy = approvedBy;
        ApprovedAt = nowUtc;
        Raise(new VoucherMadePermanent(Id, FiscalYearId, voucherNo));
    }

    /// <summary>ابطال سند پیش‌نویس یا موقت. سند دائم فقط با سند اصلاحی اصلاح می‌شود.</summary>
    public void Void()
    {
        if (Status is VoucherStatus.Permanent)
            throw new DomainException("سند دائم باطل نمی‌شود؛ اصلاح فقط با سند اصلاحی.");
        if (Status is VoucherStatus.Void)
            throw new DomainException("سند قبلاً باطل شده است.");
        Status = VoucherStatus.Void;
    }

    private void EnsureValidForPosting()
    {
        if (_lines.Count < 2)
            throw new DomainException("سند باید حداقل دو ردیف داشته باشد.");
        if (!IsBalanced)
            throw new DomainException($"سند تراز نیست. جمع بدهکار {TotalDebit:N0} و جمع بستانکار {TotalCredit:N0} است.");
    }

    private void EnsureEditable()
    {
        if (Status != VoucherStatus.Draft)
            throw new DomainException("فقط سند پیش‌نویس قابل ویرایش است.");
    }

    private static void ValidateAccount(Account account)
    {
        if (account.Level != AccountLevel.Subsidiary)
            throw new DomainException($"در سند فقط حساب معین قابل استفاده است؛ «{account.Code} - {account.Title}» معین نیست.");
        if (!account.IsActive)
            throw new DomainException($"حساب «{account.Code} - {account.Title}» غیرفعال است.");
    }

    private static void ValidateAmounts(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0)
            throw new DomainException("مبلغ بدهکار یا بستانکار نمی‌تواند منفی باشد.");
        if ((debit > 0) == (credit > 0))
            throw new DomainException("هر ردیف باید دقیقاً یکی از مبالغ بدهکار یا بستانکار را داشته باشد.");
    }

    private static int?[] ValidateDetails(Account account, IReadOnlyList<DetailAccount?> details)
    {
        if (details.Count > Account.MaxDetailLevels)
            throw new DomainException($"حداکثر {Account.MaxDetailLevels} سطح تفصیلی مجاز است.");

        var ids = new int?[Account.MaxDetailLevels];
        var gapFound = false;

        for (var i = 0; i < Account.MaxDetailLevels; i++)
        {
            var level = i + 1;
            var detail = i < details.Count ? details[i] : null;

            if (detail is null)
            {
                if (account.IsDetailLevelRequired(level))
                    throw new DomainException($"برای حساب «{account.Title}» تفصیلی سطح {level} الزامی است.");
                gapFound = true;
                continue;
            }

            if (gapFound)
                throw new DomainException($"تفصیلی سطح {level} بدون پر کردن سطوح قبلی مجاز نیست.");
            if (!detail.IsActive)
                throw new DomainException($"تفصیلی «{detail.Code} - {detail.Title}» غیرفعال است.");
            if (!account.AcceptsDetail(level, detail.DetailTypeId))
                throw new DomainException($"حساب «{account.Title}» در سطح {level} تفصیلی «{detail.Title}» را نمی‌پذیرد.");

            ids[i] = detail.Id;
        }

        return ids;
    }

    private static string? Clean(string? text) => PersianText.NormalizeOrNull(text);
}

/// <summary>سند منبع در زیرسیستم (مثلاً فاکتور فروش شماره ۱۲۳).</summary>
public sealed record SourceDocument(string Module, string DocType, long DocumentId);

public sealed record VoucherMadePermanent(long VoucherId, short FiscalYearId, int VoucherNo) : DomainEvent;
