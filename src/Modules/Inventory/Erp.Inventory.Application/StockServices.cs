using Erp.Costing.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Inventory.Application;

/// <summary>انبارها. انبار دارای سند حذف نمی‌شود و فقط غیرفعال می‌شود.</summary>
public class WarehouseService(IInventoryDbContext db, IAccessControl access)
{
    /// <summary>فهرست انبارها (آزاد؛ فرم‌های انبار لازمش دارند).</summary>
    public async Task<List<WarehouseDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var list = (await db.Warehouses.AsNoTracking().Where(w => includeInactive || w.IsActive).OrderBy(w => w.Code).ToListAsync(ct))
            .Where(w => access.CanUse(ScopeKind.Warehouse, w.Id)).ToList();
        var counts = await db.StockDocs.GroupBy(d => d.WarehouseId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var keeperIds = list.Where(w => w.KeeperId is not null).Select(w => w.KeeperId!.Value).Distinct().ToList();
        var keepers = await db.Parties.Where(p => keeperIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        return list.Select(w => new WarehouseDto(w.Id, w.Code, w.Title, w.KeeperId, w.KeeperId is int k ? keepers.GetValueOrDefault(k) : null,
            w.IsActive, w.Notes, counts.GetValueOrDefault(w.Id))).ToList();
    }

    public async Task<int> CreateAsync(SaveWarehouseRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Warehouses);
        var w = new Warehouse(request.Code, request.Title, request.KeeperId, request.Notes);
        if (!request.IsActive) w.Deactivate();
        await EnsureUniqueAsync(w, ct);
        db.Add(w);
        await db.SaveChangesAsync(ct);
        return w.Id;
    }

    public async Task UpdateAsync(int id, SaveWarehouseRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Warehouses);
        var w = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("انبار پیدا نشد.");
        w.Update(request.Code, request.Title, request.KeeperId, request.Notes);
        if (request.IsActive) w.Activate(); else w.Deactivate();
        await EnsureUniqueAsync(w, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Warehouses);
        var w = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("انبار پیدا نشد.");
        if (await db.StockDocs.AnyAsync(d => d.WarehouseId == id, ct))
            throw new DomainException($"انبار «{w.Title}» سند انبار دارد و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        db.Remove(w);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(Warehouse w, CancellationToken ct)
    {
        if (await db.Warehouses.AnyAsync(x => x.Id != w.Id && x.Code == w.Code, ct))
            throw new DomainException($"انبار با کد {w.Code} قبلاً تعریف شده است.");
        if (await db.Warehouses.AnyAsync(x => x.Id != w.Id && x.Title == w.Title, ct))
            throw new DomainException($"انبار «{w.Title}» قبلاً تعریف شده است.");
    }
}

/// <summary>
/// رسید و حواله انبار. سند حسابداری ندارد (بها و سند با محاسبه ماهانه بهای تمام‌شده). کنترل‌ها: موجودی منفی در هیچ تاریخی،
/// برگشت فقط از ردیف فاکتورشده و تا مانده آن، و سندی که فاکتور شده یا برگشت خورده اصلاح و حذف نمی‌شود.
/// </summary>
public class StockDocService(IInventoryDbContext db, IUnitOfWork uow, ICurrentUser user, TimeProvider clock, IAccessControl access)
{
    public async Task<List<StockDocListItemDto>> ListAsync(bool receipts, StockDocType? type, string? term, string? fromDate, string? toDate,
        CancellationToken ct)
    {
        access.Demand(receipts ? InventoryPermissions.ReceiptView : InventoryPermissions.IssueView);
        var query = db.StockDocs.AsNoTracking().Where(d => receipts ? (byte)d.DocType < 10 : (byte)d.DocType > 10);
        if (type is { } t) query = query.Where(d => d.DocType == t);
        if (access.OwnDocsOnly) query = query.Where(d => d.CreatedBy == access.UserId);
        if (access.WarehouseIds() is { } allowed) query = query.Where(d => allowed.Contains(d.WarehouseId));
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(d => d.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var e = PersianDate.Parse(toDate); query = query.Where(d => d.DocDate <= e); }

        var rows = await query
            .Select(d => new
            {
                d.Id, d.DocType, d.DocNo, d.DocDate, d.WarehouseId, d.PartyId, d.Description,
                Warehouse = db.Warehouses.Where(w => w.Id == d.WarehouseId).Select(w => w.Title).FirstOrDefault(),
                Party = db.Parties.Where(p => p.Id == d.PartyId).Select(p => p.DisplayName).FirstOrDefault(),
                LineIds = d.Lines.Select(l => l.Id).ToList(),
                Qty = d.Lines.Sum(l => l.Quantity)
            })
            .OrderByDescending(d => d.DocDate).ThenByDescending(d => d.Id)
            .ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Party ?? "").Contains(term) || (r.Description ?? "").Contains(term) || (r.Warehouse ?? "").Contains(term)
                                   || r.DocNo.ToString() == term).ToList();

