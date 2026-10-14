using Erp.Costing.Application.Abstractions;
using Erp.Costing.Contracts;
using Erp.Costing.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Costing.Application.Factors;

/// <summary>
/// عوامل هزینه. هر عامل یک گروه (ماهیت) دارد و یک تفصیلی (گروه «عوامل هزینه») در حسابداری
/// که در همان تراکنش ساخته و هماهنگ می‌شود.
/// </summary>
public class CostFactorService(ICostingDbContext db, IUnitOfWork uow, ICostDetailLinks links)
{
    public async Task<List<CostFactorDto>> SearchAsync(string? term, CostNature? nature, bool includeInactive, CancellationToken ct)
    {
        var query = db.CostFactors.AsNoTracking();
        if (!includeInactive) query = query.Where(f => f.IsActive);
        if (nature is not null) query = query.Where(f => f.Nature == nature);
        term = PersianText.NormalizeOrNull(term);

        var rows = await query.OrderBy(f => f.Nature).ThenBy(f => f.Title).ToListAsync(ct);
        var codes = await links.GetCodesAsync(CostDetailKind.Factor, null, ct);
        return rows
            .Select(f => new CostFactorDto(f.Id, codes.GetValueOrDefault(f.Id), f.Title, f.Nature, f.Behavior,
                f.VariablePercent, f.IsActive, f.Notes))
            .Where(d => term is null || d.Title.Contains(term) || (d.Code?.StartsWith(term) ?? false))
            .OrderBy(d => d.Code)
            .ToList();
    }

    public async Task<int> CreateAsync(SaveCostFactorRequest request, CancellationToken ct)
    {
        var factor = new CostFactor(request.Title, request.Nature, request.Behavior, request.VariablePercent, request.Notes);
        if (!request.IsActive) factor.Deactivate();
        await EnsureUniqueAsync(factor, ct);

        await using var tx = await uow.BeginAsync(ct);
        db.Add(factor);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(factor), ct);
        await tx.CommitAsync(ct);
        return factor.Id;
    }

    /// <summary>ویرایش عامل. عاملی که در سند آمده گروهش عوض نمی‌شود (سندهای قبلی در کل دیگری‌اند).</summary>
    public async Task UpdateAsync(int id, SaveCostFactorRequest request, CancellationToken ct)
    {
        var factor = await db.CostFactors.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("عامل هزینه پیدا نشد.");
        if (factor.Nature != request.Nature && await links.IsUsedAsync(CostDetailKind.Factor, id, ct))
            throw new DomainException($"عامل «{factor.Title}» در سند آمده است؛ گروه هزینه‌اش («{Titles(factor.Nature)}») عوض نمی‌شود.");

        factor.Update(request.Title, request.Nature, request.Behavior, request.VariablePercent, request.Notes);
        if (request.IsActive) factor.Activate(); else factor.Deactivate();
        await EnsureUniqueAsync(factor, ct);

        await using var tx = await uow.BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(factor), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>حذف عاملی که در سند نیامده؛ تفصیلی‌اش هم حذف می‌شود.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var factor = await db.CostFactors.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("عامل هزینه پیدا نشد.");
        await using var tx = await uow.BeginAsync(ct);
        await links.DeleteAsync(CostDetailKind.Factor, id, ct);
        db.Remove(factor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static CostDetailSnapshot Snapshot(CostFactor f) =>
        new(CostDetailKind.Factor, f.Id, f.Title, f.IsActive, f.Nature, null);

    private async Task EnsureUniqueAsync(CostFactor factor, CancellationToken ct)
    {
        if (await db.CostFactors.AnyAsync(f => f.Id != factor.Id && f.Title == factor.Title, ct))
            throw new DomainException($"عامل هزینه «{factor.Title}» قبلاً تعریف شده است.");
    }

    private static string Titles(CostNature nature) => nature switch
    {
        CostNature.Payroll => "حقوق و دستمزد",
        CostNature.Personnel => "هزینه کارکنان",
        CostNature.Depreciation => "استهلاک",
        CostNature.OtherOperating => "سایر هزینه‌های عملیاتی",
        _ => "هزینه‌های مالی"
    };
}
