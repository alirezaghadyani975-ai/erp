using Erp.Production.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Production.Application;

/// <summary>
/// فرمول ساخت: محصول (نوعی که تولید می‌شود)، مواد (نوعی که در تولید مصرف می‌شود) با جایگزین‌ها، ردیف‌های ضایعات (نوع ضایعات)
/// و زمان استاندارد مراکز. هر محصول یک فرمول پیش‌فرض دارد؛ فرمولی که در گزارش تولید آمده حذف نمی‌شود (غیرفعال می‌شود).
/// </summary>
public class BomService(IProductionDbContext db, IAccessControl access, TimeProvider clock)
{
    // ---------------- فهرست‌های پایه (آزاد) ----------------

    public async Task<List<ProductionItemDto>> ItemsAsync(CancellationToken ct) =>
        await db.Items.Where(i => i.IsActive && (i.CanInput || i.CanOutput || i.IsScrap)).OrderBy(i => i.Code)
            .Select(i => new ProductionItemDto(i.Id, i.Code, i.Title, i.Unit, i.Category, i.CanInput, i.CanOutput, i.IsScrap)).ToListAsync(ct);

    public async Task<List<LookupDto>> WarehousesAsync(CancellationToken ct) =>
        await db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Code).Select(w => new LookupDto(w.Id, w.Title)).ToListAsync(ct);

    public async Task<List<LookupDto>> CentersAsync(CancellationToken ct) =>
        await db.Centers.Where(c => c.IsActive && c.IsProduction).OrderBy(c => c.Title).Select(c => new LookupDto(c.Id, c.Title)).ToListAsync(ct);

    // ---------------- فرمول ----------------

    public async Task<List<BomListItemDto>> ListAsync(int? productId, bool includeInactive, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.BomView);
        var query = db.Boms.AsNoTracking().Include(b => b.Materials).AsQueryable();
        if (productId is int p) query = query.Where(b => b.ProductId == p);
        if (!includeInactive) query = query.Where(b => b.IsActive);
        var boms = await query.OrderBy(b => b.Code).ToListAsync(ct);
        var ids = boms.Select(b => b.ProductId).Distinct().ToList();
        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var counts = await db.Reports.GroupBy(r => r.BomId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return boms.Select(b => new BomListItemDto(b.Id, b.Code, b.Title, b.ProductId, $"{items[b.ProductId].Code} {items[b.ProductId].Title}",
            b.BaseQuantity, items[b.ProductId].Unit, b.IsDefault, b.IsActive, Date(b.ValidFrom), Date(b.ValidTo), b.Materials.Count,
            counts.GetValueOrDefault(b.Id))).ToList();
    }

    public async Task<BomDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.BomView);
        var b = await LoadAsync(id, tracking: false, ct);
        return new BomDto(b.Id, b.Code, b.Title, b.ProductId, b.BaseQuantity, b.OutputWarehouseId, b.IssueWarehouseId, Date(b.ValidFrom), Date(b.ValidTo),
            b.IsDefault, b.IsActive, b.Notes,
            b.Materials.OrderBy(m => m.RowNo).Select(m => new BomMaterialInput(m.ItemId, m.Quantity, m.WarehouseId, m.LossPercent, m.Notes,
                m.Substitutes.OrderBy(s => s.OptionNo).Select(s => new BomSubstituteInput(s.OptionNo, s.ItemId, s.Factor)).ToList())).ToList(),
            b.Scraps.OrderBy(s => s.RowNo).Select(s => new BomScrapInput(s.ItemId, s.Quantity, s.ScrapType)).ToList(),
            b.Operations.OrderBy(o => o.RowNo).Select(o => new BomOperationInput(o.CostCenterId, o.ManSeconds, o.MachineSeconds, o.Notes)).ToList(),
            await db.Reports.CountAsync(r => r.BomId == id, ct));
    }

    public async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await db.Boms.Select(b => b.Code).ToListAsync(ct);
        var max = codes.Select(c => long.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return (max + 1).ToString("0000");
    }

    public async Task<int> CreateAsync(SaveBomRequest r, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.BomCreate);
        var bom = new Bom(r.ProductId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(bom, r, ct);
        db.Add(bom);
        await db.SaveChangesAsync(ct);
        return bom.Id;
    }

    public async Task UpdateAsync(int id, SaveBomRequest r, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.BomEdit);
        var bom = await LoadAsync(id, tracking: true, ct);
        if (bom.ProductId != r.ProductId && await db.Reports.AnyAsync(x => x.BomId == id, ct))
            throw new DomainException($"فرمول {bom.Code} در گزارش تولید آمده است؛ محصولش عوض نمی‌شود (فرمول تازه بسازید).");
        await ApplyAsync(bom, r, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.BomDelete);
        var bom = await LoadAsync(id, tracking: true, ct);
        if (await db.Reports.AnyAsync(x => x.BomId == id, ct))
            throw new DomainException($"فرمول {bom.Code} در گزارش تولید آمده است و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(bom);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Bom bom, SaveBomRequest r, CancellationToken ct)
    {
        DateOnly? Parse(string? s) => string.IsNullOrWhiteSpace(s) ? null : PersianDate.Parse(s);
        bom.Update(r.Code, r.Title, r.ProductId, r.BaseQuantity, r.OutputWarehouseId, r.IssueWarehouseId, Parse(r.ValidFrom), Parse(r.ValidTo),
            r.IsDefault, r.IsActive, r.Notes);
        bom.SetMaterials(r.Materials);
        bom.SetScraps(r.Scraps);
        bom.SetOperations(r.Operations);

        if (await db.Boms.AnyAsync(b => b.Id != bom.Id && b.Code == bom.Code, ct))
            throw new DomainException($"فرمول با کد {bom.Code} قبلاً تعریف شده است.");

        var itemIds = bom.Materials.Select(m => m.ItemId).Concat(bom.Materials.SelectMany(m => m.Substitutes).Select(s => s.ItemId))
            .Concat(bom.Scraps.Select(s => s.ItemId)).Append(bom.ProductId).Distinct().ToList();
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        ProductionItemRef Item(int id) => items.GetValueOrDefault(id) ?? throw new DomainException("کالای فرمول پیدا نشد.");

        var product = Item(bom.ProductId);
        if (!product.CanOutput)
            throw new DomainException($"«{product.Title}» از نوع «{product.Category}» است؛ محصول فرمول باید محصول یا نیم‌ساخته باشد.");
        foreach (var m in bom.Materials)
        {
            var item = Item(m.ItemId);
            if (!item.CanInput) throw new DomainException($"ردیف مواد {m.RowNo}: «{item.Title}» از نوع «{item.Category}» است و در تولید مصرف نمی‌شود.");
            foreach (var s in m.Substitutes)
            {
                var sub = Item(s.ItemId);
                if (!sub.CanInput) throw new DomainException($"ردیف مواد {m.RowNo}: جایگزین «{sub.Title}» از نوع «{sub.Category}» است و در تولید مصرف نمی‌شود.");
            }
        }
        foreach (var s in bom.Scraps)
        {
            var item = Item(s.ItemId);
            if (!item.IsScrap) throw new DomainException($"ردیف ضایعات {s.RowNo}: «{item.Title}» از نوع «ضایعات» نیست.");
        }

        var whIds = bom.Materials.Select(m => m.WarehouseId).OfType<int>().Append(bom.OutputWarehouseId).Append(bom.IssueWarehouseId).Distinct().ToList();
        if (await db.Warehouses.CountAsync(w => whIds.Contains(w.Id), ct) != whIds.Count) throw new DomainException("انبار فرمول پیدا نشد.");
        var centerIds = bom.Operations.Select(o => o.CostCenterId).Distinct().ToList();
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToListAsync(ct);
        if (centers.Count != centerIds.Count) throw new DomainException("مرکز هزینه زمان استاندارد پیدا نشد.");
        if (centers.FirstOrDefault(c => !c.IsProduction) is { } nonProd)
            throw new DomainException($"مرکز «{nonProd.Title}» تولیدی نیست؛ زمان استاندارد فقط برای مراکز تولیدی ثبت می‌شود.");

        await EnsureNoCycleAsync(bom, ct);

        if (bom.IsDefault)
            foreach (var other in await db.Boms.Where(b => b.ProductId == bom.ProductId && b.Id != bom.Id && b.IsDefault).ToListAsync(ct))
                other.ClearDefault();
    }

    /// <summary>
    /// دور در فرمول‌های چندسطحی: اگر یکی از مواد (یا جایگزین‌ها)، با فرمول‌های فعال خودش، به همین محصول برسد.
    /// </summary>
    private async Task EnsureNoCycleAsync(Bom bom, CancellationToken ct)
    {
        var graph = (await db.Boms.AsNoTracking().Include(b => b.Materials).ThenInclude(m => m.Substitutes)
                .Where(b => b.IsActive && b.Id != bom.Id).ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.SelectMany(b => b.Materials.Select(m => m.ItemId).Concat(b.Materials.SelectMany(m => m.Substitutes).Select(s => s.ItemId))).ToHashSet());
        var start = bom.Materials.Select(m => m.ItemId).Concat(bom.Materials.SelectMany(m => m.Substitutes).Select(s => s.ItemId)).ToHashSet();
        var seen = new HashSet<int>();
        var stack = new Stack<int>(start);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == bom.ProductId) throw new DomainException("فرمول دور دارد: یکی از مواد (با فرمول‌های خودش) به همین محصول می‌رسد.");
            if (!seen.Add(id)) continue;
            foreach (var next in graph.GetValueOrDefault(id) ?? []) stack.Push(next);
        }
    }

    private async Task<Bom> LoadAsync(int id, bool tracking, CancellationToken ct)
    {
        var query = db.Boms.Include(b => b.Materials).ThenInclude(m => m.Substitutes).Include(b => b.Scraps).Include(b => b.Operations).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("فرمول ساخت پیدا نشد.");
    }

    private static string? Date(DateOnly? d) => d is { } x ? PersianDate.ToPersian(x) : null;
}
