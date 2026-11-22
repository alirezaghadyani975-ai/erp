namespace Erp.SharedKernel.Security;

/// <summary>
/// یک مجوز قابل اعطا (مثلاً trs.payment.create). هر ماژول فهرست مجوزهای خودش را با <see cref="IPermissionSource"/>
/// معرفی می‌کند؛ فرم نقش‌ها درخت مجوز را از همین‌ها می‌سازد.
/// </summary>
/// <param name="Key">کلید ثابت (در دیتابیس مرکزی ذخیره می‌شود؛ تغییرش نقش‌های قبلی را بی‌اثر می‌کند).</param>
/// <param name="Module">عنوان ماژول (حسابداری، خزانه‌داری، ...).</param>
/// <param name="Group">عنوان فرم یا بخش (پرداخت، اسناد حسابداری، ...).</param>
/// <param name="Action">عنوان کار (مشاهده، ایجاد، اصلاح، حذف، ...).</param>
public sealed record PermissionDef(string Key, string Module, string Group, string Action)
{
    public string Title => $"{Group} - {Action}";
}

public interface IPermissionSource
{
    IEnumerable<PermissionDef> Permissions { get; }
}

/// <summary>ساخت فهرست مجوزهای یک ماژول با کارهای استاندارد.</summary>
public sealed class PermissionList(string module) : IPermissionSource
{
    private readonly List<PermissionDef> _items = [];

    public IEnumerable<PermissionDef> Permissions => _items;

    /// <summary>مشاهده، ایجاد، اصلاح و حذف (کلیدهای prefix.view/create/edit/delete).</summary>
    public PermissionList Crud(string prefix, string group) =>
        Add(prefix, group, ("view", "مشاهده"), ("create", "ایجاد"), ("edit", "اصلاح"), ("delete", "حذف"));

    public PermissionList Add(string prefix, string group, params (string Action, string Title)[] actions)
    {
        foreach (var (action, title) in actions)
            _items.Add(new PermissionDef($"{prefix}.{action}", module, group, title));
        return this;
    }
}

/// <summary>نوع محدوده داده: کاربر فقط صندوق/بانک/تنخواه/سال‌های مالی مشخص را می‌بیند.</summary>
public enum ScopeKind : byte
{
    CashBox = 1,
    BankAccount = 2,
    ImprestFund = 3,
    FiscalYear = 4,
    Warehouse = 5,
}

/// <summary>
/// کنترل دسترسی کاربر جاری در شرکت جاری. سرویس‌های Application پیش از هر کار با <see cref="Demand"/> مجوز را
/// کنترل می‌کنند؛ رابط کاربری با <see cref="Has"/> منو و دکمه‌ها را پنهان می‌کند (کنترل اصلی همان سرویس است).
/// </summary>
public interface IAccessControl
{
    bool IsSystemAdmin { get; }

    bool Has(string permission);

    /// <summary>اگر کاربر مجوز را نداشته باشد <see cref="ForbiddenException"/> با پیام فارسی.</summary>
    void Demand(string permission);

    /// <summary>شناسه‌های مجاز یک نوع؛ null یعنی همه.</summary>
    IReadOnlySet<int>? Allowed(ScopeKind kind);

    /// <summary>کاربر فقط فرم‌ها و اسنادی را می‌بیند که خودش ثبت کرده.</summary>
    bool OwnDocsOnly { get; }

    int UserId { get; }
}

public static class AccessControlExtensions
{
    public static bool CanUse(this IAccessControl access, ScopeKind kind, int id) =>
        access.Allowed(kind) is not { } set || set.Contains(id);

    /// <summary>کنترل محدوده داده؛ title برای پیام (مثلاً «صندوق مرکزی»).</summary>
    public static void DemandScope(this IAccessControl access, ScopeKind kind, int id, string? title = null)
    {
        if (access.CanUse(kind, id)) return;
        var what = kind switch
        {
            ScopeKind.CashBox => "صندوق",
            ScopeKind.BankAccount => "حساب بانکی",
            ScopeKind.ImprestFund => "تنخواه‌گردان",
            ScopeKind.Warehouse => "انبار",
            _ => "سال مالی",
        };
        throw new ForbiddenException(title is null
            ? $"شما به این {what} دسترسی ندارید."
            : $"شما به {what} «{title}» دسترسی ندارید.");
    }

    /// <summary>کاربر «فقط اسناد خودش» فرم دیگران را نمی‌بیند و تغییر نمی‌دهد.</summary>
    public static void DemandOwner(this IAccessControl access, int createdBy)
    {
        if (access.OwnDocsOnly && createdBy != access.UserId)
            throw new ForbiddenException("این فرم را کاربر دیگری ثبت کرده است و شما فقط به فرم‌های خودتان دسترسی دارید.");
    }

    /// <summary>اولین مجوزِ موجود از چند مجوز (مثلاً ایجاد یا اصلاح).</summary>
    public static void DemandAny(this IAccessControl access, params string[] permissions)
    {
        if (permissions.Any(access.Has)) return;
        access.Demand(permissions[0]);
    }
}

/// <summary>کاربر مجوز این کار را ندارد (HTTP 403). پیام فارسی به کاربر نمایش داده می‌شود.</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>دسترسی کامل؛ برای تست‌ها و کارهای سیستمی (بدون کاربر).</summary>
public sealed class FullAccess(int userId = 1) : IAccessControl
{
    public bool IsSystemAdmin => true;
    public bool Has(string permission) => true;
    public void Demand(string permission) { }
    public IReadOnlySet<int>? Allowed(ScopeKind kind) => null;
    public bool OwnDocsOnly => false;
    public int UserId => userId;
}
