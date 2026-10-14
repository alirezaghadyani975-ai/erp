namespace Erp.BaseData.Contracts;

public enum PersonKind : byte
{
    Natural = 1, // حقیقی
    Legal = 2    // حقوقی
}

/// <summary>کد نقش‌های سیستمی (base.PersonRoleTypes.Code).</summary>
public static class RoleCodes
{
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Contractor = "contractor";
    public const string Shareholder = "shareholder";
    public const string Affiliate = "affiliate";
    public const string Employee = "employee";
    public const string Other = "other";
}

/// <summary>آنچه ماژول‌های دیگر از یک شخص لازم دارند.</summary>
/// <param name="HasPartyRole">حداقل یک نقش طرف‌حسابی (غیر از کارمند) دارد.</param>
/// <param name="HasEmployeeRole">نقش کارمند دارد.</param>
public sealed record PersonSnapshot(int Id, PersonKind Kind, string DisplayName, bool IsActive,
    bool HasPartyRole, bool HasEmployeeRole);

/// <summary>یک رکورد ماژول دیگر که به شخص وصل است (مثلاً تفصیلی حسابداری).</summary>
public sealed record PersonLinkInfo(int PersonId, string Module, string Kind, string Code, string Title, string GroupTitle);

/// <summary>
/// ماژول‌هایی که به اشخاص رکورد وصل می‌کنند (حسابداری: تفصیلی‌ها) این را پیاده می‌کنند.
/// ماژول اطلاعات پایه بعد از ذخیره یا قبل از حذف شخص، داخل همان تراکنش صدایشان می‌زند.
/// </summary>
public interface IPersonLinks
{
    /// <summary>هماهنگ کردن رکوردهای وابسته با شخص (ساخت، تغییر نام، فعال/غیرفعال).</summary>
    Task SyncAsync(PersonSnapshot person, CancellationToken ct);

    /// <summary>دلیل فارسی اینکه شخص حذف‌شدنی نیست، یا null.</summary>
    Task<string?> CannotDeleteReasonAsync(int personId, CancellationToken ct);

    /// <summary>حذف رکوردهای وابسته (قبل از حذف خود شخص).</summary>
    Task DeleteAsync(int personId, CancellationToken ct);

    Task<IReadOnlyList<PersonLinkInfo>> GetAsync(IReadOnlyCollection<int> personIds, CancellationToken ct);

    /// <summary>شخص‌هایی که کد رکورد وابسته‌شان با این عبارت شروع می‌شود (جستجو با کد تفصیلی).</summary>
    Task<IReadOnlyList<int>> FindByCodeAsync(string codePrefix, CancellationToken ct);
}
