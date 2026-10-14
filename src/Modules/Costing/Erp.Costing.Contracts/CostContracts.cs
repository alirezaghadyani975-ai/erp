namespace Erp.Costing.Contracts;

/// <summary>ماهیت هزینه؛ هر ماهیت یک کل در گروه هزینه‌هاست (fin.Accounts.CostNature).</summary>
public enum CostNature : byte
{
    Payroll = 1,         // حقوق و دستمزد
    Depreciation = 2,    // استهلاک
    OtherOperating = 3,  // سایر هزینه‌های عملیاتی
    Finance = 4          // هزینه‌های مالی (تأمین مالی)
}

/// <summary>وظیفه هزینه؛ هر وظیفه یک معین زیر کل ماهیت است و نوع مرکز هزینه هم یکی از آن‌هاست.</summary>
public enum CostFunction : byte
{
    Production = 1,  // تولیدی
    Service = 2,     // خدماتی (پشتیبانی تولید)
    Admin = 3,       // اداری
    Selling = 4      // فروش و توزیع
}

/// <summary>رفتار هزینه نسبت به حجم فعالیت.</summary>
public enum CostBehavior : byte
{
    Fixed = 1,     // ثابت
    Variable = 2,  // متغیر
    Mixed = 3      // نیمه‌متغیر (با درصد جزء متغیر)
}

public enum CostDetailKind : byte
{
    Factor = 1,
    Center = 2
}

/// <summary>داده‌ای از عامل یا مرکز هزینه که تفصیلی حسابداری‌اش لازم دارد.</summary>
public sealed record CostDetailSnapshot(CostDetailKind Kind, int Id, string Title, bool IsActive,
    CostNature? Nature, CostFunction? Function);

/// <summary>
/// تفصیلی عوامل و مراکز هزینه را ماژول حسابداری پیاده می‌کند (ساخت، تغییر نام، فعال/غیرفعال، حذف).
/// ماهیت عامل و وظیفه مرکز روی تفصیلی نگه داشته می‌شود تا در ثبت سند کنترل شود.
/// </summary>
public interface ICostDetailLinks
{
    Task SyncAsync(CostDetailSnapshot item, CancellationToken ct);

    /// <summary>آیا تفصیلی این عامل/مرکز در سند حسابداری آمده است.</summary>
    Task<bool> IsUsedAsync(CostDetailKind kind, int id, CancellationToken ct);

    Task DeleteAsync(CostDetailKind kind, int id, CancellationToken ct);

    /// <summary>کد تفصیلی عامل‌ها یا مراکز (شناسه ← کد)؛ ids خالی یعنی همه.</summary>
    Task<IReadOnlyDictionary<int, string>> GetCodesAsync(CostDetailKind kind, IReadOnlyCollection<int>? ids, CancellationToken ct);
}