        var links = await db.InvoiceLinksAsync(rows.SelectMany(r => r.LineIds).ToList(), ct);
        return rows.Select(r => new StockDocListItemDto(r.Id, r.DocType, r.DocNo, PersianDate.ToPersian(r.DocDate), r.WarehouseId,
            r.Warehouse ?? "?", r.PartyId, r.Party, r.LineIds.Count, r.Qty, r.LineIds.Count(links.ContainsKey), r.Description)).ToList();
    }

    public async Task<StockDocDto> GetAsync(long id, CancellationToken ct)
    {
        var doc = await LoadAsync(id, tracking: false, ct);
        access.Demand(doc.IsReceipt ? InventoryPermissions.ReceiptView : InventoryPermissions.IssueView);
        access.DemandScope(ScopeKind.Warehouse, doc.WarehouseId);
        return await ToDtoAsync(doc, ct);
    }

    /// <summary>
    /// ردیف‌های مبدأ قابل برگشت: برای رسید برگشت از فروش، ردیف‌های فاکتورشده حواله فروش مشتری؛ برای حواله برگشت از خرید،
    /// ردیف‌های فاکتورشده رسید خرید تأمین‌کننده. برگشت در حال اصلاح از مانده کم نمی‌شود.
    /// </summary>
    public async Task<List<ReturnSourceDto>> ReturnSourcesAsync(StockDocType type, int partyId, long? exceptDocId, CancellationToken ct)
    {
        access.Demand(type.IsReceipt() ? InventoryPermissions.ReceiptView : InventoryPermissions.IssueView);
        var sourceType = type.SourceType() ?? throw new DomainException("این نوع سند برگشت نیست.");
        var lines = await db.StockDocs.Where(d => d.DocType == sourceType && d.PartyId == partyId)
            .SelectMany(d => d.Lines.Select(l => new { l.Id, DocId = d.Id, d.DocNo, d.DocDate, d.WarehouseId, l.RowNo, l.ItemId, l.Quantity }))
            .ToListAsync(ct);
        var links = await db.InvoiceLinksAsync(lines.Select(l => l.Id).ToList(), ct);
        lines = lines.Where(l => links.ContainsKey(l.Id)).ToList();

        var returned = await ReturnedAsync(lines.Select(l => l.Id).ToList(), exceptDocId, ct);
        var items = await ItemsAsync(lines.Select(l => l.ItemId), ct);
        var warehouses = await db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        return lines.Select(l =>
            {
                var item = items.GetValueOrDefault(l.ItemId);
                var r = returned.GetValueOrDefault(l.Id);
                return new ReturnSourceDto(l.Id, l.DocId, l.DocNo, PersianDate.ToPersian(l.DocDate), l.WarehouseId,
                    warehouses.GetValueOrDefault(l.WarehouseId, "?"), l.RowNo, l.ItemId, item.Code, item.Title, item.Unit,
                    l.Quantity, r, l.Quantity - r, links[l.Id]);
            })
            .Where(x => x.Remaining > 0)
            .OrderByDescending(x => x.Date).ThenBy(x => x.DocNo).ThenBy(x => x.RowNo).ToList();
    }

    public async Task<long> CreateAsync(SaveStockDocRequest request, CancellationToken ct)
    {
        access.Demand(request.Type.IsReceipt() ? InventoryPermissions.ReceiptCreate : InventoryPermissions.IssueCreate);
        if (request.Type.IsProduction()) throw new DomainException($"«{request.Type.Title()}» فقط از گزارش تولید ساخته می‌شود.");
        access.DemandScope(ScopeKind.Warehouse, request.WarehouseId);
        return await CreateCoreAsync(request, null, ct);
    }

    /// <summary>ساخت سند (بدون کنترل مجوز؛ برای ماژول تولید از راه IProductionStock).</summary>
    internal async Task<long> CreateCoreAsync(SaveStockDocRequest request, (string Module, long Id)? source, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var docNo = (await db.StockDocs.Where(d => d.DocType == request.Type).MaxAsync(d => (int?)d.DocNo, ct) ?? 0) + 1;
        var doc = new StockDoc(request.Type, docNo, user.UserId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(doc, request, ct);
        if (source is { } s) doc.SetSource(s.Module, s.Id);
        await EnsureStockAsync(doc, [], ct);
        db.Add(doc);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return doc.Id;
    }

    public async Task UpdateAsync(long id, SaveStockDocRequest request, CancellationToken ct) => await UpdateCoreAsync(id, request, false, ct);

    /// <param name="fromSource">از ماژول سازنده (بدون کنترل مجوز و مالکیت).</param>
    internal async Task UpdateCoreAsync(long id, SaveStockDocRequest request, bool fromSource, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var doc = await LoadAsync(id, tracking: true, ct, checkOwner: !fromSource);
        if (!fromSource)
        {
            access.Demand(doc.IsReceipt ? InventoryPermissions.ReceiptEdit : InventoryPermissions.IssueEdit);
            access.DemandScope(ScopeKind.Warehouse, doc.WarehouseId);
            access.DemandScope(ScopeKind.Warehouse, request.WarehouseId);
            EnsureNotSourced(doc);
        }
        if (doc.DocType != request.Type) throw new DomainException("نوع سند انبار عوض نمی‌شود.");
        await EnsureUnlockedAsync(doc, "اصلاح", ct);

        var before = doc.Lines.Select(l => (l.ItemId, doc.WarehouseId)).ToList();
        var previousItems = doc.Lines.Select(l => l.ItemId).ToHashSet();
        doc.ClearLines();
        await db.SaveChangesAsync(ct); // ردیف‌های قبلی پیش از درج ردیف‌های تازه حذف شوند
        await ApplyAsync(doc, request, ct, previousItems);
        await EnsureStockAsync(doc, before, ct);
        doc.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct) => await DeleteCoreAsync(id, false, ct);

    internal async Task DeleteCoreAsync(long id, bool fromSource, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var doc = await LoadAsync(id, tracking: true, ct, checkOwner: !fromSource);
        if (!fromSource)
        {
            access.Demand(doc.IsReceipt ? InventoryPermissions.ReceiptDelete : InventoryPermissions.IssueDelete);
            access.DemandScope(ScopeKind.Warehouse, doc.WarehouseId);
            EnsureNotSourced(doc);
        }
        await EnsureUnlockedAsync(doc, "حذف", ct);
        if (doc.IsReceipt)
            await EnsureBalancesAsync(doc.Id, doc.Lines.Select(l => (l.ItemId, doc.WarehouseId)), [], ct);
        db.Remove(doc);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task ApplyAsync(StockDoc doc, SaveStockDocRequest request, CancellationToken ct, HashSet<int>? previousItems = null)
    {
        var type = request.Type;
        var date = PersianDate.Parse(request.Date);
        if (!await db.FiscalYears.AnyAsync(y => y.StartDate <= date && y.EndDate >= date, ct))
            throw new DomainException($"تاریخ {request.Date} در هیچ سال مالی تعریف‌شده‌ای نیست.");

        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId, ct)
                        ?? throw new DomainException("انبار انتخاب نشده یا پیدا نشد.");
        if (!warehouse.IsActive && warehouse.Id != doc.WarehouseId) throw new DomainException($"انبار «{warehouse.Title}» غیرفعال است.");

        PartyRef? party = null;
        if (type.HasParty())
        {
            party = await db.Parties.FirstOrDefaultAsync(p => p.Id == request.PartyId, ct)
                    ?? throw new DomainException($"{type.PartyTitle()} انتخاب نشده یا پیدا نشد.");
            if (!party.IsActive && party.Id != doc.PartyId) throw new DomainException($"شخص «{party.DisplayName}» غیرفعال است.");
        }

        await CheckOpeningAsync(doc, type, warehouse, date, ct);

        // کالاها
        var requestItemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => requestItemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Title, i.Kind, i.IsActive, i.CategoryId }).ToDictionaryAsync(i => i.Id, ct);
        var categories = await db.ItemCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var row = 0;
        foreach (var l in request.Lines)
        {
            row++;
            var item = items.GetValueOrDefault(l.ItemId) ?? throw new DomainException($"ردیف {row}: کالا انتخاب نشده یا پیدا نشد.");
            if (item.Kind != ItemKind.Goods) throw new DomainException($"ردیف {row}: «{item.Title}» خدمت است و در انبار نمی‌آید.");
            if (!item.IsActive && previousItems?.Contains(item.Id) != true) throw new DomainException($"ردیف {row}: «{item.Title}» غیرفعال است.");
            // قاعده‌های نوع کالا (پشتیبان: تریگر trg_StockDocLines_Category)
            if (item.CategoryId is byte cid && categories.TryGetValue(cid, out var c))
            {
                var problem = type switch
                {
                    StockDocType.PurchaseReceipt when !c.CanPurchase => "خریدنی نیست (فقط تولید می‌شود)",
                    StockDocType.SalesIssue when !c.CanSell => "فروختنی نیست",
                    StockDocType.Consumption when !c.CanConsume => "در حواله مصرف نمی‌آید",
                    StockDocType.ProductionIssue when !c.CanProductionInput => "در تولید مصرف نمی‌شود",
                    StockDocType.ProductionReceipt when !c.CanProductionOutput && !c.IsScrap => "محصول یا ضایعات تولید نیست",
                    _ => null
                };
                if (problem is not null) throw new DomainException($"ردیف {row}: «{item.Title}» از نوع «{c.Title}» است و {problem}.");
            }
        }

        if (type == StockDocType.Consumption) await CheckConsumptionAsync(request.Lines, ct);
        if (type.IsReturn()) await CheckReturnsAsync(doc, type, party!, date, request.Lines, items.ToDictionary(i => i.Key, i => i.Value.Title), ct);

        doc.SetHeader(date, warehouse.Id, party?.Id, request.Description);
        doc.SetLines(request.Lines.Select(l => new StockLineInput(l.ItemId, l.Quantity, l.SourceLineId, l.CostFactorId, l.CostCenterId,
            l.EnteredPrice, l.Description)));
    }

    /// <summary>موجودی اول دوره: روز اول یک سال مالی، یکی برای هر انبار و قبل از همه سندهای آن انبار.</summary>
    private async Task CheckOpeningAsync(StockDoc doc, StockDocType type, Warehouse warehouse, DateOnly date, CancellationToken ct)
    {
        if (type == StockDocType.Opening)
        {
            if (!await db.FiscalYears.AnyAsync(y => y.StartDate == date, ct))
                throw new DomainException("تاریخ موجودی اول دوره باید روز اول یک سال مالی باشد.");
            if (await db.StockDocs.Where(d => d.Id != doc.Id && d.DocType == StockDocType.Opening && d.WarehouseId == warehouse.Id)
                    .Select(d => (int?)d.DocNo).FirstOrDefaultAsync(ct) is int other)
                throw new DomainException($"انبار «{warehouse.Title}» موجودی اول دوره دارد (شماره {other:000000}).");
            if (await db.StockDocs.AnyAsync(d => d.Id != doc.Id && d.WarehouseId == warehouse.Id && d.DocDate < date, ct))
                throw new DomainException($"انبار «{warehouse.Title}» پیش از این تاریخ سند دارد؛ موجودی اول دوره باید اولین سند انبار باشد.");
        }
        else if (await db.StockDocs.Where(d => d.DocType == StockDocType.Opening && d.WarehouseId == warehouse.Id)
                     .Select(d => (DateOnly?)d.DocDate).FirstOrDefaultAsync(ct) is { } opening && date < opening)
        {
            throw new DomainException($"تاریخ سند پیش از موجودی اول دوره انبار «{warehouse.Title}» ({PersianDate.ToPersian(opening)}) است.");
        }
    }

    private async Task CheckConsumptionAsync(List<StockLineRequest> lines, CancellationToken ct)
    {
        var factorIds = lines.Where(l => l.CostFactorId is not null).Select(l => l.CostFactorId!.Value).Distinct().ToList();
        var centerIds = lines.Where(l => l.CostCenterId is not null).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var row = 0;
        foreach (var l in lines)
        {
            row++;
            var factor = l.CostFactorId is int fid ? factors.GetValueOrDefault(fid) : null;
            if (factor is null) throw new DomainException($"ردیف {row}: عامل هزینه انتخاب نشده یا پیدا نشد.");
            if (!factor.IsActive) throw new DomainException($"ردیف {row}: عامل هزینه «{factor.Title}» غیرفعال است.");
            if (factor.Nature == CostNature.Finance)
            {
                if (l.CostCenterId is not null) throw new DomainException($"ردیف {row}: عامل «{factor.Title}» هزینه مالی است و مرکز هزینه ندارد.");
                continue;
            }
            var center = l.CostCenterId is int cid ? centers.GetValueOrDefault(cid) : null;
            if (center is null) throw new DomainException($"ردیف {row}: مرکز هزینه انتخاب نشده یا پیدا نشد.");
            if (!center.IsActive) throw new DomainException($"ردیف {row}: مرکز هزینه «{center.Title}» غیرفعال است.");
        }
    }

    private async Task CheckReturnsAsync(StockDoc doc, StockDocType type, PartyRef party, DateOnly date, List<StockLineRequest> lines,
        Dictionary<int, string> itemTitles, CancellationToken ct)
    {
        var sourceType = type.SourceType()!.Value;
        var sourceIds = lines.Where(l => l.SourceLineId is not null).Select(l => l.SourceLineId!.Value).Distinct().ToList();
        var sources = await db.StockDocs.Where(d => d.DocType == sourceType)
            .SelectMany(d => d.Lines.Where(l => sourceIds.Contains(l.Id))
                .Select(l => new { l.Id, l.ItemId, l.Quantity, l.RowNo, d.DocNo, d.DocDate, d.PartyId }))
            .ToDictionaryAsync(s => s.Id, ct);
        var links = await db.InvoiceLinksAsync(sourceIds, ct);
        var returned = await ReturnedAsync(sourceIds, doc.Id == 0 ? null : doc.Id, ct);

        var row = 0;
        foreach (var l in lines)
        {
            row++;
            var title = itemTitles.GetValueOrDefault(l.ItemId, "?");
            var s = l.SourceLineId is long sid ? sources.GetValueOrDefault(sid) : null;
            if (s is null) throw new DomainException($"ردیف {row} («{title}»): ردیف {sourceType.Title()} مبدأ انتخاب نشده یا پیدا نشد.");
            var origin = $"ردیف {s.RowNo} {sourceType.Title()} {s.DocNo:000000}";
            if (s.PartyId != party.Id) throw new DomainException($"ردیف {row}: {origin} مال «{party.DisplayName}» نیست.");
            if (s.ItemId != l.ItemId) throw new DomainException($"ردیف {row}: کالای ردیف با {origin} یکی نیست.");
            if (s.DocDate > date) throw new DomainException($"ردیف {row}: تاریخ سند پیش از تاریخ {origin} است.");
            if (!links.ContainsKey(s.Id))
                throw new DomainException($"ردیف {row}: {origin} هنوز فاکتور نشده است؛ به‌جای برگشت، خود {sourceType.Title()} را اصلاح کنید.");
            var remaining = s.Quantity - returned.GetValueOrDefault(s.Id);
            if (l.Quantity > remaining)
                throw new DomainException($"ردیف {row} («{title}»): از {origin} حداکثر {remaining:0.######} قابل برگشت است.");
        }
    }

    /// <summary>سندی که ردیفش فاکتور شده یا برگشت خورده اصلاح و حذف نمی‌شود.</summary>
    private async Task EnsureUnlockedAsync(StockDoc doc, string action, CancellationToken ct)
    {
        var ids = doc.Lines.Select(l => l.Id).ToList();
        var links = await db.InvoiceLinksAsync(ids, ct);
        if (links.Count > 0)
        {
            var link = links.Values.First();
            throw new DomainException($"{doc.DocType.Title()} {doc.DocNo:000000} در {link.Title} {link.DocNo:000000} آمده و {action} نمی‌شود؛ " +
                                      "اول ردیف‌هایش را از فاکتور بردارید.");
        }
        var returnDoc = await db.StockDocs.Where(d => d.Lines.Any(l => l.SourceLineId != null && ids.Contains(l.SourceLineId.Value)))
            .Select(d => new { d.DocType, d.DocNo }).FirstOrDefaultAsync(ct);
        if (returnDoc is not null)
            throw new DomainException($"{doc.DocType.Title()} {doc.DocNo:000000} در {returnDoc.DocType.Title()} {returnDoc.DocNo:000000} برگشت خورده و {action} نمی‌شود.");
    }

    /// <summary>کنترل موجودی منفی سند تازه یا اصلاح‌شده (ردیف‌های قبلی همین سند حساب نمی‌شوند).</summary>
    private Task EnsureStockAsync(StockDoc doc, IReadOnlyCollection<(int ItemId, int WarehouseId)> before, CancellationToken ct)
    {
        var sign = doc.IsReceipt ? 1 : -1;
        var added = doc.Lines.Select(l => (l.ItemId, doc.WarehouseId, doc.DocDate, Qty: sign * l.Quantity)).ToList();
        // اصلاح رسید ممکن است موجودی کالاهای قبلی را منفی کند؛ اصلاح حواله فقط کالاهای تازه را
        var keys = added.Select(a => (a.ItemId, a.WarehouseId)).Concat(doc.IsReceipt ? before : []);
        return EnsureBalancesAsync(doc.Id, keys, added, ct);
    }

    private async Task EnsureBalancesAsync(long excludeDocId, IEnumerable<(int ItemId, int WarehouseId)> keys,
        IReadOnlyCollection<(int ItemId, int WarehouseId, DateOnly Date, decimal Qty)> added, CancellationToken ct)
    {
        var keySet = keys.Distinct().ToHashSet();
        if (keySet.Count == 0) return;
        var itemIds = keySet.Select(k => k.ItemId).Distinct().ToList();
        var warehouseIds = keySet.Select(k => k.WarehouseId).Distinct().ToList();

        var existing = await db.StockDocs
            .Where(d => d.Id != excludeDocId && warehouseIds.Contains(d.WarehouseId))
            .SelectMany(d => d.Lines.Where(l => itemIds.Contains(l.ItemId))
                .Select(l => new { l.ItemId, d.WarehouseId, d.DocDate, Qty = (byte)d.DocType < 10 ? l.Quantity : -l.Quantity }))
            .GroupBy(x => new { x.ItemId, x.WarehouseId, x.DocDate })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.DocDate, Qty = g.Sum(x => x.Qty) })
            .ToListAsync(ct);

        var movements = existing.Select(e => (e.ItemId, e.WarehouseId, Date: e.DocDate, e.Qty))
            .Concat(added.Select(a => (a.ItemId, a.WarehouseId, a.Date, a.Qty)))
            .Where(m => keySet.Contains((m.ItemId, m.WarehouseId)));

        foreach (var key in movements.GroupBy(m => (m.ItemId, m.WarehouseId)))
        {
            decimal balance = 0;
            foreach (var day in key.GroupBy(m => m.Date).OrderBy(g => g.Key))
            {
                balance += day.Sum(m => m.Qty);
                if (balance >= 0) continue;
                var item = await db.Items.Where(i => i.Id == key.Key.ItemId).Select(i => i.Title).FirstAsync(ct);
                var warehouse = await db.Warehouses.Where(w => w.Id == key.Key.WarehouseId).Select(w => w.Title).FirstAsync(ct);
                throw new DomainException($"موجودی «{item}» در «{warehouse}» در تاریخ {PersianDate.ToPersian(day.Key)} " +
                                          $"به {balance:#,0.######} می‌رسد؛ موجودی منفی مجاز نیست.");
            }
        }
    }

    /// <summary>مقدار برگشت‌شده از هر ردیف مبدأ (بدون سند در حال اصلاح).</summary>
    private async Task<Dictionary<long, decimal>> ReturnedAsync(IReadOnlyCollection<long> sourceIds, long? exceptDocId, CancellationToken ct) =>
        await db.StockDocs.Where(d => d.Id != (exceptDocId ?? 0))
            .SelectMany(d => d.Lines.Where(l => l.SourceLineId != null && sourceIds.Contains(l.SourceLineId.Value)))
            .GroupBy(l => l.SourceLineId!.Value)
            .Select(g => new { g.Key, Qty = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    private async Task<Dictionary<int, (string Code, string Title, string Unit)>> ItemsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.Where(i => list.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Title, Unit = db.Units.Where(u => u.Id == i.UnitId).Select(u => u.Title).FirstOrDefault() })
            .ToDictionaryAsync(i => i.Id, i => (i.Code, i.Title, i.Unit ?? ""), ct);
    }

    private async Task<StockDocDto> ToDtoAsync(StockDoc doc, CancellationToken ct)
    {
        var items = await ItemsAsync(doc.Lines.Select(l => l.ItemId), ct);
        var warehouse = await db.Warehouses.Where(w => w.Id == doc.WarehouseId).Select(w => w.Title).FirstOrDefaultAsync(ct);
        var party = doc.PartyId is int pid ? await db.Parties.Where(p => p.Id == pid).Select(p => p.DisplayName).FirstOrDefaultAsync(ct) : null;
        var lineIds = doc.Lines.Select(l => l.Id).ToList();
        var links = await db.InvoiceLinksAsync(lineIds, ct);
        var returned = await ReturnedAsync(lineIds, null, ct);

        var sourceIds = doc.Lines.Where(l => l.SourceLineId is not null).Select(l => l.SourceLineId!.Value).ToList();
        var sources = await db.StockDocs.SelectMany(d => d.Lines.Where(l => sourceIds.Contains(l.Id)).Select(l => new { l.Id, d.DocType, d.DocNo, l.RowNo }))
            .ToDictionaryAsync(s => s.Id, s => $"{s.DocType.Title()} {s.DocNo:000000} ردیف {s.RowNo}", ct);
        var factorIds = doc.Lines.Where(l => l.CostFactorId is not null).Select(l => l.CostFactorId!.Value).ToList();
        var centerIds = doc.Lines.Where(l => l.CostCenterId is not null).Select(l => l.CostCenterId!.Value).ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        return new StockDocDto(doc.Id, doc.DocType, doc.DocNo, PersianDate.ToPersian(doc.DocDate), doc.WarehouseId, warehouse ?? "?",
            doc.PartyId, party, doc.Description,
            doc.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var item = items.TryGetValue(l.ItemId, out var found) ? found : (Code: "?", Title: "?", Unit: "");
                return new StockLineDto(l.Id, l.RowNo, l.ItemId, item.Code, item.Title, item.Unit, l.Quantity,
                    l.SourceLineId, l.SourceLineId is long s ? sources.GetValueOrDefault(s) : null,
                    l.CostFactorId, l.CostFactorId is int f ? factors.GetValueOrDefault(f) : null,
                    l.CostCenterId, l.CostCenterId is int c ? centers.GetValueOrDefault(c) : null,
                    l.EnteredPrice, l.UnitCost, l.Amount, l.Description, links.GetValueOrDefault(l.Id), returned.GetValueOrDefault(l.Id));
            }).ToList()) { SourceModule = doc.SourceModule, SourceId = doc.SourceId };
    }

    private async Task<StockDoc> LoadAsync(long id, bool tracking, CancellationToken ct, bool checkOwner = true)
    {
        var query = db.StockDocs.Include(d => d.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var doc = await query.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException($"سند انبار با شناسه {id} پیدا نشد.");
        if (checkOwner) access.DemandOwner(doc.CreatedBy);
        return doc;
    }

    private static void EnsureNotSourced(StockDoc doc)
    {
        if (doc.SourceModule is not null)
            throw new DomainException($"«{doc.DocType.Title()}» {doc.DocNo:000000} از گزارش تولید ساخته شده است؛ از همان گزارش اصلاح یا حذف کنید.");
    }
}

