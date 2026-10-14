using Erp.Costing.Application.Abstractions;
using Erp.Costing.Contracts;
using Erp.Costing.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Costing.Application.Setup;

/// <summary>انواع هزینه (زیر گروه‌های ماهیت) و انواع مرکز هزینه (با وظیفه).</summary>
public class CostSetupService(ICostingDbContext db, IUnitOfWork uow, ICostDetailLinks links)
{
    // ------------------------------------------------------------------ انواع هزینه

    public async Task<List<ExpenseTypeDto>> GetExpenseTypesAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.CostFactors.GroupBy(f => f.ExpenseTypeId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var types = await db.ExpenseTypes.AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Nature).ThenBy(t => t.SortOrder).ThenBy(t => t.Title).ToListAsync(ct);
        return types.Select(t => new ExpenseTypeDto(t.Id, t.Title, t.Nature, t.SortOrder, t.IsActive, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<short> CreateExpenseTypeAsync(SaveExpenseTypeRequest request, CancellationToken ct)
    {
        var type = new ExpenseType(request.Title, request.Nature, request.SortOrder);
        if (!request.IsActive) type.Deactivate();
        await EnsureUniqueAsync(type, ct);
        db.Add(type);
        await db.SaveChangesAsync(ct);
        return type.Id;
    }

    /// <summary>
    /// ویرایش نوع هزینه. تغییر گروه، ماهیت همه عوامل این نوع را عوض می‌کند؛ اگر عاملی در سند آمده باشد ممنوع است.
    /// </summary>
    public async Task UpdateExpenseTypeAsync(short id, SaveExpenseTypeRequest request, CancellationToken ct)
    {
        var type = await db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع هزینه پیدا نشد.");
        var natureChanged = type.Nature != request.Nature;
        var factors = natureChanged ? await db.CostFactors.Where(f => f.ExpenseTypeId == id).ToListAsync(ct) : [];
        foreach (var f in factors)
            if (await links.IsUsedAsync(CostDetailKind.Factor, f.Id, ct))
                throw new DomainException($"عامل «{f.Title}» از این نوع در سند آمده است؛ گروه هزینه نوع عوض نمی‌شود.");

        await using var tx = await uow.BeginAsync(ct);
        type.Update(request.Title, request.Nature, request.SortOrder);
        if (request.IsActive) type.Activate(); else type.Deactivate();
        await EnsureUniqueAsync(type, ct);
        await db.SaveChangesAsync(ct);
        foreach (var f in factors)
            await links.SyncAsync(new CostDetailSnapshot(CostDetailKind.Factor, f.Id, f.Title, f.IsActive, type.Nature, null), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteExpenseTypeAsync(short id, CancellationToken ct)
    {
        var type = await db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع هزینه پیدا نشد.");
        var count = await db.CostFactors.CountAsync(f => f.ExpenseTypeId == id, ct);
        if (count > 0)
            throw new DomainException($"نوع «{type.Title}» {count} عامل هزینه دارد و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(ExpenseType type, CancellationToken ct)
    {
        if (await db.ExpenseTypes.AnyAsync(t => t.Id != type.Id && t.Title == type.Title, ct))
            throw new DomainException($"نوع هزینه «{type.Title}» قبلاً تعریف شده است.");
    }

    // ------------------------------------------------------------------ انواع مرکز

    public async Task<List<CostCenterTypeDto>> GetCenterTypesAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.CostCenters.GroupBy(c => c.CenterTypeId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var types = await db.CostCenterTypes.AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Function).ThenBy(t => t.SortOrder).ThenBy(t => t.Title).ToListAsync(ct);
        return types.Select(t => new CostCenterTypeDto(t.Id, t.Title, t.Function, t.SortOrder, t.IsActive, t.IsSystem,
            counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<short> CreateCenterTypeAsync(SaveCostCenterTypeRequest request, CancellationToken ct)
    {
        var type = new CostCenterType(request.Title, request.Function, request.SortOrder);
        if (!request.IsActive) type.Deactivate();
        await EnsureUniqueAsync(type, ct);
        db.Add(type);
        await db.SaveChangesAsync(ct);
        return type.Id;
    }

    /// <summary>تغییر وظیفه نوع مرکز، وظیفه همه مراکز آن را عوض می‌کند؛ اگر مرکزی در سند آمده باشد ممنوع است.</summary>
    public async Task UpdateCenterTypeAsync(short id, SaveCostCenterTypeRequest request, CancellationToken ct)
    {
        var type = await db.CostCenterTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع مرکز پیدا نشد.");
        var functionChanged = type.Function != request.Function;
        var centers = functionChanged ? await db.CostCenters.Include(c => c.Allocations).Where(c => c.CenterTypeId == id).ToListAsync(ct) : [];
        foreach (var c in centers)
            if (await links.IsUsedAsync(CostDetailKind.Center, c.Id, ct))
                throw new DomainException($"مرکز «{c.Title}» از این نوع در سند آمده است؛ وظیفه نوع عوض نمی‌شود.");
        if (functionChanged && centers.Any(c => c.Allocations.Count > 0 || c.AllocationBaseId is not null || c.AbsorptionBaseId is not null))
            throw new DomainException("مراکز این نوع تنظیمات تسهیم یا جذب دارند؛ ابتدا آن‌ها را پاک کنید یا نوع جدیدی بسازید.");

        await using var tx = await uow.BeginAsync(ct);
        type.Update(request.Title, request.Function, request.SortOrder);
        if (request.IsActive) type.Activate(); else type.Deactivate();
        await EnsureUniqueAsync(type, ct);
        await db.SaveChangesAsync(ct);
        foreach (var c in centers)
            await links.SyncAsync(new CostDetailSnapshot(CostDetailKind.Center, c.Id, c.Title, c.IsActive, null, type.Function), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteCenterTypeAsync(short id, CancellationToken ct)
    {
        var type = await db.CostCenterTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع مرکز پیدا نشد.");
        if (type.IsSystem)
            throw new DomainException("نوع‌های سیستمی حذف نمی‌شوند.");
        var count = await db.CostCenters.CountAsync(c => c.CenterTypeId == id, ct);
        if (count > 0)
            throw new DomainException($"نوع «{type.Title}» {count} مرکز هزینه دارد و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(CostCenterType type, CancellationToken ct)
    {
        if (await db.CostCenterTypes.AnyAsync(t => t.Id != type.Id && t.Title == type.Title, ct))
            throw new DomainException($"نوع مرکز «{type.Title}» قبلاً تعریف شده است.");
    }

    // ------------------------------------------------------------------ مبنای تسهیم و جذب

    public Task<List<CostBaseDto>> GetCostBasesAsync(CancellationToken ct) =>
        db.LookupItems.AsNoTracking().Where(i => i.CategoryCode == CostLookupCodes.CostBase)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Title).Select(i => new CostBaseDto(i.Id, i.Title, i.IsActive)).ToListAsync(ct);
}
