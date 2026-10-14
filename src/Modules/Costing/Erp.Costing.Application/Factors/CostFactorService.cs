using Erp.Costing.Application.Abstractions;
using Erp.Costing.Contracts;
using Erp.Costing.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Costing.Application.Factors;

/// <summary>
/// عوامل هزینه. هر عامل یک تفصیلی (گروه «عوامل هزینه») در حسابداری دارد که در همان تراکنش ساخته و هماهنگ می‌شود.
/// </summary>
public class CostFactorService(ICostingDbContext db, IUnitOfWork uow, ICostDetailLinks links)
{
    public async Task<List<CostFactorDto>> SearchAsync(string? term, CostNature? nature, short? expenseTypeId, bool includeInactive,
        CancellationToken ct)
    {
        var query = from f in db.CostFactors.AsNoTracking()
                    join t in db.ExpenseTypes on f.ExpenseTypeId equals t.Id
                    select new { f, t };
        if (!includeInactive) query = query.Where(x => x.f.IsActive);
        if (nature is not null) query = query.Where(x => x.t.Nature == nature);
        if (expenseTypeId is not null) query = query.Where(x => x.f.ExpenseTypeId == expenseTypeId);
        term = PersianText.NormalizeOrNull(term);

        var rows = await query.OrderBy(x => x.t.Nature).ThenBy(x => x.t.SortOrder).ThenBy(x => x.f.Title).ToListAsync(ct);
        var codes = await links.GetCodesAsync(CostDetailKind.Factor, null, ct);
        return rows
            .Select(x => new CostFactorDto(x.f.Id, codes.GetValueOrDefault(x.f.Id), x.f.Title, x.t.Id, x.t.Title, x.t.Nature,
                x.f.Behavior, x.f.VariablePercent, x.f.IsActive, x.f.Notes))
            .Where(d => term is null || d.Title.Contains(term) || (d.Code?.StartsWith(term) ?? false))
            .OrderBy(d => d.Code)
            .ToList();
    }

    public async Task<int> CreateAsync(SaveCostFactorRequest request, CancellationToken ct)
    {
        var type = await LoadTypeAsync(request.ExpenseTypeId, ct);
        var factor = new CostFactor(request.Title, type, request.Behavior, request.VariablePercent, request.Notes);
        if (!request.IsActive) factor.Deactivate();
        await EnsureUniqueAsync(factor, ct);

        await using var tx = await uow.BeginAsync(ct);
        db.Add(factor);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(factor, type), ct);
        await tx.CommitAsync(ct);
        return factor.Id;
    }

    public async Task UpdateAsync(int id, SaveCostFactorRequest request, CancellationToken ct)
    {
        var factor = await db.CostFactors.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("عامل هزینه پیدا نشد.");
        var oldType = await LoadTypeAsync(factor.ExpenseTypeId, ct);
        var type = await LoadTypeAsync(request.ExpenseTypeId, ct);
        if (oldType.Nature != type.Nature && await links.IsUsedAsync(CostDetailKind.Factor, id, ct))
            throw new DomainException($"عامل «{factor.Title}» در سند آمده است؛ نوع هزینه فقط به نوعی از همان گروه «{Titles(oldType.Nature)}» عوض می‌شود.");

        factor.Update(request.Title, type, request.Behavior, request.VariablePercent, request.Notes);
        if (request.IsActive) factor.Activate(); else factor.Deactivate();
        await EnsureUniqueAsync(factor, ct);

        await using var tx = await uow.BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(Snapshot(factor, type), ct);
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

    private static CostDetailSnapshot Snapshot(CostFactor f, ExpenseType t) =>
        new(CostDetailKind.Factor, f.Id, f.Title, f.IsActive, t.Nature, null);

    private async Task<ExpenseType> LoadTypeAsync(short id, CancellationToken ct) =>
        await db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع هزینه پیدا نشد.");

    private async Task EnsureUniqueAsync(CostFactor factor, CancellationToken ct)
    {
        if (await db.CostFactors.AnyAsync(f => f.Id != factor.Id && f.Title == factor.Title, ct))
            throw new DomainException($"عامل هزینه «{factor.Title}» قبلاً تعریف شده است.");
    }

    private static string Titles(CostNature nature) => nature switch
    {
        CostNature.Payroll => "حقوق و دستمزد",
        CostNature.Depreciation => "استهلاک",
        CostNature.OtherOperating => "سایر هزینه‌های عملیاتی",
        _ => "هزینه‌های مالی"
    };
}
