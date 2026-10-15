using Erp.SharedKernel.Text;
using Erp.BaseData.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.Details;

/// <summary>ماژول مالک یک نوع تفصیلی؛ تفصیلی مالک‌دار فقط از همان ماژول ساخته می‌شود.</summary>
public static class OwnerModules
{
    public const string Persons = "persons";
    public const string Treasury = "treasury";
    public const string Payroll = "payroll";
    public const string Assets = "assets";
    public const string Manual = "manual";
    public const string CostFactors = "cost_factors";
    public const string CostCenters = "cost_centers";
    public const string Inventory = "inventory";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Persons, Treasury, Payroll, Assets, Manual, CostFactors, CostCenters, Inventory };

    /// <summary>تفصیلی این گروه‌ها فقط از ماژول مالک ساخته و مدیریت می‌شود، نه دستی.</summary>
    public static bool IsManagedByModule(string ownerModule) =>
        ownerModule is Persons or Payroll or CostFactors or CostCenters;

    /// <summary>برای هر یک از این مالک‌ها (و هر نوع شخص) فقط یک گروه فعال مجاز است.</summary>
    public static bool IsSingleActive(string ownerModule) => IsManagedByModule(ownerModule);
}

/// <summary>کلید سیستمی گروه‌های تفصیلی (fin.DetailTypes.SystemKey).</summary>
public static class DetailSystemKeys
{
    public const string Cash = "cash";
    public const string Imprest = "imprest";
    public const string Bank = "bank";
    public const string LetterOfCredit = "lc";
    public const string Loan = "loan";
    public const string DueMonth = "due_month";

    /// <summary>تفصیلی این گروه‌ها فقط از خزانه‌داری (تعریف صندوق، حساب بانکی، تنخواه‌گردان) ساخته می‌شود.</summary>
    public static bool IsTreasuryManaged(string? key) => key is Cash or Imprest or Bank;
}

/// <summary>
/// نوع تفصیلی (fin.DetailTypes)، مثل «مشتریان حقیقی» یا «حساب جاری بانک‌ها».
/// هر نوع یک بازه کد هم‌طول دارد (مثلاً 01001 تا 02999) تا کد تفصیلی نقش را نشان دهد.
/// </summary>
public class DetailType : AggregateRoot<short>
{
    private DetailType() { } // EF

    public DetailType(string title, string ownerModule, string codeFrom, string codeTo, PersonKind? personKind = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان نوع تفصیلی الزامی است.");
        if (!OwnerModules.All.Contains(ownerModule))
            throw new DomainException($"ماژول مالک «{ownerModule}» معتبر نیست.");
        if (personKind is not null && ownerModule is not (OwnerModules.Persons or OwnerModules.Payroll))
            throw new DomainException("نوع شخص (حقیقی/حقوقی) فقط برای گروه‌های اشخاص و کارکنان معنا دارد.");
        if (ownerModule == OwnerModules.Payroll && personKind == Erp.BaseData.Contracts.PersonKind.Legal)
            throw new DomainException("کارکنان فقط شخص حقیقی‌اند.");
        (CodeFrom, CodeTo) = CheckRange(codeFrom, codeTo);

        Title = PersianText.Normalize(title);
        OwnerModule = ownerModule;
        PersonKind = personKind;
    }

    /// <summary>طول کد مجاز: ۲ تا ۱۰ رقم (ستون Code در دیتابیس ۱۰ نویسه است).</summary>
    public const int MinCodeLength = 2;
    public const int MaxCodeLength = 10;

    private static (string From, string To) CheckRange(string codeFrom, string codeTo)
    {
        codeFrom = PersianText.Normalize(codeFrom ?? "");
        codeTo = PersianText.Normalize(codeTo ?? "");
        if (codeFrom.Length == 0 || codeFrom.Length != codeTo.Length
            || !codeFrom.All(char.IsAsciiDigit) || !codeTo.All(char.IsAsciiDigit)
            || string.CompareOrdinal(codeFrom, codeTo) > 0)
            throw new DomainException("بازه کد باید عددی، هم‌طول و از کوچک به بزرگ باشد (مثل 01001 تا 02999).");
        if (codeFrom.Length is < MinCodeLength or > MaxCodeLength)
            throw new DomainException($"طول کد تفصیلی باید بین {MinCodeLength} تا {MaxCodeLength} رقم باشد.");
        return (codeFrom, codeTo);
    }

