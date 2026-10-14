using Erp.SharedKernel.Text;
using Erp.Accounting.Domain.BaseData;
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

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Persons, Treasury, Payroll, Assets, Manual };
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
        codeFrom = codeFrom.Trim();
        codeTo = codeTo.Trim();
        if (codeFrom.Length == 0 || codeFrom.Length != codeTo.Length
            || !codeFrom.All(char.IsAsciiDigit) || !codeTo.All(char.IsAsciiDigit)
            || string.CompareOrdinal(codeFrom, codeTo) > 0)
            throw new DomainException("بازه کد باید عددی، هم‌طول و از کوچک به بزرگ باشد (مثل 01001 تا 02999).");

        Title = PersianText.Normalize(title);
        OwnerModule = ownerModule;
        CodeFrom = codeFrom;
        CodeTo = codeTo;
        PersonKind = personKind;
    }

    public string Title { get; private set; } = null!;
    public string OwnerModule { get; private set; } = null!;
    public PersonKind? PersonKind { get; private set; }
    public string CodeFrom { get; private set; } = null!;
    public string CodeTo { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

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

    public void Rename(string title) =>
        Title = string.IsNullOrWhiteSpace(title) ? throw new DomainException("عنوان الزامی است.") : PersianText.Normalize(title);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
