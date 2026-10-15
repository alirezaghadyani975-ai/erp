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
    /// <summary>خود حساب تفصیلی (fin.DetailAccounts)، مثلاً صندوق یا حساب بانکی.</summary>
    DetailAccount = 9
}

public sealed record PostingDetail(PostingDetailKind Kind, long Id);

/// <summary>
/// یک ردیف سند منبع با کلید ردیف الگو (مثلاً expense یا vat). مبلغ منفی در طرف مقابل الگو ثبت می‌شود؛
/// مبلغ صفر نادیده گرفته می‌شود. تفصیلی‌ها به ترتیب سطح‌های قاعده معین خودکار جا می‌گیرند.
/// </summary>
public sealed record PostingLine(string LineKey, decimal Amount, IReadOnlyList<PostingDetail> Details, string? Description = null);

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
