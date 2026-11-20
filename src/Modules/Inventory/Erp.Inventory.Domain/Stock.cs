using Erp.Inventory.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Inventory.Domain;

/// <summary>انبار (inv.Warehouses).</summary>
public class Warehouse : AggregateRoot<int>
{
    private Warehouse() { } // EF

    public Warehouse(string code, string title, int? keeperId, string? notes) => Update(code, title, keeperId, notes);

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public int? KeeperId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    public void Update(string code, string title, int? keeperId, string? notes)
    {
        var c = PersianText.NormalizeOrNull(code) ?? throw new DomainException("کد انبار الزامی است.");
        if (c.Length > 10) throw new DomainException("کد انبار حداکثر ۱۰ نویسه است.");
        Code = c;
        Title = Texts.Required(title, "عنوان انبار", 100);
        KeeperId = keeperId is > 0 ? keeperId : null;
        Notes = Optional(notes, "توضیحات", 500);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    internal static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}

/// <summary>ردیف ورودی سند انبار.</summary>
/// <param name="SourceLineId">برگشت: ردیف حواله فروش یا رسید خرید مبدأ.</param>
/// <param name="EnteredPrice">اول دوره: فی (الزامی)؛ رسید خرید: فی تقریبی تا رسیدن فاکتور.</param>
public sealed record StockLineInput(int ItemId, decimal Quantity, long? SourceLineId = null, int? CostFactorId = null,
    int? CostCenterId = null, decimal? EnteredPrice = null, string? Description = null);

/// <summary>
/// رسید یا حواله انبار (inv.StockDocs). سند حسابداری مستقیم ندارد؛ بها و سند با محاسبه ماهانه بهای تمام‌شده می‌آید.
/// موجودی منفی را سرویس پیش از ذخیره (و تریگر دیتابیس به‌عنوان پشتیبان) کنترل می‌کند.
/// </summary>
public class StockDoc : AggregateRoot<long>
{
    private readonly List<StockDocLine> _lines = [];

    private StockDoc() { } // EF

    public StockDoc(StockDocType docType, int docNo, int createdBy, DateTime nowUtc)
    {
        if (!Enum.IsDefined(docType)) throw new DomainException("نوع سند انبار معتبر نیست.");
        if (docNo <= 0) throw new DomainException("شماره سند انبار نامعتبر است.");
        DocType = docType;
        DocNo = docNo;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
    }

    public StockDocType DocType { get; private set; }
    public int DocNo { get; private set; }
    public DateOnly DocDate { get; private set; }
    public int WarehouseId { get; private set; }
    public int? PartyId { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    public IReadOnlyList<StockDocLine> Lines => _lines.AsReadOnly();

    public bool IsReceipt => DocType.IsReceipt();

    public void SetHeader(DateOnly date, int warehouseId, int? partyId, string? description)
    {
        if (warehouseId <= 0) throw new DomainException("انبار الزامی است.");
        if (DocType.HasParty() && partyId is not > 0) throw new DomainException($"{DocType.PartyTitle()} الزامی است.");
        DocDate = date;
        WarehouseId = warehouseId;
        PartyId = DocType.HasParty() ? partyId : null;
        Description = Warehouse.Optional(description, "شرح", 500);
    }

    /// <summary>جایگزینی کامل ردیف‌ها.</summary>
    public void SetLines(IEnumerable<StockLineInput> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("حداقل یک ردیف کالا لازم است.");
        if (list.Count > 500) throw new DomainException("تعداد ردیف‌ها بیش از حد مجاز است.");
        var sources = list.Where(l => l.SourceLineId is not null).Select(l => l.SourceLineId).ToList();
        if (sources.Distinct().Count() != sources.Count) throw new DomainException("یک ردیف مبدأ دو بار آمده است.");

        _lines.Clear();
        short row = 0;
        foreach (var l in list)
        {
            row++;
            if (l.ItemId <= 0) throw new DomainException($"ردیف {row}: کالا الزامی است.");
            if (l.Quantity <= 0) throw new DomainException($"ردیف {row}: مقدار باید بزرگتر از صفر باشد.");
            if (DocType.IsReturn() && l.SourceLineId is not > 0)
                throw new DomainException($"ردیف {row}: ردیف {DocType.SourceType()!.Value.Title()} مبدأ الزامی است.");
            if (DocType == StockDocType.Consumption && l.CostFactorId is not > 0)
                throw new DomainException($"ردیف {row}: عامل هزینه مصرف الزامی است.");
            if (DocType == StockDocType.Opening && l.EnteredPrice is null)
                throw new DomainException($"ردیف {row}: فی موجودی اول دوره الزامی است.");
            if (l.EnteredPrice < 0) throw new DomainException($"ردیف {row}: فی نمی‌تواند منفی باشد.");

            var price = DocType is StockDocType.Opening or StockDocType.PurchaseReceipt && l.EnteredPrice is { } p ? Math.Round(p, 4) : (decimal?)null;
            _lines.Add(new StockDocLine(row, l.ItemId, Math.Round(l.Quantity, 6),
                DocType.IsReturn() ? l.SourceLineId : null,
                DocType == StockDocType.Consumption ? l.CostFactorId : null,
                DocType == StockDocType.Consumption ? l.CostCenterId : null,
                price, Warehouse.Optional(l.Description, $"شرح ردیف {row}", 500)));
        }
    }

    public void ClearLines() => _lines.Clear();

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>ردیف رسید یا حواله (inv.StockDocLines).</summary>
public class StockDocLine
{
    private StockDocLine() { } // EF

    internal StockDocLine(short rowNo, int itemId, decimal quantity, long? sourceLineId, int? costFactorId, int? costCenterId,
        decimal? enteredPrice, string? description)
    {
        RowNo = rowNo;
        ItemId = itemId;
        Quantity = quantity;
        SourceLineId = sourceLineId;
        CostFactorId = costFactorId;
        CostCenterId = costCenterId;
        EnteredPrice = enteredPrice;
        Description = description;
    }

    public long Id { get; private set; }
    public long DocId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public long? SourceLineId { get; private set; }
    public int? CostFactorId { get; private set; }
    public int? CostCenterId { get; private set; }
    public decimal? EnteredPrice { get; private set; }
    /// <summary>نتیجه محاسبه بهای تمام‌شده (مرحله ۲).</summary>
    public decimal? UnitCost { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Description { get; private set; }
}