/// <summary>سندهای انبار تولید برای ماژول تولید (بدون کنترل مجوز؛ مجوز در خود گزارش تولید کنترل می‌شود).</summary>
public class ProductionStock(StockDocService stock, IInventoryDbContext db) : IProductionStock
{
    public async Task<long> SaveAsync(long? existingId, StockDocType type, DateOnly date, int warehouseId, string sourceModule, long sourceId,
        string? description, IReadOnlyList<ProductionStockLine> lines, CancellationToken ct)
    {
        if (!type.IsProduction()) throw new DomainException("فقط سند انبار تولید.");
        var request = new SaveStockDocRequest(type, PersianDate.ToPersian(date), warehouseId, null, description,
            lines.Select(l => new StockLineRequest(l.ItemId, l.Quantity, Description: l.Description)).ToList());
        if (existingId is long id)
        {
            await stock.UpdateCoreAsync(id, request, true, ct);
            return id;
        }
        return await stock.CreateCoreAsync(request, (sourceModule, sourceId), ct);
    }

    public Task DeleteAsync(long docId, CancellationToken ct) => stock.DeleteCoreAsync(docId, true, ct);

    public async Task<IReadOnlyDictionary<long, int>> NumbersAsync(IReadOnlyCollection<long> docIds, CancellationToken ct) =>
        await db.StockDocs.Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.DocNo, ct);
}

