using Erp.Inventory.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Inventory.Domain;

/// <summary>
/// گروه کالا (inv.ItemGroups): معین‌های فروش، برگشت از فروش و خرید/موجودی کالاهای گروه را تعیین می‌کند.
/// گروه خدمات معین خرید ندارد (خرید خدمت با شناسایی هزینه ثبت می‌شود).
/// </summary>
public class ItemGroup : AggregateRoot<int>
{
    private ItemGroup() { } // EF

    public ItemGroup(string title, ItemKind kind, int salesAccountId, int salesReturnAccountId, int? purchaseAccountId, int? parentId,
        int? costAccountId = null) =>
        Update(title, kind, salesAccountId, salesReturnAccountId, purchaseAccountId, parentId, costAccountId);

    public int? ParentId { get; private set; }
    public string Title { get; private set; } = null!;
    public ItemKind Kind { get; private set; }
    public int SalesAccountId { get; private set; }
    public int SalesReturnAccountId { get; private set; }
    public int? PurchaseAccountId { get; private set; }
    /// <summary>معین بهای تمام‌شده کالای فروش‌رفته (فقط گروه کالا).</summary>
    public int? CostAccountId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string title, ItemKind kind, int salesAccountId, int salesReturnAccountId, int? purchaseAccountId, int? parentId,
        int? costAccountId = null)
    {
        Title = Texts.Required(title, "عنوان گروه کالا", 100);
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع گروه (کالا یا خدمت) معتبر نیست.");
        if (salesAccountId <= 0) throw new DomainException("معین فروش گروه الزامی است.");
        if (salesReturnAccountId <= 0) throw new DomainException("معین برگشت از فروش گروه الزامی است.");
        if (kind == ItemKind.Goods && purchaseAccountId is not > 0)
            throw new DomainException("معین خرید (موجودی) برای گروه کالا الزامی است.");
        if (kind == ItemKind.Goods && costAccountId is not > 0)
            throw new DomainException("معین بهای تمام‌شده برای گروه کالا الزامی است.");
        if (parentId is { } p && p == Id && Id != 0) throw new DomainException("گروه نمی‌تواند زیرگروه خودش باشد.");

        Kind = kind;
        SalesAccountId = salesAccountId;
        SalesReturnAccountId = salesReturnAccountId;
        PurchaseAccountId = kind == ItemKind.Goods ? purchaseAccountId : null;
        CostAccountId = kind == ItemKind.Goods ? costAccountId : null;
        ParentId = parentId;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>
/// کالا یا خدمت (inv.Items). نوعش همان نوع گروه است و معین‌ها از گروه می‌آیند.
/// شناسه کالا/خدمت سامانه مودیان ۱۳ رقم است و برای ارسال صورتحساب فروش لازم می‌شود.
/// </summary>
public class Item : AggregateRoot<int>
{
    private Item() { } // EF

    public Item(string code, string title, ItemGroup group, short unitId, string? taxItemId, bool vatExempt, string? barcode,
        decimal? salesPrice, string? notes, DateTime nowUtc)
    {
        CreatedAt = nowUtc;
        Update(code, title, group, unitId, taxItemId, vatExempt, barcode, salesPrice, notes);
    }

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public ItemKind Kind { get; private set; }
    public int GroupId { get; private set; }
    public short UnitId { get; private set; }
    public string? TaxItemId { get; private set; }
    public bool VatExempt { get; private set; }
    public string? Barcode { get; private set; }
    public decimal? SalesPrice { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void Update(string code, string title, ItemGroup group, short unitId, string? taxItemId, bool vatExempt, string? barcode,
        decimal? salesPrice, string? notes)
    {
        Code = Texts.Required(code, "کد کالا", 30);
        Title = Texts.Required(title, "عنوان کالا", 200);
        if (unitId <= 0) throw new DomainException("واحد سنجش الزامی است.");
        if (salesPrice is < 0) throw new DomainException("قیمت فروش نمی‌تواند منفی باشد.");

        var tax = PersianText.NormalizeOrNull(taxItemId)?.Replace(" ", "");
        if (tax is not null && (tax.Length != 13 || !tax.All(char.IsAsciiDigit)))
            throw new DomainException("شناسه کالا/خدمت سامانه مودیان باید ۱۳ رقم باشد.");

        GroupId = group.Id;
        Kind = group.Kind;
        UnitId = unitId;
        TaxItemId = tax;
        VatExempt = vatExempt;
        Barcode = PersianText.NormalizeOrNull(barcode)?.Replace(" ", "") is { } b
            ? (b.Length <= 50 ? b : throw new DomainException("بارکد حداکثر ۵۰ نویسه است."))
            : null;
        SalesPrice = salesPrice is { } p ? Math.Round(p, 4) : null;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

internal static class Texts
{
    public static string Required(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");
        return v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }

    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}
