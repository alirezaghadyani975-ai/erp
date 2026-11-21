using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Production.Domain;

/// <summary>نوع ضایعات: فقط سهم مواد، یا مواد به‌علاوه سهم دستمزد و سربار.</summary>
public enum ScrapType : byte
{
    MaterialOnly = 1,
    Full = 2
}

public sealed record BomMaterialInput(int ItemId, decimal Quantity, int? WarehouseId, decimal LossPercent, string? Notes,
    IReadOnlyList<BomSubstituteInput> Substitutes);

/// <param name="OptionNo">شماره گزینه جایگزین (اولویت)؛ ردیف‌های هم‌شماره با هم یک ترکیب‌اند.</param>
public sealed record BomSubstituteInput(byte OptionNo, int ItemId, decimal Factor);

public sealed record BomScrapInput(int ItemId, decimal Quantity, ScrapType ScrapType);

public sealed record BomOperationInput(int CostCenterId, decimal ManSeconds, decimal MachineSeconds, string? Notes);

/// <summary>
/// فرمول ساخت (prd.Boms): مواد، جایگزین‌ها، ضایعات و زمان استاندارد مراکز برای «مقدار پایه» محصول.
/// مصرف استاندارد برای Q واحد = مقدار ردیف × Q ÷ مقدار پایه × (۱ + درصد افت ÷ ۱۰۰).
/// </summary>
public class Bom : AggregateRoot<int>
{
    private readonly List<BomMaterial> _materials = [];
    private readonly List<BomScrap> _scraps = [];
    private readonly List<BomOperation> _operations = [];

    private Bom() { } // EF

