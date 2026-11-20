using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Inventory.Application;

/// <summary>گروه‌های کالا با معین‌های فروش، برگشت و خرید.</summary>
public class ItemGroupService(IInventoryDbContext db, IAccessControl access)
{
    /// <summary>فهرست گروه‌ها (آزاد؛ فرم‌های کالا، خرید و فروش لازمش دارند).</summary>
    public async Task<List<ItemGroupDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var groups = await db.ItemGroups.AsNoTracking().Where(g => includeInactive || g.IsActive).OrderBy(g => g.Title).ToListAsync(ct);
        var counts = await db.Items.GroupBy(i => i.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var ids = groups.SelectMany(g => new[] { g.SalesAccountId, g.SalesReturnAccountId, g.PurchaseAccountId ?? 0, g.CostAccountId ?? 0 }).Distinct().ToList();
        var accounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => $"{a.Code} - {a.Title}", ct);
        return groups.Select(g => new ItemGroupDto(g.Id, g.ParentId, g.Title, g.Kind,
            g.SalesAccountId, accounts.GetValueOrDefault(g.SalesAccountId, "?"),
            g.SalesReturnAccountId, accounts.GetValueOrDefault(g.SalesReturnAccountId, "?"),
            g.PurchaseAccountId, g.PurchaseAccountId is int p ? accounts.GetValueOrDefault(p, "?") : null,
            g.IsActive, counts.GetValueOrDefault(g.Id),
            g.CostAccountId, g.CostAccountId is int c ? accounts.GetValueOrDefault(c, "?") : null)).ToList();
    }

    public async Task<int> CreateAsync(SaveItemGroupRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Groups);
        await ValidateAccountsAsync(request, ct);
        var group = new ItemGroup(request.Title, request.Kind, request.SalesAccountId, request.SalesReturnAccountId,
            request.PurchaseAccountId, request.ParentId, await CostAccountAsync(request, null, ct));
        if (!request.IsActive) group.Deactivate();
        await EnsureUniqueAsync(group, ct);
        db.Add(group);
        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    /// <summary>اصلاح گروه. نوع گروهی که کالا دارد عوض نمی‌شود؛ تغییر معین‌ها روی فاکتورهای بعدی (و اصلاح فاکتورهای قبلی) اثر دارد.</summary>
    public async Task UpdateAsync(int id, SaveItemGroupRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Groups);
        var group = await db.ItemGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException("گروه کالا پیدا نشد.");
        if (group.Kind != request.Kind && await db.Items.AnyAsync(i => i.GroupId == id, ct))
            throw new DomainException($"گروه «{group.Title}» کالا دارد؛ نوعش (کالا/خدمت) عوض نمی‌شود.");
        if (request.ParentId is int parent && await IsDescendantAsync(parent, id, ct))
            throw new DomainException("گروه نمی‌تواند زیرمجموعه زیرگروه خودش باشد.");
        await ValidateAccountsAsync(request, ct);

        group.Update(request.Title, request.Kind, request.SalesAccountId, request.SalesReturnAccountId, request.PurchaseAccountId, request.ParentId,
            await CostAccountAsync(request, group, ct));
        if (request.IsActive) group.Activate(); else group.Deactivate();
        await EnsureUniqueAsync(group, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Groups);
        var group = await db.ItemGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException("گروه کالا پیدا نشد.");
        if (await db.Items.AnyAsync(i => i.GroupId == id, ct))
            throw new DomainException($"گروه «{group.Title}» کالا دارد و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        if (await db.ItemGroups.AnyAsync(g => g.ParentId == id, ct))
            throw new DomainException($"گروه «{group.Title}» زیرگروه دارد و حذف نمی‌شود.");
        db.Remove(group);
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateAccountsAsync(SaveItemGroupRequest r, CancellationToken ct)
    {
        var ids = new[] { r.SalesAccountId, r.SalesReturnAccountId, r.PurchaseAccountId ?? 0, r.CostAccountId ?? 0 }.Where(i => i > 0).Distinct().ToList();
        var found = await db.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        foreach (var missing in ids.Where(i => found.All(a => a.Id != i)))
            throw new DomainException("معین انتخاب‌شده پیدا نشد یا معین (سطح ۳) نیست.");
        if (found.FirstOrDefault(a => !a.IsActive) is { } inactive)
            throw new DomainException($"معین «{inactive.Code} - {inactive.Title}» غیرفعال است.");
    }

    /// <summary>معین بهای تمام‌شده گروه کالا: از درخواست، یا همان قبلی، یا پیش‌فرض 7001.</summary>
    private async Task<int?> CostAccountAsync(SaveItemGroupRequest r, ItemGroup? current, CancellationToken ct) =>
        r.Kind != ItemKind.Goods ? null
        : r.CostAccountId ?? current?.CostAccountId
          ?? await db.Accounts.Where(a => a.Code == "7001").Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

    private async Task<bool> IsDescendantAsync(int candidate, int ancestor, CancellationToken ct)
    {
        var parents = await db.ItemGroups.Select(g => new { g.Id, g.ParentId }).ToDictionaryAsync(g => g.Id, g => g.ParentId, ct);
        for (int? cur = candidate; cur is int c; cur = parents.GetValueOrDefault(c))
            if (c == ancestor) return true;
        return false;
    }

    private async Task EnsureUniqueAsync(ItemGroup group, CancellationToken ct)
    {
        if (await db.ItemGroups.AnyAsync(g => g.Id != group.Id && g.Title == group.Title, ct))
            throw new DomainException($"گروه کالای «{group.Title}» قبلاً تعریف شده است.");
    }
}

