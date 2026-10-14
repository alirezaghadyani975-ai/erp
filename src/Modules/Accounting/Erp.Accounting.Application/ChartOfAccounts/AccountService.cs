using Erp.SharedKernel.Text;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.ChartOfAccounts;

public class AccountService(IAccountingDbContext db)
{
    public async Task<List<AccountNodeDto>> GetTreeAsync(CancellationToken ct)
    {
        var all = await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        var nodes = all.ToDictionary(a => a.Id, a => new AccountNodeDto(
            a.Id, a.Code, a.Title, a.Level, a.ParentId, a.Nature, a.Type, a.IsStandard, a.IsActive, []));

        var roots = new List<AccountNodeDto>();
        foreach (var node in nodes.Values)
        {
            if (node.ParentId is int parentId && nodes.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots;
    }

    public async Task<AccountDto> GetAsync(int id, CancellationToken ct) => ToDto(await LoadAsync(id, ct));

    public async Task<AccountDto> GetByCodeAsync(string code, CancellationToken ct)
    {
        var id = await db.Accounts.Where(a => a.Code == code).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct)
                 ?? throw new NotFoundException($"حساب با کد {code} پیدا نشد.");
        return await GetAsync(id, ct);
    }

    /// <summary>جستجوی معین‌های فعال (قابل استفاده در سند) با کد یا عنوان، همراه قواعد تفصیلی.</summary>
    public async Task<List<AccountDto>> SearchPostableAsync(string? term, CancellationToken ct)
    {
        var query = db.Accounts.AsNoTracking().Include(a => a.DetailLevels).ThenInclude(l => l.AllowedTypes)
            .Include(a => a.DetailLevels).ThenInclude(l => l.AllowedRoles)
            .Where(a => a.Level == AccountLevel.Subsidiary && a.IsActive);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            query = query.Where(a => a.Code.StartsWith(term) || a.Title.Contains(term));

        var list = await query.OrderBy(a => a.Code).Take(30).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<int> CreateAsync(CreateAccountRequest request, CancellationToken ct)
    {
        var parent = await LoadAsync(request.ParentId, ct);
        if (await db.Accounts.AnyAsync(a => a.Code == request.Code.Trim(), ct))
            throw new DomainException($"کد حساب {request.Code} تکراری است.");

        var type = request.Type;
        if (type is null && parent.Level == AccountLevel.General)
            type = await InferTypeAsync(parent, ct);

        var account = Account.CreateCustom(parent, request.Code, request.Title, request.Nature, type);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    public async Task UpdateAsync(int id, UpdateAccountRequest request, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        account.Rename(request.Title);
        if (request.IsActive) account.Activate(); else account.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>تعریف یا تغییر یک سطح تفصیلی برای معین.</summary>
    public async Task SetDetailLevelAsync(int id, int levelNo, SetDetailLevelRequest request, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        var typeIds = request.DetailTypeIds.Distinct().ToList();
        var existing = await db.DetailTypes.CountAsync(t => typeIds.Contains(t.Id), ct);
        if (existing != typeIds.Count)
            throw new DomainException("یک یا چند نوع تفصیلی انتخاب‌شده وجود ندارد.");

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        if (await db.RoleTypeRefs.CountAsync(r => roleIds.Contains(r.Id), ct) != roleIds.Count)
            throw new DomainException("یک یا چند نقش انتخاب‌شده وجود ندارد.");

        var inUse = await TypesInUseAsync(id, levelNo, ct);
        account.SetDetailLevel(levelNo, request.IsRequired, typeIds,
            new Dictionary<int, IReadOnlySet<short>> { [levelNo] = inUse }, roleIds);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف حساب اختصاصی (نه استاندارد) که زیرحساب و گردش ندارد.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        if (account.IsStandard)
            throw new DomainException("حساب استاندارد کدینگ حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        if (await db.Accounts.AnyAsync(a => a.ParentId == id, ct))
            throw new DomainException($"حساب {account.Code} زیرحساب دارد؛ ابتدا آن‌ها را حذف کنید.");
        if (await db.VoucherLines.AnyAsync(l => l.AccountId == id, ct))
            throw new DomainException($"حساب {account.Code} در سند استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveDetailLevelAsync(int id, int levelNo, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        var inUse = await TypesInUseAsync(id, levelNo, ct);
        account.RemoveDetailLevel(levelNo, inUse.Count > 0);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Account> LoadAsync(int id, CancellationToken ct) =>
        await db.Accounts.Include(a => a.DetailLevels).ThenInclude(l => l.AllowedTypes)
            .Include(a => a.DetailLevels).ThenInclude(l => l.AllowedRoles)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException($"حساب با شناسه {id} پیدا نشد.");

    /// <summary>انواع تفصیلی‌ای که روی این معین در این سطح گردش دارند (در سندهای غیرباطل).</summary>
    private async Task<IReadOnlySet<short>> TypesInUseAsync(int accountId, int levelNo, CancellationToken ct)
    {
        var lines = db.VoucherLines.Where(l => l.AccountId == accountId
            && db.Vouchers.Any(v => v.Id == l.VoucherId && v.Status != VoucherStatus.Void));
        IQueryable<int?> detailIds = levelNo switch
        {
            1 => lines.Select(l => l.Detail1Id),
            2 => lines.Select(l => l.Detail2Id),
            3 => lines.Select(l => l.Detail3Id),
            4 => lines.Select(l => l.Detail4Id),
            5 => lines.Select(l => l.Detail5Id),
            6 => lines.Select(l => l.Detail6Id),
            _ => throw new DomainException($"سطح تفصیلی باید بین ۱ تا {Account.MaxDetailLevels} باشد.")
        };
        var types = await db.DetailAccounts
            .Where(d => detailIds.Contains(d.Id))
            .Select(d => d.DetailTypeId).Distinct().ToListAsync(ct);
        return types.ToHashSet();
    }

    /// <summary>نوع حساب معین جدید از معین‌های هم‌گروه گرفته می‌شود.</summary>
    private async Task<AccountType> InferTypeAsync(Account general, CancellationToken ct)
    {
        var groupId = general.ParentId;
        var type = await db.Accounts
            .Where(a => a.Level == AccountLevel.Subsidiary && a.Type != null
                        && db.Accounts.Any(k => k.Id == a.ParentId && k.ParentId == groupId))
            .Select(a => a.Type).FirstOrDefaultAsync(ct);
        return type ?? throw new DomainException("نوع حساب (ترازنامه‌ای، سود و زیانی، انتظامی) را مشخص کنید.");
    }

    private static AccountDto ToDto(Account a) => new(
        a.Id, a.Code, a.Title, a.Level, a.ParentId, a.Nature, a.Type, a.IsStandard, a.IsActive,
        a.DetailLevels.OrderBy(l => l.LevelNo)
            .Select(l => new DetailLevelDto(l.LevelNo, l.IsRequired, l.AllowedTypes.Select(t => t.DetailTypeId).ToList(),
                l.AllowedRoles.Select(r => r.RoleId).ToList()))
            .ToList());
}
