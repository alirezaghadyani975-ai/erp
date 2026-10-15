namespace Erp.Accounting.Contracts;

/// <summary>تفصیلی‌ای که سند منبع می‌شناسد؛ حسابداری آن را به حساب تفصیلی تبدیل می‌کند.</summary>
public enum PostingDetailKind : byte
{
    /// <summary>شخص (base.Persons): تفصیلی مشترک شخص یا تفصیلی کارکنان، هر کدام که معین بپذیرد.</summary>
    Person = 1,
    /// <summary>عامل هزینه (cost.CostFactors).</summary>
    CostFactor = 2,
    /// <summary>مرکز هزینه (cost.CostCenters).</summary>
    CostCenter = 3,
    /// <summary>ماه سررسید؛ Id شماره ماه (۱ تا ۱۲).</summary>
    DueMonth = 4,
    /// <summary>صندوق (trs.CashBoxes).</summary>
    CashBox = 5,
    /// <summary>حساب بانکی (trs.BankAccounts).</summary>
    BankAccount = 6,
    /// <summary>تنخواه‌گردان (trs.ImprestFunds).</summary>
    Imprest = 7,
    /// <summary>خود حساب تفصیلی (fin.DetailAccounts)، مثلاً صندوق یا حساب بانکی.</summary>
    DetailAccount = 9
}

public sealed record PostingDetail(PostingDetailKind Kind, long Id);

/// <summary>
/// یک ردیف سند منبع با کلید ردیف الگو (مثلاً expense یا vat). مبلغ منفی در طرف مقابل الگو ثبت می‌شود؛
/// مبلغ صفر نادیده گرفته می‌شود. تفصیلی‌ها به ترتیب سطح‌های قاعده معین خودکار جا می‌گیرند.
/// </summary>
/// <param name="AccountId">معین ردیف وقتی ردیف الگو قاعده «source» دارد (فرم معین را تعیین می‌کند، مثلاً از نوع دریافت).</param>
public sealed record PostingLine(string LineKey, decimal Amount, IReadOnlyList<PostingDetail> Details, string? Description = null,
    int? AccountId = null, string? FollowUpNo = null, DateOnly? FollowUpDate = null);

/// <summary>درخواست صدور (یا صدور دوباره) سند حسابداری یک سند منبع.</summary>
public sealed record PostingRequest(
    string SourceModule,
    string DocType,
    long DocumentId,
    DateOnly Date,
    string Description,
    IReadOnlyList<PostingLine> Lines);

/// <summary>وضعیت سند حسابداری: 1 پیش‌نویس، 2 موقت، 3 دائم، 9 باطل.</summary>
public sealed record PostedVoucher(long VoucherId, short FiscalYearId, int TempNo, int? VoucherNo, byte Status)
{
    public bool IsPermanent => Status == 3;
}

/// <summary>
/// صدور سند خودکار از زیرسیستم‌ها. پیاده‌سازی در حسابداری است و با الگوی همان نوع سند (fin.PostingTemplates)
/// معین و طرف هر ردیف را تعیین می‌کند. همه متدها در تراکنش جاری (IUnitOfWork) اجرا می‌شوند.
/// </summary>
public interface IVoucherPosting
{
    /// <summary>صدور سند موقت؛ اگر سند منبع قبلاً سند داشته، همان سند با ردیف‌های تازه اصلاح می‌شود.</summary>
    Task<PostedVoucher> PostAsync(PostingRequest request, CancellationToken ct);

    /// <summary>حذف سند حسابداری سند منبع (سند دائم حذف نمی‌شود و خطا می‌دهد).</summary>
    Task UnpostAsync(string sourceModule, string docType, long documentId, CancellationToken ct);

    /// <summary>خطا اگر سند حسابداری این سند منبع دائم شده باشد (قبل از اصلاح یا حذف سند منبع).</summary>
    Task EnsureChangeableAsync(string sourceModule, string docType, long documentId, CancellationToken ct);

    /// <summary>سند حسابداری زنده چند سند منبع (شناسه سند منبع ← سند).</summary>
    Task<IReadOnlyDictionary<long, PostedVoucher>> FindAsync(string sourceModule, string docType,
        IReadOnlyCollection<long> documentIds, CancellationToken ct);
}

/// <summary>موجودیت خزانه‌داری که تفصیلی حسابداری دارد.</summary>
public enum TreasuryDetailKind : byte
{
    CashBox = 1,
    BankAccount = 2,
    Imprest = 3
}

/// <summary>
/// تفصیلی صندوق‌ها، حساب‌های بانکی و تنخواه‌گردان‌ها؛ پیاده‌سازی در حسابداری. تفصیلی در گروه همان نوع
/// (کلید سیستمی cash، bank، imprest) ساخته و با عنوان و وضعیت موجودیت هماهنگ می‌شود.
/// </summary>
public interface ITreasuryDetailLinks
{
    /// <summary>ساخت یا هماهنگ کردن تفصیلی؛ کد تفصیلی را برمی‌گرداند.</summary>
    Task<string> SyncAsync(TreasuryDetailKind kind, int id, string title, bool isActive, CancellationToken ct);

    Task<bool> IsUsedAsync(TreasuryDetailKind kind, int id, CancellationToken ct);

    Task DeleteAsync(TreasuryDetailKind kind, int id, CancellationToken ct);

    /// <summary>کد تفصیلی همه موجودیت‌های یک نوع (شناسه ← کد).</summary>
    Task<IReadOnlyDictionary<int, string>> GetCodesAsync(TreasuryDetailKind kind, CancellationToken ct);

    /// <summary>
    /// مانده هر صندوق/حساب بانکی/تنخواه (شناسه ← مانده بدهکار) در معین وجه نقد همان نوع، از سندهای موقت و دائم
    /// تا تاریخ داده‌شده (خالی یعنی همه).
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsync(TreasuryDetailKind kind, DateOnly? asOf, CancellationToken ct);
}