/// <summary>کالا و خدمات.</summary>
public class ItemService(IInventoryDbContext db, IAccessControl access, TimeProvider clock)
{
    /// <summary>جستجو (آزاد؛ فرم‌های خرید و فروش لازمش دارند): کد، عنوان، بارکد یا شناسه مودیان.</summary>
    public async Task<List<ItemDto>> SearchAsync(string? term, int? groupId, ItemKind? kind, bool includeInactive, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking();
        if (!includeInactive) query = query.Where(i => i.IsActive);
        if (groupId is int g) query = query.Where(i => i.GroupId == g);
        if (kind is { } k) query = query.Where(i => i.Kind == k);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            query = query.Where(i => i.Title.Contains(term) || i.Code.StartsWith(term) || i.Barcode == term || i.TaxItemId == term);
        var items = await query.OrderBy(i => i.Code).Take(term is null ? 5000 : 100).ToListAsync(ct);
        return await ToDtosAsync(items, ct);
    }

    public async Task<ItemDto> GetAsync(int id, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("کالا پیدا نشد.");
        return (await ToDtosAsync([item], ct))[0];
    }

    public async Task<List<ItemUnitDto>> GetUnitsAsync(CancellationToken ct) =>
        await db.Units.Where(u => u.IsActive).OrderBy(u => u.Title).Select(u => new ItemUnitDto(u.Id, u.Title)).ToListAsync(ct);

    /// <summary>کد پیشنهادی بعدی: بزرگترین کد عددی + ۱ (حداقل ۶ رقم).</summary>
    public async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await db.Items.Select(i => i.Code).ToListAsync(ct);
        var max = codes.Select(c => long.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return (max + 1).ToString("000000");
    }

    public async Task<int> CreateAsync(SaveItemRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.ItemCreate);
        var group = await GroupAsync(request.GroupId, null, ct);
        await EnsureUnitAsync(request.UnitId, null, ct);
        var code = string.IsNullOrWhiteSpace(request.Code) ? await NextCodeAsync(ct) : request.Code;
        var item = new Item(code, request.Title, group, request.UnitId, request.TaxItemId, request.VatExempt, request.Barcode,
            request.SalesPrice, request.Notes, clock.GetUtcNow().UtcDateTime);
        if (!request.IsActive) item.Deactivate();
        await EnsureUniqueAsync(item, ct);
        db.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>اصلاح کالا. کالایی که در فاکتور آمده نوعش (با تغییر گروه به گروه خدمات یا برعکس) عوض نمی‌شود.</summary>
    public async Task UpdateAsync(int id, SaveItemRequest request, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.ItemEdit);
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("کالا پیدا نشد.");
        var group = await GroupAsync(request.GroupId, item.GroupId, ct);
        if (group.Kind != item.Kind && await db.IsItemUsedAsync(id, ct))
            throw new DomainException($"«{item.Title}» در فاکتور آمده است؛ از کالا به خدمت یا برعکس تغییر نمی‌کند.");
        await EnsureUnitAsync(request.UnitId, item.UnitId, ct);

        item.Update(string.IsNullOrWhiteSpace(request.Code) ? item.Code : request.Code, request.Title, group, request.UnitId,
            request.TaxItemId, request.VatExempt, request.Barcode, request.SalesPrice, request.Notes);
        if (request.IsActive) item.Activate(); else item.Deactivate();
        await EnsureUniqueAsync(item, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف کالایی که در هیچ فاکتوری نیامده؛ در غیر این صورت فقط غیرفعال می‌شود.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.ItemDelete);
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("کالا پیدا نشد.");
        if (await db.IsItemUsedAsync(id, ct))
            throw new DomainException($"«{item.Title}» در فاکتور آمده است و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        db.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ItemGroup> GroupAsync(int groupId, int? currentGroupId, CancellationToken ct)
    {
        var group = await db.ItemGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
                    ?? throw new DomainException("گروه کالا انتخاب نشده یا پیدا نشد.");
        if (!group.IsActive && group.Id != currentGroupId) throw new DomainException($"گروه «{group.Title}» غیرفعال است.");
        return group;
    }

    private async Task EnsureUnitAsync(short unitId, short? current, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, ct) ?? throw new DomainException("واحد سنجش انتخاب نشده یا پیدا نشد.");
        if (!unit.IsActive && unit.Id != current) throw new DomainException($"واحد «{unit.Title}» غیرفعال است.");
    }

    private async Task EnsureUniqueAsync(Item item, CancellationToken ct)
    {
        if (await db.Items.AnyAsync(i => i.Id != item.Id && i.Code == item.Code, ct))
            throw new DomainException($"کد کالای «{item.Code}» قبلاً استفاده شده است.");
        if (item.Barcode is { } b && await db.Items.AnyAsync(i => i.Id != item.Id && i.Barcode == b, ct))
            throw new DomainException($"بارکد «{b}» برای کالای دیگری ثبت شده است.");
    }

    private async Task<List<ItemDto>> ToDtosAsync(List<Item> items, CancellationToken ct)
    {
        var groups = await db.ItemGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Title, ct);
        var units = await db.Units.ToDictionaryAsync(u => u.Id, u => u.Title, ct);
        return items.Select(i => new ItemDto(i.Id, i.Code, i.Title, i.Kind, i.GroupId, groups.GetValueOrDefault(i.GroupId, "?"),
            i.UnitId, units.GetValueOrDefault(i.UnitId, "?"), i.TaxItemId, i.VatExempt, i.Barcode, i.SalesPrice, i.IsActive, i.Notes)).ToList();
    }
}
