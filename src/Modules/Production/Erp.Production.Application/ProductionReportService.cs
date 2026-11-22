using Erp.Inventory.Contracts;
using Erp.Production.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Production.Application;

/// <summary>
/// گزارش تولید: پیش‌نویس از روی فرمول (مصرف، ضایعات و زمان استاندارد برای مقدار تولید)، ثبت مصرف و ضایعات و زمان واقعی و اپراتورها.
/// ثبت در یک تراکنش: حواله مصرف تولید برای هر انبار مواد و رسید تولید (محصول + ضایعات) به انبار محصول؛
/// کنترل‌های انبار (موجودی منفی، ماه قفل، نوع کالا) همان‌جا اعمال می‌شود.
/// </summary>
public class ProductionReportService(IProductionDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock,
    IProductionStock stock, IUnitOfWork uow)
{
    public async Task<List<ProductionReportListItemDto>> ListAsync(string? fromDate, string? toDate, int? productId, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportView);
        var query = db.Reports.AsNoTracking().Include(r => r.Operators).AsQueryable();
        if (PersianDate.TryParse(fromDate, out var from)) query = query.Where(r => r.ReportDate >= from);
        if (PersianDate.TryParse(toDate, out var to)) query = query.Where(r => r.ReportDate <= to);
        if (productId is int p) query = query.Where(r => r.ProductId == p);
        var reports = await query.OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.DocNo).ToListAsync(ct);
        var itemIds = reports.Select(r => r.ProductId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var bomIds = reports.Select(r => r.BomId).Distinct().ToList();
        var boms = await db.Boms.Where(b => bomIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        return reports.Select(r => new ProductionReportListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.ReportDate), r.ProductId,
            $"{items[r.ProductId].Code} {items[r.ProductId].Title}", boms.GetValueOrDefault(r.BomId, "?"), r.Quantity, items[r.ProductId].Unit,
            r.Operators.Count, r.Operators.Sum(o => o.Hours), r.Description)).ToList();
    }

    /// <summary>کارکنان شاغل برای پنجره اپراتورها.</summary>
    public async Task<List<LookupDto>> OperatorsAsync(CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportView);
        return await db.Employees.Where(e => e.Status != Erp.Personnel.Contracts.EmployeeStatus.Left).OrderBy(e => e.PersonnelNo)
            .Select(e => new LookupDto(e.Id, e.PersonnelNo + " — " + e.DisplayName)).ToListAsync(ct);
    }

    /// <summary>فرمول‌های معتبر یک محصول در تاریخ (اول پیش‌فرض).</summary>
    public async Task<List<LookupDto>> BomsForAsync(int productId, string date, CancellationToken ct)
    {
        var d = PersianDate.Parse(date);
        return (await db.Boms.AsNoTracking().Where(b => b.ProductId == productId && b.IsActive).ToListAsync(ct))
            .Where(b => b.IsValidOn(d)).OrderByDescending(b => b.IsDefault).ThenBy(b => b.Code)
            .Select(b => new LookupDto(b.Id, $"{b.Code} {b.Title}{(b.IsDefault ? " (پیش‌فرض)" : "")}")).ToList();
    }

    /// <summary>پیش‌نویس استاندارد گزارش تولید برای فرمول و مقدار.</summary>
    public async Task<ProductionDraftDto> DraftAsync(int bomId, decimal quantity, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportView);
        if (quantity <= 0) throw new DomainException("مقدار تولید باید بیشتر از صفر باشد.");
        var bom = await db.Boms.AsNoTracking().Include(b => b.Materials).ThenInclude(m => m.Substitutes).Include(b => b.Scraps).Include(b => b.Operations)
            .FirstOrDefaultAsync(b => b.Id == bomId, ct) ?? throw new NotFoundException("فرمول ساخت پیدا نشد.");
        var ratio = bom.Ratio(quantity);
        return new ProductionDraftDto(bom.Id, bom.ProductId, quantity, bom.OutputWarehouseId,
            bom.Materials.OrderBy(m => m.RowNo).Select(m => new DraftMaterialDto(m.Id, m.ItemId, m.WarehouseId ?? bom.IssueWarehouseId, m.StandardFor(ratio),
                m.Substitutes.GroupBy(s => s.OptionNo).OrderBy(g => g.Key)
                    .Select(g => new SubstituteOptionDto(g.Key, g.Select(s => new BomSubstituteInput(s.OptionNo, s.ItemId, s.Factor)).ToList())).ToList())).ToList(),
            bom.Scraps.OrderBy(s => s.RowNo).Select(s => new ProductionScrapInput(s.ItemId, Math.Round(s.Quantity * ratio, 6), Math.Round(s.Quantity * ratio, 6), s.ScrapType)).ToList(),
            bom.Operations.OrderBy(o => o.RowNo).Select(o => new ProductionOperationInput(o.CostCenterId, Math.Round(o.ManSeconds * ratio, 2),
                Math.Round(o.MachineSeconds * ratio, 2), Math.Round(o.ManSeconds * ratio, 2), Math.Round(o.MachineSeconds * ratio, 2))).ToList());
    }

    public async Task<ProductionReportDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportView);
        var r = await LoadAsync(id, tracking: false, ct);
        var docIds = r.StockDocs.Select(d => d.StockDocId).ToList();
        var docs = await db.StockDocs.Where(d => docIds.Contains(d.Id)).ToListAsync(ct);
        var whIds = docs.Select(d => d.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        return new ProductionReportDto(r.Id, r.DocNo, PersianDate.ToPersian(r.ReportDate), r.BomId, r.ProductId, r.Quantity, r.OutputWarehouseId, r.Description,
            r.Materials.OrderBy(m => m.RowNo).Select(m => new ProductionMaterialInput(m.ItemId, m.WarehouseId, m.StandardQty, m.ActualQty, m.BomMaterialId, m.OptionNo)).ToList(),
            r.Scraps.OrderBy(s => s.RowNo).Select(s => new ProductionScrapInput(s.ItemId, s.StandardQty, s.ActualQty, s.ScrapType)).ToList(),
            r.Operations.Select(o => new ProductionOperationInput(o.CostCenterId, o.StdManSeconds, o.StdMachineSeconds, o.ActualManSeconds, o.ActualMachineSeconds)).ToList(),
            r.Operators.Select(o => new ProductionOperatorInput(o.EmployeeId, o.CostCenterId, o.Hours)).ToList(),
            docs.OrderBy(d => d.DocType).Select(d => new StockDocLinkDto(d.Id, d.DocType, d.DocNo, warehouses.GetValueOrDefault(d.WarehouseId, "?"))).ToList());
    }

    public async Task<long> CreateAsync(SaveProductionReportRequest r, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportCreate);
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.Reports.MaxAsync(x => (int?)x.DocNo, ct) ?? 0) + 1;
        var report = new ProductionReport(no, user.UserId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(report, r, ct);
        db.Add(report);
        await db.SaveChangesAsync(ct);
        await SyncStockAsync(report, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return report.Id;
    }

    public async Task UpdateAsync(long id, SaveProductionReportRequest r, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportEdit);
        await using var tx = await uow.BeginAsync(ct);
        var report = await LoadAsync(id, tracking: true, ct);
        access.DemandOwner(report.CreatedBy);
        await ApplyAsync(report, r, ct);
        await db.SaveChangesAsync(ct);
        await SyncStockAsync(report, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.ReportDelete);
        await using var tx = await uow.BeginAsync(ct);
        var report = await LoadAsync(id, tracking: true, ct);
        access.DemandOwner(report.CreatedBy);
        var docIds = report.StockDocs.Select(d => d.StockDocId).ToList();
        var docs = await db.StockDocs.Where(d => docIds.Contains(d.Id)).ToListAsync(ct);
        report.SetStockDocs([]);
        await db.SaveChangesAsync(ct);
        // اول حواله‌ها (مواد برمی‌گردد)، بعد رسید (محصول خارج می‌شود؛ اگر مصرف یا فروخته شده باشد خطا)
        foreach (var d in docs.OrderByDescending(d => d.DocType)) await stock.DeleteAsync(d.Id, ct);
        db.Remove(report);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ApplyAsync(ProductionReport report, SaveProductionReportRequest r, CancellationToken ct)
    {
        var date = PersianDate.Parse(r.Date);
        var bom = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == r.BomId, ct) ?? throw new DomainException("فرمول ساخت انتخاب نشده یا پیدا نشد.");
        if (bom.Id != report.BomId && !bom.IsValidOn(date))
            throw new DomainException($"فرمول {bom.Code} در تاریخ {r.Date} فعال یا معتبر نیست.");

        report.SetHeader(date, bom, r.Quantity, r.OutputWarehouseId, r.Description, clock.GetUtcNow().UtcDateTime);
        report.SetLines(r.Materials, r.Scraps, r.Operations, r.Operators);

        var itemIds = report.Materials.Select(m => m.ItemId).Concat(report.Scraps.Select(s => s.ItemId)).Append(report.ProductId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        ProductionItemRef Item(int id) => items.GetValueOrDefault(id) ?? throw new DomainException("کالای گزارش تولید پیدا نشد.");
        foreach (var m in report.Materials)
        {
            var item = Item(m.ItemId);
            if (!item.CanInput) throw new DomainException($"ردیف مواد {m.RowNo}: «{item.Title}» از نوع «{item.Category}» است و در تولید مصرف نمی‌شود.");
        }
        foreach (var s in report.Scraps)
            if (!Item(s.ItemId).IsScrap) throw new DomainException($"ردیف ضایعات {s.RowNo}: «{Item(s.ItemId).Title}» از نوع «ضایعات» نیست.");

        var employeeIds = report.Operators.Select(o => o.EmployeeId).ToList();
        if (await db.Employees.CountAsync(e => employeeIds.Contains(e.Id), ct) != employeeIds.Count) throw new DomainException("اپراتور پیدا نشد.");
        var centerIds = report.Operations.Select(o => o.CostCenterId).Concat(report.Operators.Select(o => o.CostCenterId).OfType<int>()).Distinct().ToList();
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToListAsync(ct);
        if (centers.Count != centerIds.Count) throw new DomainException("مرکز هزینه پیدا نشد.");
        if (centers.FirstOrDefault(c => !c.IsProduction) is { } nonProd)
            throw new DomainException($"مرکز «{nonProd.Title}» تولیدی نیست؛ زمان و اپراتور گزارش تولید فقط روی مراکز تولیدی ثبت می‌شود.");
    }

    /// <summary>سندهای انبار گزارش: حواله مصرف تولید برای هر انبار مواد و یک رسید تولید؛ سندهای قبلی بازنویسی یا حذف می‌شوند.</summary>
    private async Task SyncStockAsync(ProductionReport report, CancellationToken ct)
    {
        var oldIds = report.StockDocs.Select(d => d.StockDocId).ToList();
        var old = await db.StockDocs.Where(d => oldIds.Contains(d.Id)).ToListAsync(ct);
        var itemIds = report.Materials.Select(m => m.ItemId).Concat(report.Scraps.Select(s => s.ItemId)).Append(report.ProductId).Distinct().ToList();
        var titles = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);
        var description = $"گزارش تولید {report.DocNo:000000} — {titles[report.ProductId]}";

        var wanted = new List<(StockDocType Type, int Warehouse, List<ProductionStockLine> Lines)>();
        var receipt = new List<ProductionStockLine> { new(report.ProductId, report.Quantity, "محصول") };
        receipt.AddRange(report.Scraps.Where(s => s.ActualQty > 0).Select(s => new ProductionStockLine(s.ItemId, s.ActualQty, "ضایعات")));
        wanted.Add((StockDocType.ProductionReceipt, report.OutputWarehouseId, receipt));
        wanted.AddRange(report.Materials.Where(m => m.ActualQty > 0).GroupBy(m => m.WarehouseId)
            .Select(g => (StockDocType.ProductionIssue, g.Key, g.GroupBy(m => m.ItemId).Select(x => new ProductionStockLine(x.Key, x.Sum(m => m.ActualQty))).ToList())));

        var ids = new List<long>();
        // رسید پیش از حواله تا نیم‌ساخته‌ای که همین‌جا تولید و مصرف شود موجودی منفی نسازد
        foreach (var (type, warehouse, lines) in wanted)
        {
            var existing = old.FirstOrDefault(d => d.DocType == type && d.WarehouseId == warehouse);
            ids.Add(await stock.SaveAsync(existing?.Id, type, report.ReportDate, warehouse, ProductionReport.Module, report.Id, description, lines, ct));
        }
        var leftover = old.Where(d => !ids.Contains(d.Id)).OrderByDescending(d => d.DocType).ToList();
        report.SetStockDocs(ids);
        await db.SaveChangesAsync(ct);
        foreach (var d in leftover) await stock.DeleteAsync(d.Id, ct);
    }

    private async Task<ProductionReport> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.Reports.Include(r => r.Materials).Include(r => r.Scraps).Include(r => r.Operations).Include(r => r.Operators)
            .Include(r => r.StockDocs).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("گزارش تولید پیدا نشد.");
    }
}
