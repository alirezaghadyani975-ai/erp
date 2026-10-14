using Erp.Costing.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Costing.Domain;

/// <summary>
/// عامل هزینه (cost.CostFactors): سطح ۱ تفصیلی معین‌های هزینه. گروهش (ماهیت: حقوق، کارکنان، استهلاک، ...)
/// تعیین می‌کند فقط در معین‌های کدام کل ثبت شود. رفتار ثابت/متغیر/نیمه‌متغیر برای تحلیل هزینه-حجم-سود و بودجه.
/// </summary>
public class CostFactor : AggregateRoot<int>
{
    private CostFactor() { } // EF

    public CostFactor(string title, CostNature nature, CostBehavior behavior, decimal? variablePercent, string? notes) =>
        Update(title, nature, behavior, variablePercent, notes);

    public string Title { get; private set; } = null!;
    public CostNature Nature { get; private set; }
    public CostBehavior Behavior { get; private set; }
    public decimal? VariablePercent { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public void Update(string title, CostNature nature, CostBehavior behavior, decimal? variablePercent, string? notes)
    {
        Title = Texts.Required(title, "عنوان عامل هزینه", 200);
        if (!Enum.IsDefined(nature))
            throw new DomainException("گروه هزینه معتبر نیست.");
        if (!Enum.IsDefined(behavior))
            throw new DomainException("رفتار هزینه معتبر نیست.");
        if (behavior == CostBehavior.Mixed)
        {
            if (variablePercent is not (> 0 and < 100))
                throw new DomainException("برای هزینه نیمه‌متغیر، درصد جزء متغیر بین ۱ تا ۹۹ لازم است.");
        }
        else variablePercent = null;

        Nature = nature;
        Behavior = behavior;
        VariablePercent = variablePercent is { } p ? Math.Round(p, 2) : null;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>نوع مرکز هزینه (cost.CostCenterTypes)؛ وظیفه‌اش تعیین می‌کند مرکز در کدام معین‌ها ثبت شود.</summary>
public class CostCenterType : Entity<short>
{
    private CostCenterType() { } // EF

    public CostCenterType(string title, CostFunction function, int sortOrder = 0) => Update(title, function, sortOrder);

    public string Title { get; private set; } = null!;
    public CostFunction Function { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsSystem { get; private set; }

    public void Update(string title, CostFunction function, int sortOrder)
    {
        Title = Texts.Required(title, "عنوان نوع مرکز", 100);
        if (!Enum.IsDefined(function))
            throw new DomainException("وظیفه مرکز معتبر نیست.");
        if (IsSystem && Id != 0 && function != Function)
            throw new DomainException("وظیفه نوع‌های سیستمی تغییر نمی‌کند.");
        Function = function;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = IsSystem ? throw new DomainException("نوع‌های سیستمی غیرفعال نمی‌شوند.") : false;
}

/// <summary>
/// مرکز هزینه (cost.CostCenters): سطح ۲ تفصیلی معین‌های هزینه.
/// برای بهای تمام‌شده: مراکز خدماتی و اداری با مبنا و ترتیب تسهیم به مراکز گیرنده تسهیم می‌شوند؛
/// مراکز تولیدی تسهیم نمی‌شوند و سربارشان با مبنای جذب به محصول می‌رود.
/// </summary>
public class CostCenter : AggregateRoot<int>
{
    private readonly List<CostCenterAllocation> _allocations = [];

    private CostCenter() { } // EF

    public CostCenter(string title, CostCenterType type) => SetType(title, type);

    public string Title { get; private set; } = null!;
    public short CenterTypeId { get; private set; }
    public int? ParentId { get; private set; }
    public int? ManagerPersonId { get; private set; }
    public int? AllocationBaseId { get; private set; }
    public short? AllocationOrder { get; private set; }
    public int? AbsorptionBaseId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public IReadOnlyCollection<CostCenterAllocation> Allocations => _allocations.AsReadOnly();

    public void SetType(string title, CostCenterType type)
    {
        Title = Texts.Required(title, "عنوان مرکز هزینه", 200);
        if (!type.IsActive && type.Id != CenterTypeId)
            throw new DomainException($"نوع مرکز «{type.Title}» غیرفعال است.");
        CenterTypeId = type.Id;
    }

    public void SetDetails(int? parentId, int? managerPersonId, string? notes)
    {
        if (parentId is int p && p == Id && Id != 0)
            throw new DomainException("مرکز نمی‌تواند زیرمجموعه خودش باشد.");
        ParentId = parentId;
        ManagerPersonId = managerPersonId;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    /// <summary>
    /// تنظیمات تسهیم و جذب بر اساس وظیفه مرکز. <paramref name="targets"/>: مرکز گیرنده و وظیفه‌اش با درصد سهم.
    /// </summary>
    public void SetCosting(CostFunction function, int? allocationBaseId, short? allocationOrder, int? absorptionBaseId,
        IReadOnlyList<(int CenterId, CostFunction Function, string Title, decimal Percent)> targets)
    {
        switch (function)
        {
            case CostFunction.Production:
                if (targets.Count > 0 || allocationBaseId is not null)
                    throw new DomainException("مرکز تولیدی تسهیم نمی‌شود؛ سربار آن با «مبنای جذب» به محصول می‌رود.");
                allocationOrder = null;
                break;
            case CostFunction.Selling:
                if (targets.Count > 0 || allocationBaseId is not null || absorptionBaseId is not null)
                    throw new DomainException("هزینه مراکز فروش و توزیع هزینه دوره است و تسهیم یا جذب نمی‌شود.");
                allocationOrder = null;
                break;
            default:
                if (absorptionBaseId is not null)
                    throw new DomainException("مبنای جذب سربار فقط برای مراکز تولیدی است.");
                break;
        }

        if (allocationOrder is < 1)
            throw new DomainException("ترتیب تسهیم عدد مثبت است (کوچک‌تر زودتر تسهیم می‌شود).");

        var seen = new HashSet<int>();
        foreach (var t in targets)
        {
            if (t.CenterId == Id && Id != 0)
                throw new DomainException("مرکز نمی‌تواند به خودش تسهیم شود.");
            if (!seen.Add(t.CenterId))
                throw new DomainException($"مرکز «{t.Title}» دو بار در گیرنده‌های تسهیم آمده است.");
            if (t.Percent is <= 0 or > 100)
                throw new DomainException($"سهم «{t.Title}» باید بیشتر از صفر و حداکثر ۱۰۰ درصد باشد.");
            if (function == CostFunction.Service && t.Function is not (CostFunction.Production or CostFunction.Service))
                throw new DomainException($"مرکز خدماتی فقط به مراکز تولیدی یا خدماتی تسهیم می‌شود («{t.Title}» مجاز نیست).");
            if (t.Function == CostFunction.Selling)
                throw new DomainException($"به مرکز فروش و توزیع («{t.Title}») تسهیم نمی‌شود.");
        }
        if (targets.Count > 0 && targets.Sum(t => t.Percent) != 100)
            throw new DomainException($"جمع سهم مراکز گیرنده باید ۱۰۰ درصد باشد (اکنون {targets.Sum(t => t.Percent):0.##}).");

        AllocationBaseId = allocationBaseId;
        AllocationOrder = allocationOrder;
        AbsorptionBaseId = absorptionBaseId;
        // ردیف موجود به‌روز، ردیف حذف‌شده پاک و ردیف جدید اضافه می‌شود (کلید ردیف همان مرکز گیرنده است)
        _allocations.RemoveAll(a => targets.All(t => t.CenterId != a.TargetCenterId));
        foreach (var t in targets)
        {
            var existing = _allocations.FirstOrDefault(a => a.TargetCenterId == t.CenterId);
            if (existing is null) _allocations.Add(new CostCenterAllocation(t.CenterId, Math.Round(t.Percent, 2)));
            else existing.SetPercent(Math.Round(t.Percent, 2));
        }
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>سهم پیش‌فرض یک مرکز گیرنده در تسهیم (cost.CostCenterAllocations).</summary>
public class CostCenterAllocation
{
    private CostCenterAllocation() { } // EF

    internal CostCenterAllocation(int targetCenterId, decimal percent)
    {
        TargetCenterId = targetCenterId;
        Percent = percent;
    }

    public int CenterId { get; private set; }
    public int TargetCenterId { get; private set; }
    public decimal Percent { get; private set; }

    internal void SetPercent(decimal percent) => Percent = percent;
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
