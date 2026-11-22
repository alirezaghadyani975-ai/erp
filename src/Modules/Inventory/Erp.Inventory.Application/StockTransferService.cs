using Erp.Inventory.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Inventory.Application;

public sealed record StockTransferLineRequest(int ItemId, decimal Quantity, string? Description = null);

public sealed record SaveStockTransferRequest(string Date, int FromWarehouseId, int ToWarehouseId, string? Description, List<StockTransferLineRequest> Lines);

public sealed record StockTransferLineDto(int ItemId, string ItemCode, string ItemTitle, string Unit, decimal Quantity, string? Description);

/// <param name="Id">شناسه حواله انتقال (شناسه انتقال).</param>
public sealed record StockTransferDto(long Id, int DocNo, string Date, int FromWarehouseId, int ToWarehouseId, string? Description,
    long ReceiptId, int ReceiptNo, List<StockTransferLineDto> Lines);

public sealed record StockTransferListItemDto(long Id, int DocNo, string Date, string From, string To, int LineCount, decimal Quantity, string? Description);

/// <summary>
/// انتقال بین انبارها: یک حواله انتقال از انبار مبدأ و یک رسید انتقال به انبار مقصد، هم‌تاریخ و با ردیف‌های هم‌شماره.
/// هر دو سند SourceModule = transfer و SourceId = شناسه حواله دارند و فقط از همین سرویس تغییر می‌کنند.
/// بها: رسید انتقال همان بهای حواله (میانگین مبدأ) را در محاسبه ماهانه می‌گیرد؛ سند حسابداری ندارد.
/// </summary>
public class StockTransferService(IInventoryDbContext db, StockDocService stock, IUnitOfWork uow, IAccessControl access)
{
    public const string Module = "transfer";

