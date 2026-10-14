using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.BaseData;

// داده‌های پایه مشترک (schema: base). شخص در Person.cs است.

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
