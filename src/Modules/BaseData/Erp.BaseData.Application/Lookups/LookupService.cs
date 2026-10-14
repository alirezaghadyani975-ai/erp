using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Domain.Lookups;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Application.Lookups;

public sealed record LookupCategoryDto(short Id, string Code, string Title, short? ParentCategoryId, bool IsTree,
    string? Description, int ItemCount);

public sealed record LookupItemDto(int Id, short CategoryId, string? Code, string Title, int? ParentId, string? ParentTitle,
    int SortOrder, bool IsActive, bool IsSystem);

public sealed record SaveLookupItemRequest(short CategoryId, string Title, string? Code, int? ParentId, int SortOrder, bool IsActive);

/// <summary>
/// جدول پایه عمومی: فهرست‌ها و اقلامشان. اقلام سیستمی (مثل استان‌ها) ویرایش‌پذیرند ولی حذف نمی‌شوند.
/// قلمی که جایی استفاده شده حذف نمی‌شود؛ باید غیرفعال شود.
/// </summary>
public class LookupService(IBaseDataDbContext db)
{
    public async Task<List<LookupCategoryDto>> GetCategoriesAsync(CancellationToken ct)
    {
        var counts = await db.LookupItems.GroupBy(i => i.CategoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var categories = await db.LookupCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Title).ToListAsync(ct);
        return categories.Select(c => new LookupCategoryDto(c.Id, c.Code, c.Title, c.ParentCategoryId, c.IsTree, c.Description,
            counts.GetValueOrDefault(c.Id))).ToList();
    }

    /// <summary>اقلام یک فهرست (با کد فهرست)، اختیاری فقط زیر یک والد؛ برای انتخاب در فرم‌ها.</summary>
    public async Task<List<LookupItemDto>> GetItemsAsync(string categoryCode, int? parentId, bool includeInactive, CancellationToken ct)
    {
        var category = await db.LookupCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Code == categoryCode, ct)
                       ?? throw new NotFoundException($"فهرست «{categoryCode}» وجود ندارد.");
        return await ItemsAsync(category.Id, parentId, includeInactive, null, ct);
    }

    public Task<List<LookupItemDto>> GetItemsAsync(short categoryId, string? term, CancellationToken ct) =>
        ItemsAsync(categoryId, null, includeInactive: true, term, ct);

    private async Task<List<LookupItemDto>> ItemsAsync(short categoryId, int? parentId, bool includeInactive, string? term,
        CancellationToken ct)
    {
        var query = from i in db.LookupItems.AsNoTracking()
                    join p in db.LookupItems on i.ParentId equals p.Id into parents
                    from p in parents.DefaultIfEmpty()
                    where i.CategoryId == categoryId
                    select new { i, ParentTitle = p == null ? null : p.Title };
        if (parentId is int pid) query = query.Where(x => x.i.ParentId == pid);
        if (!includeInactive) query = query.Where(x => x.i.IsActive);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null) query = query.Where(x => x.i.Title.Contains(term) || x.i.Code!.StartsWith(term));

        return await query.OrderBy(x => x.i.SortOrder).ThenBy(x => x.i.Title)
            .Select(x => new LookupItemDto(x.i.Id, x.i.CategoryId, x.i.Code, x.i.Title, x.i.ParentId, x.ParentTitle,
                x.i.SortOrder, x.i.IsActive, x.i.IsSystem))
            .ToListAsync(ct);
    }

    public async Task<int> CreateItemAsync(SaveLookupItemRequest request, CancellationToken ct)
    {
        var (category, parent) = await ResolveAsync(request, ct);
        var item = new LookupItem(category, request.Title, request.Code, parent, request.SortOrder);
        if (!request.IsActive) item.Deactivate();
        await EnsureUniqueAsync(item, ct);
        db.LookupItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task UpdateItemAsync(int id, SaveLookupItemRequest request, CancellationToken ct)
    {
        var item = await db.LookupItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("قلم پیدا نشد.");
        if (item.CategoryId != request.CategoryId)
            throw new DomainException("فهرست قلم عوض نمی‌شود.");
        var (category, parent) = await ResolveAsync(request, ct);
        if (parent is not null && await IsDescendantAsync(parent.Id, id, ct))
            throw new DomainException("والد نمی‌تواند زیرمجموعه خود این قلم باشد.");
        item.Update(category, request.Title, request.Code, parent, request.SortOrder);
        if (request.IsActive) item.Activate(); else item.Deactivate();
        await EnsureUniqueAsync(item, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف قلم؛ اقلام سیستمی، اقلامی که زیرمجموعه دارند یا جایی استفاده شده‌اند حذف نمی‌شوند.</summary>
    public async Task DeleteItemAsync(int id, CancellationToken ct)
    {
        var item = await db.LookupItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("قلم پیدا نشد.");
        if (item.IsSystem)
            throw new DomainException($"«{item.Title}» قلم سیستمی است و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        if (await db.LookupItems.AnyAsync(i => i.ParentId == id, ct))
            throw new DomainException($"«{item.Title}» زیرمجموعه دارد؛ ابتدا آن‌ها را حذف یا جابه‌جا کنید.");
        db.LookupItems.Remove(item);
        await db.SaveChangesAsync(ct); // اگر در شخص یا نشانی استفاده شده باشد، کلید خارجی جلوی حذف را می‌گیرد
    }

    private async Task<(LookupCategory, LookupItem?)> ResolveAsync(SaveLookupItemRequest request, CancellationToken ct)
    {
        var category = await db.LookupCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
                       ?? throw new NotFoundException("فهرست پیدا نشد.");
        LookupItem? parent = null;
        if (request.ParentId is int pid)
            parent = await db.LookupItems.FirstOrDefaultAsync(i => i.Id == pid, ct) ?? throw new NotFoundException("والد پیدا نشد.");
        return (category, parent);
    }

    private async Task EnsureUniqueAsync(LookupItem item, CancellationToken ct)
    {
        if (await db.LookupItems.AnyAsync(i => i.Id != item.Id && i.CategoryId == item.CategoryId
                                               && i.ParentId == item.ParentId && i.Title == item.Title, ct))
            throw new DomainException($"«{item.Title}» در این فهرست وجود دارد.");
        if (item.Code is not null
            && await db.LookupItems.AnyAsync(i => i.Id != item.Id && i.CategoryId == item.CategoryId && i.Code == item.Code, ct))
            throw new DomainException($"کد «{item.Code}» در این فهرست تکراری است.");
    }

    private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId, CancellationToken ct)
    {
        var current = (int?)candidateId;
        for (var guard = 0; current is int c && guard < 50; guard++)
        {
            if (c == ancestorId) return true;
            current = await db.LookupItems.Where(i => i.Id == c).Select(i => i.ParentId).FirstOrDefaultAsync(ct);
        }
        return false;
    }
}