    public async Task<List<StockTransferListItemDto>> ListAsync(string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.TransferView);
        var query = db.StockDocs.AsNoTracking().Where(d => d.DocType == StockDocType.TransferIssue);
        if (PersianDate.TryParse(fromDate, out var f)) query = query.Where(d => d.DocDate >= f);
        if (PersianDate.TryParse(toDate, out var t)) query = query.Where(d => d.DocDate <= t);
        if (access.WarehouseIds() is { } allowed) query = query.Where(d => allowed.Contains(d.WarehouseId));
        var issues = await query.OrderByDescending(d => d.DocDate).ThenByDescending(d => d.DocNo)
            .Select(d => new { d.Id, d.DocNo, d.DocDate, d.WarehouseId, d.Description, Count = d.Lines.Count, Qty = d.Lines.Sum(l => l.Quantity) })
            .ToListAsync(ct);
        var ids = issues.Select(i => (long?)i.Id).ToList();
        var targets = await db.StockDocs.AsNoTracking()
            .Where(d => d.DocType == StockDocType.TransferReceipt && d.SourceModule == Module && ids.Contains(d.SourceId))
            .ToDictionaryAsync(d => d.SourceId!.Value, d => d.WarehouseId, ct);
        var warehouses = await db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        return issues.Select(i => new StockTransferListItemDto(i.Id, i.DocNo, PersianDate.ToPersian(i.DocDate), warehouses.GetValueOrDefault(i.WarehouseId, "?"),
            targets.TryGetValue(i.Id, out var to) ? warehouses.GetValueOrDefault(to, "?") : "?", i.Count, i.Qty, i.Description)).ToList();
    }

    public async Task<StockTransferDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.TransferView);
        var (issue, receipt) = await LoadPairAsync(id, ct);
        access.DemandScope(ScopeKind.Warehouse, issue.WarehouseId);
        var itemIds = issue.Lines.Select(l => l.ItemId).ToList();
        var items = await (from i in db.Items where itemIds.Contains(i.Id)
                           join u in db.Units on i.UnitId equals u.Id
                           select new { i.Id, i.Code, i.Title, Unit = u.Title }).ToDictionaryAsync(x => x.Id, ct);
        return new StockTransferDto(issue.Id, issue.DocNo, PersianDate.ToPersian(issue.DocDate), issue.WarehouseId, receipt.WarehouseId, issue.Description,
            receipt.Id, receipt.DocNo,
            issue.Lines.OrderBy(l => l.RowNo).Select(l => new StockTransferLineDto(l.ItemId, items[l.ItemId].Code, items[l.ItemId].Title, items[l.ItemId].Unit,
                l.Quantity, l.Description)).ToList());
    }

    public async Task<long> CreateAsync(SaveStockTransferRequest r, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.TransferCreate);
        Validate(r);
        await using var tx = await uow.BeginAsync(ct);
        var issueId = await stock.CreateCoreAsync(Request(StockDocType.TransferIssue, r), (Module, 0), ct);
        var issue = await db.StockDocs.FirstAsync(d => d.Id == issueId, ct);
        issue.SetSource(Module, issueId);
        await db.SaveChangesAsync(ct);
        await stock.CreateCoreAsync(Request(StockDocType.TransferReceipt, r), (Module, issueId), ct);
        await tx.CommitAsync(ct);
        return issueId;
    }

    public async Task UpdateAsync(long id, SaveStockTransferRequest r, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.TransferEdit);
        Validate(r);
        await using var tx = await uow.BeginAsync(ct);
        var (issue, receipt) = await LoadPairAsync(id, ct);
        access.DemandScope(ScopeKind.Warehouse, issue.WarehouseId);
        access.DemandScope(ScopeKind.Warehouse, receipt.WarehouseId);
        access.DemandOwner(issue.CreatedBy);
        // اول حواله (کنترل موجودی مبدأ با مقدار تازه)، بعد رسید هم‌تاریخ
        await stock.UpdateCoreAsync(issue.Id, Request(StockDocType.TransferIssue, r), true, ct);
        await stock.UpdateCoreAsync(receipt.Id, Request(StockDocType.TransferReceipt, r), true, ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.TransferDelete);
        await using var tx = await uow.BeginAsync(ct);
        var (issue, receipt) = await LoadPairAsync(id, ct);
        access.DemandScope(ScopeKind.Warehouse, issue.WarehouseId);
        access.DemandScope(ScopeKind.Warehouse, receipt.WarehouseId);
        access.DemandOwner(issue.CreatedBy);
        // اول رسید مقصد (اگر از آن خارج شده باشد خطای موجودی)، بعد حواله مبدأ
        await stock.DeleteCoreAsync(receipt.Id, true, ct);
        await stock.DeleteCoreAsync(issue.Id, true, ct);
        await tx.CommitAsync(ct);
    }

    private void Validate(SaveStockTransferRequest r)
    {
        if (r.FromWarehouseId == r.ToWarehouseId) throw new DomainException("انبار مبدأ و مقصد انتقال نباید یکی باشد.");
        if (r.Lines.Count == 0) throw new DomainException("انتقال دست‌کم یک ردیف کالا لازم دارد.");
        access.DemandScope(ScopeKind.Warehouse, r.FromWarehouseId);
        access.DemandScope(ScopeKind.Warehouse, r.ToWarehouseId);
    }

    private static SaveStockDocRequest Request(StockDocType type, SaveStockTransferRequest r) =>
        new(type, r.Date, type == StockDocType.TransferIssue ? r.FromWarehouseId : r.ToWarehouseId, null, r.Description,
            r.Lines.Select(l => new StockLineRequest(l.ItemId, l.Quantity, Description: l.Description)).ToList());

    private async Task<(Domain.StockDoc Issue, Domain.StockDoc Receipt)> LoadPairAsync(long id, CancellationToken ct)
    {
        var docs = await db.StockDocs.AsNoTracking().Include(d => d.Lines)
            .Where(d => d.SourceModule == Module && d.SourceId == id).ToListAsync(ct);
        var issue = docs.FirstOrDefault(d => d.DocType == StockDocType.TransferIssue) ?? throw new NotFoundException("انتقال پیدا نشد.");
        var receipt = docs.FirstOrDefault(d => d.DocType == StockDocType.TransferReceipt) ?? throw new NotFoundException("رسید انتقال پیدا نشد.");
        return (issue, receipt);
    }
}