/// <summary>کاردکس تعدادی کالا و موجودی انبارها.</summary>
public class StockReportService(IInventoryDbContext db, IAccessControl access)
{
    /// <summary>کاردکس یک کالا در یک انبار یا همه انبارها؛ در هر روز رسیدها قبل از حواله‌ها.</summary>
    public async Task<KardexDto> KardexAsync(int itemId, int? warehouseId, string? fromDate, string? toDate, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Kardex);
        var item = await db.Items.Where(i => i.Id == itemId)
            .Select(i => new { i.Id, i.Code, i.Title, Unit = db.Units.Where(u => u.Id == i.UnitId).Select(u => u.Title).FirstOrDefault() })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("کالا پیدا نشد.");
        DateOnly? from = string.IsNullOrWhiteSpace(fromDate) ? null : PersianDate.Parse(fromDate);
        DateOnly? to = string.IsNullOrWhiteSpace(toDate) ? null : PersianDate.Parse(toDate);

        if (warehouseId is int wh) access.DemandScope(ScopeKind.Warehouse, wh);
        var query = db.StockDocs.AsNoTracking().Where(d => warehouseId == null || d.WarehouseId == warehouseId);
        if (access.WarehouseIds() is { } allowed) query = query.Where(d => allowed.Contains(d.WarehouseId));
        var lines = await query
            .SelectMany(d => d.Lines.Where(l => l.ItemId == itemId).Select(l => new
            {
                d.Id, d.DocType, d.DocNo, d.DocDate, d.WarehouseId, d.PartyId, Description = l.Description ?? d.Description, l.RowNo,
                In = (byte)d.DocType < 10 ? l.Quantity : 0m,
                Out = (byte)d.DocType < 10 ? 0m : l.Quantity,
                l.UnitCost, l.Amount
            }))
            .Where(x => to == null || x.DocDate <= to)
            .ToListAsync(ct);

