using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.BaseData.Domain.Lookups;

/// <summary>کد فهرست‌های سیستمی جدول پایه عمومی.</summary>
public static class LookupCodes
{
    public const string Country = "country";
    public const string Province = "province";
    public const string City = "city";
    public const string AddressType = "address_type";
    public const string PersonGroup = "person_group";
    public const string ContactRole = "contact_role";
}

/// <summary>
/// یک فهرست جدول پایه (base.LookupCategories). اقلام یا زیر اقلام فهرست دیگری‌اند (شهر ← استان)
/// یا در همان فهرست درختی‌اند (گروه اشخاص).
/// </summary>
public class LookupCategory : Entity<short>
{
    private LookupCategory() { } // EF

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public short? ParentCategoryId { get; private set; }
    public bool IsTree { get; private set; }
    public bool IsSystem { get; private set; }
    public string? Description { get; private set; }
    public short SortOrder { get; private set; }
}

/// <summary>قلم جدول پایه (base.LookupItems).</summary>
public class LookupItem : Entity<int>
{
    private LookupItem() { } // EF

    public LookupItem(LookupCategory category, string title, string? code, LookupItem? parent, int sortOrder = 0)
    {
        CategoryId = category.Id;
        Update(category, title, code, parent, sortOrder);
    }

    public short CategoryId { get; private set; }
    public string? Code { get; private set; }
    public string Title { get; private set; } = null!;
    public int? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsSystem { get; private set; }

    public void Update(LookupCategory category, string title, string? code, LookupItem? parent, int sortOrder)
    {
        Title = PersianText.NormalizeOrNull(title) ?? throw new DomainException("عنوان الزامی است.");
        Code = PersianText.NormalizeOrNull(code);
        if (Title.Length > 150 || Code?.Length > 20)
            throw new DomainException("عنوان حداکثر ۱۵۰ و کد حداکثر ۲۰ نویسه است.");

        if (parent is not null)
        {
            var ok = category.IsTree ? parent.CategoryId == category.Id
                : category.ParentCategoryId is short pc && parent.CategoryId == pc;
            if (!ok) throw new DomainException("والد انتخاب‌شده از فهرست درستی نیست.");
            if (parent.Id != 0 && parent.Id == Id) throw new DomainException("قلم نمی‌تواند والد خودش باشد.");
        }
        // قلم بدون والد مجاز است (مثلاً شهر خارج از کشور بدون استان)

        ParentId = parent?.Id;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