    public Bom(int productId, DateTime nowUtc)
    {
        if (productId <= 0) throw new DomainException("محصول فرمول الزامی است.");
        ProductId = productId;
        CreatedAt = nowUtc;
    }

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public int ProductId { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public int OutputWarehouseId { get; private set; }
    public int IssueWarehouseId { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<BomMaterial> Materials => _materials.AsReadOnly();
    public IReadOnlyList<BomScrap> Scraps => _scraps.AsReadOnly();
    public IReadOnlyList<BomOperation> Operations => _operations.AsReadOnly();

    public void Update(string code, string title, int productId, decimal baseQuantity, int outputWarehouseId, int issueWarehouseId,
        DateOnly? validFrom, DateOnly? validTo, bool isDefault, bool isActive, string? notes)
    {
        Code = Required(code, "کد فرمول", 30);
        Title = Required(title, "عنوان فرمول", 150);
        if (productId <= 0) throw new DomainException("محصول فرمول الزامی است.");
        if (baseQuantity <= 0) throw new DomainException("مقدار پایه فرمول باید بیشتر از صفر باشد.");
        if (outputWarehouseId <= 0) throw new DomainException("انبار رسید محصول الزامی است.");
        if (issueWarehouseId <= 0) throw new DomainException("انبار برداشت مواد الزامی است.");
        if (validFrom is { } f && validTo is { } t && t < f) throw new DomainException("تاریخ پایان اعتبار پیش از شروع آن است.");
        if (isDefault && !isActive) throw new DomainException("فرمول غیرفعال پیش‌فرض محصول نمی‌شود.");
        var n = PersianText.NormalizeOrNull(notes);
        if (n?.Length > 500) throw new DomainException("توضیحات حداکثر ۵۰۰ نویسه است.");
        (ProductId, BaseQuantity, OutputWarehouseId, IssueWarehouseId, ValidFrom, ValidTo, IsDefault, IsActive, Notes) =
            (productId, Math.Round(baseQuantity, 6), outputWarehouseId, issueWarehouseId, validFrom, validTo, isDefault, isActive, n);
    }

    public void ClearDefault() => IsDefault = false;

    public bool IsValidOn(DateOnly date) => IsActive && (ValidFrom is null || ValidFrom <= date) && (ValidTo is null || ValidTo >= date);

    public void SetMaterials(IReadOnlyList<BomMaterialInput> inputs)
    {
        if (inputs.Count == 0) throw new DomainException("فرمول ساخت حداقل یک ردیف مواد دارد.");
        var rows = new List<BomMaterial>();
        short row = 0;
        foreach (var m in inputs)
        {
            row++;
            if (m.ItemId <= 0) throw new DomainException($"ردیف مواد {row}: ماده انتخاب نشده است.");
            if (m.ItemId == ProductId) throw new DomainException($"ردیف مواد {row}: محصول در مواد فرمول خودش نمی‌آید.");
            if (m.Quantity <= 0) throw new DomainException($"ردیف مواد {row}: مقدار باید بیشتر از صفر باشد.");
            if (m.LossPercent is < 0 or >= 100) throw new DomainException($"ردیف مواد {row}: درصد افت باید بین ۰ و کمتر از ۱۰۰ باشد.");
            var subs = new List<BomSubstitute>();
            foreach (var s in m.Substitutes)
            {
                if (s.ItemId <= 0 || s.Factor <= 0) throw new DomainException($"ردیف مواد {row}: ماده و ضریب جایگزین را کامل کنید.");
                if (s.ItemId == m.ItemId) throw new DomainException($"ردیف مواد {row}: ماده جایگزین خود ماده اصلی است.");
                if (s.ItemId == ProductId) throw new DomainException($"ردیف مواد {row}: محصول جایگزین ماده خودش نمی‌شود.");
                if (s.OptionNo is < 1 or > 20) throw new DomainException($"ردیف مواد {row}: شماره گزینه جایگزین باید بین ۱ و ۲۰ باشد.");
                if (subs.Any(x => x.OptionNo == s.OptionNo && x.ItemId == s.ItemId))
                    throw new DomainException($"ردیف مواد {row}: یک ماده دو بار در یک گزینه جایگزین آمده است.");
                subs.Add(new BomSubstitute(s.OptionNo, s.ItemId, Math.Round(s.Factor, 6)));
            }
            rows.Add(new BomMaterial(row, m.ItemId, Math.Round(m.Quantity, 6), m.WarehouseId is > 0 ? m.WarehouseId : null,
                Math.Round(m.LossPercent, 2), PersianText.NormalizeOrNull(m.Notes), subs));
        }
        _materials.Clear();
        _materials.AddRange(rows);
    }

    public void SetScraps(IReadOnlyList<BomScrapInput> inputs)
    {
        var rows = new List<BomScrap>();
        short row = 0;
        foreach (var s in inputs)
        {
            row++;
            if (s.ItemId <= 0) throw new DomainException($"ردیف ضایعات {row}: کالای ضایعات انتخاب نشده است.");
            if (s.Quantity <= 0) throw new DomainException($"ردیف ضایعات {row}: مقدار باید بیشتر از صفر باشد.");
            if (!Enum.IsDefined(s.ScrapType)) throw new DomainException($"ردیف ضایعات {row}: نوع ضایعات معتبر نیست.");
            rows.Add(new BomScrap(row, s.ItemId, Math.Round(s.Quantity, 6), s.ScrapType));
        }
        _scraps.Clear();
        _scraps.AddRange(rows);
    }

    public void SetOperations(IReadOnlyList<BomOperationInput> inputs)
    {
        var rows = new List<BomOperation>();
        short row = 0;
        foreach (var o in inputs)
        {
            row++;
            if (o.CostCenterId <= 0) throw new DomainException($"ردیف زمان {row}: مرکز هزینه انتخاب نشده است.");
            if (o.ManSeconds < 0 || o.MachineSeconds < 0) throw new DomainException($"ردیف زمان {row}: زمان منفی نمی‌شود.");
            if (o.ManSeconds + o.MachineSeconds == 0) throw new DomainException($"ردیف زمان {row}: نفر-ساعت یا ماشین-ساعت را وارد کنید.");
            if (rows.Any(r => r.CostCenterId == o.CostCenterId)) throw new DomainException($"ردیف زمان {row}: یک مرکز دو بار آمده است.");
            rows.Add(new BomOperation(row, o.CostCenterId, Math.Round(o.ManSeconds, 2), Math.Round(o.MachineSeconds, 2), PersianText.NormalizeOrNull(o.Notes)));
        }
        _operations.Clear();
        _operations.AddRange(rows);
    }

    /// <summary>ضریب مقدار تولید نسبت به مقدار پایه.</summary>
    public decimal Ratio(decimal quantity) => quantity / BaseQuantity;

    private static string Required(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");
        return v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}

public class BomMaterial
{
    private readonly List<BomSubstitute> _substitutes = [];

    private BomMaterial() { } // EF

    internal BomMaterial(short rowNo, int itemId, decimal quantity, int? warehouseId, decimal lossPercent, string? notes, List<BomSubstitute> substitutes)
    {
        (RowNo, ItemId, Quantity, WarehouseId, LossPercent, Notes) = (rowNo, itemId, quantity, warehouseId, lossPercent, notes);
        _substitutes.AddRange(substitutes);
    }

    public int Id { get; private set; }
    public int BomId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public int? WarehouseId { get; private set; }
    public decimal LossPercent { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyList<BomSubstitute> Substitutes => _substitutes.AsReadOnly();

    /// <summary>مصرف استاندارد برای ضریب تولید (با افت).</summary>
    public decimal StandardFor(decimal ratio) => Math.Round(Quantity * ratio * (1 + LossPercent / 100), 6);
}

public class BomSubstitute
{
    private BomSubstitute() { } // EF

    internal BomSubstitute(byte optionNo, int itemId, decimal factor) => (OptionNo, ItemId, Factor) = (optionNo, itemId, factor);

    public int Id { get; private set; }
    public int MaterialId { get; private set; }
    public byte OptionNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal Factor { get; private set; }
}

public class BomScrap
{
    private BomScrap() { } // EF

    internal BomScrap(short rowNo, int itemId, decimal quantity, ScrapType scrapType) => (RowNo, ItemId, Quantity, ScrapType) = (rowNo, itemId, quantity, scrapType);

    public int Id { get; private set; }
    public int BomId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public ScrapType ScrapType { get; private set; }
}

public class BomOperation
{
    private BomOperation() { } // EF

    internal BomOperation(short rowNo, int costCenterId, decimal manSeconds, decimal machineSeconds, string? notes) =>
        (RowNo, CostCenterId, ManSeconds, MachineSeconds, Notes) = (rowNo, costCenterId, manSeconds, machineSeconds, notes);

    public int Id { get; private set; }
    public int BomId { get; private set; }
    public short RowNo { get; private set; }
    public int CostCenterId { get; private set; }
    public decimal ManSeconds { get; private set; }
    public decimal MachineSeconds { get; private set; }
    public string? Notes { get; private set; }
}
