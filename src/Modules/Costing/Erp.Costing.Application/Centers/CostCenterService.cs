using Erp.SharedKernel.Security;
using Erp.Costing.Application.Abstractions;
using Erp.Costing.Contracts;
using Erp.Costing.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Costing.Application.Centers;

/// <summary>
/// مراکز هزینه با نوع (وظیفه)، مسئول، مرکز بالادست و تنظیمات تسهیم/جذب.
/// هر مرکز یک تفصیلی (گروه «مراکز هزینه») در حسابداری دارد که در همان تراکنش هماهنگ می‌شود.
/// </summary>
public class CostCenterService(ICostingDbContext db, IUnitOfWork uow, ICostDetailLinks links, IAccessControl access)
{
    public async Task<List<CostCenterListItemDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var centers = await db.CostCenters.AsNoTracking().Include(c => c.Allocations)
            .Where(c => includeInactive || c.IsActive).ToListAsync(ct);
        var types = await db.CostCenterTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var titles = await db.CostCenters.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var baseIds = centers.SelectMany(c => new[] { c.AllocationBaseId, c.AbsorptionBaseId }).OfType<int>().Distinct().ToList();
        var bases = await db.LookupItems.AsNoTracking().Where(i => baseIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);
        var managerIds = centers.Select(c => c.ManagerPersonId).OfType<int>().Distinct().ToList();
        var managers = await db.Persons.AsNoTracking().Where(p => managerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var codes = await links.GetCodesAsync(CostDetailKind.Center, null, ct);

        return centers.Select(c =>
            {
                var t = types[c.CenterTypeId];
                return new CostCenterListItemDto(c.Id, codes.GetValueOrDefault(c.Id), c.Title, t.Id, t.Title, t.Function,
                    c.ParentId, c.ParentId is int p ? titles.GetValueOrDefault(p) : null,
                    c.ManagerPersonId is int m ? managers.GetValueOrDefault(m) : null,
                    c.AllocationBaseId is int ab ? bases.GetValueOrDefault(ab) : null, c.AllocationOrder,
                    c.AbsorptionBaseId is int sb ? bases.GetValueOrDefault(sb) : null,
                    c.Allocations.Count, c.IsActive);
            })
            .OrderBy(c => c.Function).ThenBy(c => c.Code).ToList();
    }