    /// <summary>ظرفیت بازه (تعداد کدهای ممکن).</summary>
    public long Capacity => long.Parse(CodeTo) - long.Parse(CodeFrom) + 1;

    /// <summary>
    /// تغییر بازه. اگر تفصیلی دارد، طول کد ثابت می‌ماند و کوچک‌ترین/بزرگ‌ترین کد موجود باید داخل بازه جدید باشد.
    /// </summary>
    public void ChangeRange(string codeFrom, string codeTo, string? minUsed, string? maxUsed)
    {
        var (from, to) = CheckRange(codeFrom, codeTo);
        if (minUsed is not null)
        {
            if (from.Length != CodeLength)
                throw new DomainException("این گروه تفصیلی دارد؛ طول کد را نمی‌توان عوض کرد.");
            if (string.CompareOrdinal(minUsed, from) < 0 || string.CompareOrdinal(maxUsed, to) > 0)
                throw new DomainException($"کدهای موجود ({minUsed} تا {maxUsed}) باید داخل بازه جدید بمانند.");
        }
        CodeFrom = from;
        CodeTo = to;
    }

    public string Title { get; private set; } = null!;
    public string OwnerModule { get; private set; } = null!;
    public PersonKind? PersonKind { get; private set; }
    public string CodeFrom { get; private set; } = null!;
    public string CodeTo { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    /// <summary>کلید سیستمی گروه‌هایی که زیرسیستم‌ها پیدایشان می‌کنند (cash، bank، imprest، due_month، ...).</summary>
    public string? SystemKey { get; private set; }

    public int CodeLength => CodeFrom.Length;

    public bool IsInRange(string code) =>
        code.Length == CodeLength && string.CompareOrdinal(code, CodeFrom) >= 0 && string.CompareOrdinal(code, CodeTo) <= 0;

    public bool OverlapsWith(string from, string to) =>
        from.Length == CodeLength && string.CompareOrdinal(from, CodeTo) <= 0 && string.CompareOrdinal(CodeFrom, to) <= 0;

    /// <summary>کد بعدی بازه، با صفرهای ابتدای کد.</summary>
    public string NextCode(string? lastUsed)
    {
        var next = lastUsed is null ? long.Parse(CodeFrom) : long.Parse(lastUsed) + 1;
        var code = next.ToString().PadLeft(CodeLength, '0');
        if (!IsInRange(code))
            throw new DomainException($"بازه کد «{Title}» ({CodeFrom} تا {CodeTo}) پر شده است.");
        return code;
    }

    /// <summary>تغییر ماژول مالک یا نوع شخص؛ فقط وقتی گروه هنوز تفصیلی ندارد.</summary>
    public void ChangeOwner(string ownerModule, PersonKind? personKind, bool hasDetails)
    {
        if (ownerModule == OwnerModule && personKind == PersonKind) return;
        if (hasDetails)
            throw new DomainException("این گروه تفصیلی دارد؛ مالک و نوع شخص آن عوض نمی‌شود.");
        if (!OwnerModules.All.Contains(ownerModule))
            throw new DomainException($"ماژول مالک «{ownerModule}» معتبر نیست.");
        if (personKind is not null && ownerModule is not (OwnerModules.Persons or OwnerModules.Payroll))
            throw new DomainException("نوع شخص (حقیقی/حقوقی) فقط برای گروه‌های اشخاص و کارکنان معنا دارد.");
        OwnerModule = ownerModule;
        PersonKind = personKind;
    }

    public void Rename(string title) =>
        Title = string.IsNullOrWhiteSpace(title) ? throw new DomainException("عنوان الزامی است.") : PersianText.Normalize(title);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
