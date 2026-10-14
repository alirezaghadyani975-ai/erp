using Erp.SharedKernel.Text;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.BaseData;

// داده‌های پایه مشترک (schema: base). فعلاً این‌جا هستند؛
// با ساخته شدن ماژول اشخاص، Person به آن ماژول منتقل می‌شود.

public enum PersonKind : byte
{
    Natural = 1, // حقیقی
    Legal = 2    // حقوقی
}

/// <summary>شخص حقیقی یا حقوقی (base.Persons) — نسخه حداقلی.</summary>
public class Person : AggregateRoot<int>
{
    private Person() { } // EF

    public Person(PersonKind kind, string displayName, string? nationalId = null, string? economicCode = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("نام شخص الزامی است.");
        nationalId = PersianText.NormalizeOrNull(nationalId);
        if (nationalId is not null && !nationalId.All(char.IsAsciiDigit))
            throw new DomainException("کد ملی یا شناسه ملی فقط می‌تواند شامل رقم باشد.");

        Kind = kind;
        DisplayName = PersianText.Normalize(displayName);
        NationalId = nationalId;
        EconomicCode = string.IsNullOrWhiteSpace(economicCode) ? null : economicCode.Trim();
    }

    public PersonKind Kind { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string? NationalId { get; private set; }
    public string? EconomicCode { get; private set; }
    public bool IsActive { get; private set; } = true;
}

/// <summary>ارز (base.Currencies). ارز پایه مبنای مبالغ بدهکار/بستانکار سند است.</summary>
public class Currency : Entity<short>
{
    private Currency() { } // EF
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public byte DecimalPlaces { get; private set; }
    public bool IsBase { get; private set; }
    public bool IsActive { get; private set; }
}

/// <summary>واحد سنجش (base.Units).</summary>
public class Unit : Entity<short>
{
    private Unit() { } // EF
    public string Title { get; private set; } = null!;
    public bool IsActive { get; private set; }
}