        // ترتیب همان محاسبه بهای تمام‌شده: در هر روز رسیدهای مستقل، حواله‌ها، برگشت از فروش
        static int Phase(StockDocType t) => t switch
        {
            StockDocType.Opening or StockDocType.PurchaseReceipt => 0,
            StockDocType.SalesReturnReceipt => 2,
            _ => 1
        };
        var before = lines.Where(l => from is not null && l.DocDate < from).ToList();
        var opening = before.Sum(l => l.In - l.Out);
        decimal? openingAmount = before.Any(l => l.Amount is null) ? null : before.Sum(l => l.In > 0 ? l.Amount!.Value : -l.Amount!.Value);
        var period = lines.Where(l => from is null || l.DocDate >= from)
            .OrderBy(l => l.DocDate).ThenBy(l => Phase(l.DocType)).ThenBy(l => l.Id).ThenBy(l => l.RowNo).ToList();

        var warehouses = await db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        var partyIds = period.Where(l => l.PartyId is not null).Select(l => l.PartyId!.Value).Distinct().ToList();
        var parties = await db.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        var balance = opening;
        var balanceAmount = openingAmount;
        var rows = period.Select(l =>
        {
            balance += l.In - l.Out;
            balanceAmount = balanceAmount is { } b && l.Amount is { } a ? b + (l.In > 0 ? a : -a) : null;
            return new KardexRowDto(PersianDate.ToPersian(l.DocDate), l.DocType, l.Id, l.DocNo, warehouses.GetValueOrDefault(l.WarehouseId, "?"),
                l.PartyId is int p ? parties.GetValueOrDefault(p) : null, l.Description, l.In, l.Out, balance, l.UnitCost, l.Amount, balanceAmount);
        }).ToList();
        return new KardexDto(item.Id, item.Code, item.Title, item.Unit ?? "", opening, rows, rows.Sum(r => r.In), rows.Sum(r => r.Out), balance,
            openingAmount, balanceAmount);
    }

    /// <summary>موجودی کالاها در هر انبار تا پایان تاریخ (بدون تاریخ: همه).</summary>
    public async Task<List<StockBalanceDto>> BalancesAsync(int? warehouseId, string? date, string? term, bool includeZero, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Kardex);
        DateOnly? until = string.IsNullOrWhiteSpace(date) ? null : PersianDate.Parse(date);
        if (warehouseId is int wh) access.DemandScope(ScopeKind.Warehouse, wh);
        var allowed = access.WarehouseIds();
        var sums = await db.StockDocs.AsNoTracking()
            .Where(d => (warehouseId == null || d.WarehouseId == warehouseId) && (until == null || d.DocDate <= until))
            .Where(d => allowed == null || allowed.Contains(d.WarehouseId))
            .SelectMany(d => d.Lines.Select(l => new { l.ItemId, d.WarehouseId, Qty = (byte)d.DocType < 10 ? l.Quantity : -l.Quantity }))
            .GroupBy(x => new { x.ItemId, x.WarehouseId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Qty = g.Sum(x => x.Qty) })
            .ToListAsync(ct);
        if (!includeZero) sums = sums.Where(s => s.Qty != 0).ToList();

        var itemIds = sums.Select(s => s.ItemId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id, i.Code, i.Title,
                Group = db.ItemGroups.Where(g => g.Id == i.GroupId).Select(g => g.Title).FirstOrDefault(),
                Unit = db.Units.Where(u => u.Id == i.UnitId).Select(u => u.Title).FirstOrDefault()
            })
            .ToDictionaryAsync(i => i.Id, ct);
        var warehouses = await db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Title, ct);

        term = PersianText.NormalizeOrNull(term);
        return sums.Select(s =>
            {
                var i = items[s.ItemId];
                return new StockBalanceDto(i.Id, i.Code, i.Title, i.Group ?? "", i.Unit ?? "", s.WarehouseId,
                    warehouses.GetValueOrDefault(s.WarehouseId, "?"), s.Qty);
            })
            .Where(b => term is null || b.ItemTitle.Contains(term) || b.ItemCode.StartsWith(term) || b.GroupTitle.Contains(term))
            .OrderBy(b => b.ItemCode).ThenBy(b => b.Warehouse).ToList();
    }
}

/// <summary>محدوده داده انبار کاربر جاری (null = همه انبارها).</summary>
internal static class InventoryAccess
{
    public static List<int>? WarehouseIds(this IAccessControl access) => access.Allowed(ScopeKind.Warehouse)?.ToList();
}
