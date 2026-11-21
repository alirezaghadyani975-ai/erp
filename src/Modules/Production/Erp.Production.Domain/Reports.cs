using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Production.Domain;

/// <param name="BomMaterialId">ردیف فرمول (خالی = ماده خارج از فرمول)</param>
/// <param name="OptionNo">مصرف از گزینه جایگزین ردیف فرمول</param>
public sealed record ProductionMaterialInput(int ItemId, int WarehouseId, decimal StandardQty, decimal ActualQty, int? BomMaterialId, byte? OptionNo);

public sealed record ProductionScrapInput(int ItemId, decimal StandardQty, decimal ActualQty, ScrapType ScrapType);

public sealed record ProductionOperationInput(int CostCenterId, decimal StdManSeconds, decimal StdMachineSeconds, decimal ActualManSeconds,
    decimal ActualMachineSeconds);

public sealed record ProductionOperatorInput(int EmployeeId, int? CostCenterId, decimal Hours);

/// <summary>
/// گزارش تولید (prd.ProductionReports): تولید یک مرحله (نیم‌ساخته یا محصول) از روی فرمول با مصرف، ضایعات و زمان واقعی و اپراتورها.
/// ثبت آن حواله مصرف تولید (هر انبار یک سند) و رسید تولید (محصول و ضایعات) می‌سازد.
/// </summary>
public class ProductionReport : AggregateRoot<long>
{
    public const string Module = "production";

    private readonly List<ProductionMaterial> _materials = [];
    private readonly List<ProductionScrap> _scraps = [];
    private readonly List<ProductionOperation> _operations = [];
    private readonly List<ProductionOperator> _operators = [];
    private readonly List<ProductionStockDoc> _stockDocs = [];

    private ProductionReport() { } // EF

    public ProductionReport(int docNo, int userId, DateTime nowUtc) => (DocNo, CreatedBy, CreatedAt) = (docNo, userId, nowUtc);

    public int DocNo { get; private set; }
    public DateOnly ReportDate { get; private set; }
    public int BomId { get; private set; }
    public int ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public int OutputWarehouseId { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public IReadOnlyList<ProductionMaterial> Materials => _materials.AsReadOnly();
    public IReadOnlyList<ProductionScrap> Scraps => _scraps.AsReadOnly();
    public IReadOnlyList<ProductionOperation> Operations => _operations.AsReadOnly();
    public IReadOnlyList<ProductionOperator> Operators => _operators.AsReadOnly();
    public IReadOnlyList<ProductionStockDoc> StockDocs => _stockDocs.AsReadOnly();

    public void SetHeader(DateOnly date, Bom bom, decimal quantity, int outputWarehouseId, string? description, DateTime nowUtc)
    {
        if (quantity <= 0) throw new DomainException("مقدار تولید سالم باید بیشتر از صفر باشد.");
        if (outputWarehouseId <= 0) throw new DomainException("انبار رسید محصول الزامی است.");
        var d = PersianText.NormalizeOrNull(description);
        if (d?.Length > 500) throw new DomainException("شرح حداکثر ۵۰۰ نویسه است.");
        (ReportDate, BomId, ProductId, Quantity, OutputWarehouseId, Description) = (date, bom.Id, bom.ProductId, Math.Round(quantity, 6), outputWarehouseId, d);
        if (Id != 0) UpdatedAt = nowUtc;
    }

    public void SetLines(IReadOnlyList<ProductionMaterialInput> materials, IReadOnlyList<ProductionScrapInput> scraps,
        IReadOnlyList<ProductionOperationInput> operations, IReadOnlyList<ProductionOperatorInput> operators)
    {
        var mats = new List<ProductionMaterial>();
        short row = 0;
        foreach (var m in materials)
        {
            row++;
            if (m.ItemId <= 0 || m.WarehouseId <= 0) throw new DomainException($"ردیف مواد {row}: ماده و انبار را کامل کنید.");
            if (m.ItemId == ProductId) throw new DomainException($"ردیف مواد {row}: محصول در مواد مصرفی خودش نمی‌آید.");
            if (m.StandardQty < 0 || m.ActualQty < 0) throw new DomainException($"ردیف مواد {row}: مقدار منفی نمی‌شود.");
            if (m.StandardQty == 0 && m.ActualQty == 0) continue;
            mats.Add(new ProductionMaterial(row, m.ItemId, m.WarehouseId, Math.Round(m.StandardQty, 6), Math.Round(m.ActualQty, 6), m.BomMaterialId, m.OptionNo));
        }
        if (!mats.Any(m => m.ActualQty > 0)) throw new DomainException("گزارش تولید حداقل یک ماده مصرفی با مقدار دارد.");

        var scr = new List<ProductionScrap>();
        row = 0;
        foreach (var s in scraps)
        {
            row++;
            if (s.ItemId <= 0) throw new DomainException($"ردیف ضایعات {row}: کالای ضایعات انتخاب نشده است.");
            if (s.StandardQty < 0 || s.ActualQty < 0) throw new DomainException($"ردیف ضایعات {row}: مقدار منفی نمی‌شود.");
            if (s.StandardQty == 0 && s.ActualQty == 0) continue;
            scr.Add(new ProductionScrap(row, s.ItemId, Math.Round(s.StandardQty, 6), Math.Round(s.ActualQty, 6), s.ScrapType));
        }

        var ops = new List<ProductionOperation>();
        foreach (var o in operations)
        {
            if (o.CostCenterId <= 0) throw new DomainException("مرکز هزینه زمان تولید انتخاب نشده است.");
            if (new[] { o.StdManSeconds, o.StdMachineSeconds, o.ActualManSeconds, o.ActualMachineSeconds }.Any(x => x < 0))
                throw new DomainException("زمان منفی نمی‌شود.");
            if (ops.Any(x => x.CostCenterId == o.CostCenterId)) throw new DomainException("یک مرکز هزینه دو بار در زمان تولید آمده است.");
            ops.Add(new ProductionOperation(o.CostCenterId, Math.Round(o.StdManSeconds, 2), Math.Round(o.StdMachineSeconds, 2),
                Math.Round(o.ActualManSeconds, 2), Math.Round(o.ActualMachineSeconds, 2)));
        }

        var opr = new List<ProductionOperator>();
        foreach (var p in operators)
        {
            if (p.EmployeeId <= 0) throw new DomainException("اپراتور انتخاب نشده است.");
            if (p.Hours <= 0) throw new DomainException("ساعت کار اپراتور باید بیشتر از صفر باشد.");
            if (opr.Any(x => x.EmployeeId == p.EmployeeId)) throw new DomainException("یک اپراتور دو بار آمده است.");
            opr.Add(new ProductionOperator(p.EmployeeId, p.CostCenterId is > 0 ? p.CostCenterId : null, Math.Round(p.Hours, 2)));
        }

        _materials.Clear(); _materials.AddRange(mats);
        _scraps.Clear(); _scraps.AddRange(scr);
        _operations.Clear(); _operations.AddRange(ops);
        _operators.Clear(); _operators.AddRange(opr);
    }

    public void SetStockDocs(IEnumerable<long> ids)
    {
        _stockDocs.Clear();
        _stockDocs.AddRange(ids.Distinct().Select(id => new ProductionStockDoc(id)));
    }
}

public class ProductionMaterial
{
    private ProductionMaterial() { } // EF

