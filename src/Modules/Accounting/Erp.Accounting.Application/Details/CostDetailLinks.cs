using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.Details;
using Erp.Costing.Contracts;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Details;

/// <summary>
/// تفصیلی عوامل و مراکز هزینه برای ماژول بهای تمام‌شده. هر عامل یک تفصیلی در گروه «عوامل هزینه» و هر مرکز
/// یک تفصیلی در گروه «مراکز هزینه» دارد (SourceModule = costing). ماهیت عامل و وظیفه مرکز روی تفصیلی
/// نگه داشته می‌شود تا در ثبت سند کنترل شود.
/// </summary>
public class CostDetailLinks(IAccountingDbContext db) : ICostDetailLinks
{
    public const string SourceModule = "costing";

    public async Task SyncAsync(CostDetailSnapshot item, CancellationToken ct)
    {
        var detail = await FindAsync(item.Kind, item.Id, ct);
        if (detail is null)
        {
            var owner = Owner(item.Kind);
            var type = await db.DetailTypes.Where(t => t.IsActive && t.OwnerModule == owner).OrderBy(t => t.CodeFrom).FirstOrDefaultAsync(ct)
                       ?? throw new DomainException($"گروه تفصیلی فعال «{(item.Kind == CostDetailKind.Factor ? "عوامل هزینه" : "مراکز هزینه")}» تعریف نشده است (صفحه گروه‌های تفصیلی).");
            var code = await DetailService.NextCodeAsync(db, type, ct);
            detail = new DetailAccount(type, code, item.Title, null, SourceModule, item.Id);
            db.DetailAccounts.Add(detail);
        }

        if (detail.Title != item.Title) detail.Rename(item.Title);
        if (item.IsActive) detail.Activate(); else detail.Deactivate();
        detail.SetCostDimensions(item.Kind == CostDetailKind.Factor ? item.Nature : null,
                                 item.Kind == CostDetailKind.Center ? item.Function : null);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsUsedAsync(CostDetailKind kind, int id, CancellationToken ct)
    {
        var detail = await FindAsync(kind, id, ct);
        return detail is not null && await UsedAsync(detail.Id, ct);
    }

    public async Task DeleteAsync(CostDetailKind kind, int id, CancellationToken ct)
    {
        var detail = await FindAsync(kind, id, ct);
        if (detail is null) return;
        if (await UsedAsync(detail.Id, ct))
            throw new DomainException($"تفصیلی {detail.Code} در سند استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.DetailAccounts.Remove(detail);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, string>> GetCodesAsync(CostDetailKind kind, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var owner = Owner(kind);
        var query = from d in db.DetailAccounts.AsNoTracking()
                    join t in db.DetailTypes on d.DetailTypeId equals t.Id
                    where d.SourceModule == SourceModule && t.OwnerModule == owner && d.SourceEntityId != null
                    select new { Id = (int)d.SourceEntityId!.Value, d.Code };
        if (ids is not null)
        {
            var list = ids.Select(i => (long)i).ToList();
            query = from d in db.DetailAccounts.AsNoTracking()
                    join t in db.DetailTypes on d.DetailTypeId equals t.Id
                    where d.SourceModule == SourceModule && t.OwnerModule == owner && list.Contains(d.SourceEntityId!.Value)
                    select new { Id = (int)d.SourceEntityId!.Value, d.Code };
        }
        return await query.ToDictionaryAsync(x => x.Id, x => x.Code, ct);
    }

    private static string Owner(CostDetailKind kind) =>
        kind == CostDetailKind.Factor ? OwnerModules.CostFactors : OwnerModules.CostCenters;

    private Task<DetailAccount?> FindAsync(CostDetailKind kind, int id, CancellationToken ct)
    {
        var owner = Owner(kind);
        var typeIds = db.DetailTypes.Where(t => t.OwnerModule == owner).Select(t => t.Id);
        return db.DetailAccounts.FirstOrDefaultAsync(d => d.SourceModule == SourceModule && d.SourceEntityId == id
                                                          && typeIds.Contains(d.DetailTypeId), ct);
    }

    private Task<bool> UsedAsync(int detailId, CancellationToken ct) =>
        db.VoucherLines.AnyAsync(l => l.Detail1Id == detailId || l.Detail2Id == detailId || l.Detail3Id == detailId
                                      || l.Detail4Id == detailId || l.Detail5Id == detailId || l.Detail6Id == detailId, ct);
}