    public async Task<CostCenterDetailsDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(CostingPermissions.CenterView);
        var c = await db.CostCenters.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("مرکز هزینه پیدا نشد.");
        var codes = await links.GetCodesAsync(CostDetailKind.Center, [id], ct);
        var manager = c.ManagerPersonId is int m
            ? await db.Persons.Where(p => p.Id == m).Select(p => p.DisplayName).FirstOrDefaultAsync(ct)
            : null;
        return new CostCenterDetailsDto
        {
            Id = c.Id, Code = codes.GetValueOrDefault(c.Id), Title = c.Title, CenterTypeId = c.CenterTypeId, ParentId = c.ParentId,
            ManagerPersonId = c.ManagerPersonId, ManagerName = manager, AllocationBaseId = c.AllocationBaseId,
            AllocationOrder = c.AllocationOrder, AbsorptionBaseId = c.AbsorptionBaseId, IsActive = c.IsActive, Notes = c.Notes,
            Allocations = c.Allocations.OrderByDescending(a => a.Percent)
                .Select(a => new AllocationTargetDto { CenterId = a.TargetCenterId, Percent = a.Percent }).ToList()
        };
    }

    public async Task<int> CreateAsync(SaveCostCenterRequest request, CancellationToken ct)
    {
        access.Demand(CostingPermissions.CenterCreate);
        var type = await LoadTypeAsync(request.CenterTypeId, ct);
        var center = new CostCenter(request.Title, type);
        await ApplyAsync(center, type, request, ct);

        await using var tx = await uow.BeginAsync(ct);
        db.Add(center);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(center, type), ct);
        await tx.CommitAsync(ct);
        return center.Id;
    }

    public async Task UpdateAsync(int id, SaveCostCenterRequest request, CancellationToken ct)
    {
        access.Demand(CostingPermissions.CenterEdit);
        var center = await db.CostCenters.Include(c => c.Allocations).FirstOrDefaultAsync(c => c.Id == id, ct)
                     ?? throw new NotFoundException("مرکز هزینه پیدا نشد.");
        var oldType = await LoadTypeAsync(center.CenterTypeId, ct);
        var type = await LoadTypeAsync(request.CenterTypeId, ct);
        if (oldType.Function != type.Function && await links.IsUsedAsync(CostDetailKind.Center, id, ct))
            throw new DomainException($"مرکز «{center.Title}» در سند آمده است؛ نوعش فقط به نوعی با همان وظیفه عوض می‌شود.");

        center.SetType(request.Title, type);
        await ApplyAsync(center, type, request, ct);

        await using var tx = await uow.BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(center, type), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>حذف مرکزی که در سند نیامده، زیرمرکز ندارد و گیرنده تسهیم مرکز دیگری نیست.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(CostingPermissions.CenterDelete);
        var center = await db.CostCenters.Include(c => c.Allocations).FirstOrDefaultAsync(c => c.Id == id, ct)
                     ?? throw new NotFoundException("مرکز هزینه پیدا نشد.");
        if (await db.CostCenters.AnyAsync(c => c.ParentId == id, ct))
            throw new DomainException($"مرکز «{center.Title}» زیرمرکز دارد؛ ابتدا آن‌ها را جابه‌جا یا حذف کنید.");
        var sources = await db.CostCenters.Where(c => c.Allocations.Any(a => a.TargetCenterId == id)).Select(c => c.Title).ToListAsync(ct);
        if (sources.Count > 0)
            throw new DomainException($"مرکز «{center.Title}» گیرنده تسهیم {string.Join("، ", sources)} است؛ ابتدا از آن‌جا بردارید.");

        await using var tx = await uow.BeginAsync(ct);
        await links.DeleteAsync(CostDetailKind.Center, id, ct);
        db.Remove(center);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ApplyAsync(CostCenter center, CostCenterType type, SaveCostCenterRequest r, CancellationToken ct)
    {
        if (r.ParentId is int parentId)
        {
            if (!await db.CostCenters.AnyAsync(c => c.Id == parentId, ct))
                throw new NotFoundException("مرکز بالادست پیدا نشد.");
            if (center.Id != 0 && await IsDescendantAsync(parentId, center.Id, ct))
                throw new DomainException("مرکز بالادست نمی‌تواند زیرمجموعه خود این مرکز باشد.");
        }
        if (r.ManagerPersonId is int managerId && !await db.Persons.AnyAsync(p => p.Id == managerId, ct))
            throw new NotFoundException("مسئول مرکز پیدا نشد.");
        await CheckBaseAsync(r.AllocationBaseId, "مبنای تسهیم", ct);
        await CheckBaseAsync(r.AbsorptionBaseId, "مبنای جذب", ct);

        var rows = r.Allocations.Where(a => a.CenterId is not null || a.Percent != 0).ToList();
        if (rows.Any(a => a.CenterId is null))
            throw new DomainException("مرکز گیرنده همه ردیف‌های تسهیم را انتخاب کنید.");
        var targetIds = rows.Select(a => a.CenterId!.Value).Distinct().ToList();
        var targets = await (from c in db.CostCenters
                             join t in db.CostCenterTypes on c.CenterTypeId equals t.Id
                             where targetIds.Contains(c.Id)
                             select new { c.Id, c.Title, t.Function }).ToDictionaryAsync(x => x.Id, ct);
        var list = rows.Select(a => targets.TryGetValue(a.CenterId!.Value, out var t)
                ? (t.Id, t.Function, t.Title, a.Percent)
                : throw new NotFoundException("مرکز گیرنده تسهیم پیدا نشد."))
            .ToList();

        center.SetDetails(r.ParentId, r.ManagerPersonId, r.Notes);
        center.SetCosting(type.Function, r.AllocationBaseId, r.AllocationOrder, r.AbsorptionBaseId, list);
        if (r.IsActive) center.Activate(); else center.Deactivate();

        if (await db.CostCenters.AnyAsync(c => c.Id != center.Id && c.Title == center.Title, ct))
            throw new DomainException($"مرکز هزینه «{center.Title}» قبلاً تعریف شده است.");
    }

    private async Task CheckBaseAsync(int? id, string label, CancellationToken ct)
    {
        if (id is int baseId && !await db.LookupItems.AnyAsync(i => i.Id == baseId && i.CategoryCode == CostLookupCodes.CostBase, ct))
            throw new DomainException($"{label} از فهرست «مبنای تسهیم و جذب» نیست.");
    }

    private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId, CancellationToken ct)
    {
        int? current = candidateId;
        for (var guard = 0; current is int c && guard < 50; guard++)
        {
            if (c == ancestorId) return true;
            current = await db.CostCenters.Where(x => x.Id == c).Select(x => x.ParentId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    private async Task<CostCenterType> LoadTypeAsync(short? id, CancellationToken ct) =>
        id is short typeId
            ? await db.CostCenterTypes.FirstOrDefaultAsync(t => t.Id == typeId, ct) ?? throw new NotFoundException("نوع مرکز پیدا نشد.")
            : throw new DomainException("نوع مرکز هزینه را انتخاب کنید.");

    private static CostDetailSnapshot Snapshot(CostCenter c, CostCenterType t) =>
        new(CostDetailKind.Center, c.Id, c.Title, c.IsActive, null, t.Function);
}