    internal ProductionMaterial(short rowNo, int itemId, int warehouseId, decimal standardQty, decimal actualQty, int? bomMaterialId, byte? optionNo) =>
        (RowNo, ItemId, WarehouseId, StandardQty, ActualQty, BomMaterialId, OptionNo) = (rowNo, itemId, warehouseId, standardQty, actualQty, bomMaterialId, optionNo);

    public long Id { get; private set; }
    public long ReportId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public int WarehouseId { get; private set; }
    public decimal StandardQty { get; private set; }
    public decimal ActualQty { get; private set; }
    public int? BomMaterialId { get; private set; }
    public byte? OptionNo { get; private set; }
}

public class ProductionScrap
{
    private ProductionScrap() { } // EF

    internal ProductionScrap(short rowNo, int itemId, decimal standardQty, decimal actualQty, ScrapType scrapType) =>
        (RowNo, ItemId, StandardQty, ActualQty, ScrapType) = (rowNo, itemId, standardQty, actualQty, scrapType);

    public long Id { get; private set; }
    public long ReportId { get; private set; }
    public short RowNo { get; private set; }
    public int ItemId { get; private set; }
    public decimal StandardQty { get; private set; }
    public decimal ActualQty { get; private set; }
    public ScrapType ScrapType { get; private set; }
}

public class ProductionOperation
{
    private ProductionOperation() { } // EF

    internal ProductionOperation(int costCenterId, decimal stdMan, decimal stdMachine, decimal actualMan, decimal actualMachine) =>
        (CostCenterId, StdManSeconds, StdMachineSeconds, ActualManSeconds, ActualMachineSeconds) = (costCenterId, stdMan, stdMachine, actualMan, actualMachine);

    public long ReportId { get; private set; }
    public int CostCenterId { get; private set; }
    public decimal StdManSeconds { get; private set; }
    public decimal StdMachineSeconds { get; private set; }
    public decimal ActualManSeconds { get; private set; }
    public decimal ActualMachineSeconds { get; private set; }
}

public class ProductionOperator
{
    private ProductionOperator() { } // EF

    internal ProductionOperator(int employeeId, int? costCenterId, decimal hours) => (EmployeeId, CostCenterId, Hours) = (employeeId, costCenterId, hours);

    public long ReportId { get; private set; }
    public int EmployeeId { get; private set; }
    public int? CostCenterId { get; private set; }
    public decimal Hours { get; private set; }
}

public class ProductionStockDoc
{
    private ProductionStockDoc() { } // EF

    internal ProductionStockDoc(long stockDocId) => StockDocId = stockDocId;

    public long ReportId { get; private set; }
    public long StockDocId { get; private set; }
}
