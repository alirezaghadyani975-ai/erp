using Erp.SharedKernel.Text;
using Erp.Accounting.Application.Abstractions;
using Erp.BaseData.Contracts;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Details;

/// <param name="UsedCount">تعداد تفصیلی‌های این گروه.</param>
/// <param name="NextCode">کد بعدی که به تفصیلی تازه داده می‌شود (null اگر بازه پر باشد).</param>
public sealed record DetailTypeDto(short Id, string Title, string OwnerModule, PersonKind? PersonKind,
    string CodeFrom, string CodeTo, bool IsActive, int UsedCount = 0, string? NextCode = null,
    string? MinUsedCode = null, string? MaxUsedCode = null)
{
    public int CodeLength => CodeFrom.Length;
    public long Capacity => long.Parse(CodeTo) - long.Parse(CodeFrom) + 1;
}

public sealed record CreateDetailTypeRequest(string Title, string OwnerModule, string CodeFrom, string CodeTo, PersonKind? PersonKind);

/// <summary>مالک و نوع شخص فقط وقتی گروه هنوز تفصیلی ندارد عوض می‌شوند؛ بازه با حفظ کدهای موجود.</summary>
public sealed record UpdateDetailTypeRequest(string Title, string OwnerModule, PersonKind? PersonKind,
    string CodeFrom, string CodeTo, bool IsActive);

public sealed record DetailAccountDto(int Id, string Code, string Title, short DetailTypeId, int? PersonId, bool IsActive);

/// <param name="UsageCount">تعداد ردیف‌های سند که این تفصیلی را دارند.</param>
public sealed record DetailAccountRowDto(int Id, string Code, string Title, int? PersonId, bool IsActive, int UsageCount);

/// <summary>اگر Code خالی باشد، اولین کد آزاد بعد از آخرین کد بازه داده می‌شود.</summary>
public sealed record CreateDetailAccountRequest(short DetailTypeId, string Title, string? Code);

public class DetailService(IAccountingDbContext db)
{
    public async Task<List<DetailTypeDto>> GetTypesAsync(CancellationToken ct)
    {
        var usage = await db.DetailAccounts.AsNoTracking()
            .GroupBy(d => d.DetailTypeId)
            .Select(g => new { TypeId = g.Key, Count = g.Count(), Min = g.Min(d => d.Code), Max = g.Max(d => d.Code) })
            .ToDictionaryAsync(x => x.TypeId, ct);
        var types = await db.DetailTypes.AsNoTracking().OrderBy(t => t.CodeFrom.Length).ThenBy(t => t.CodeFrom).ToListAsync(ct);

        return types.Select(t =>
        {
            var u = usage.GetValueOrDefault(t.Id);
            string? next;
            try { next = t.NextCode(u?.Max); } catch (DomainException) { next = null; }
            return new DetailTypeDto(t.Id, t.Title, t.OwnerModule, t.PersonKind, t.CodeFrom, t.CodeTo, t.IsActive,
                u?.Count ?? 0, next, u?.Min, u?.Max);
        }).ToList();
    }

    public async Task<short> CreateTypeAsync(CreateDetailTypeRequest request, CancellationToken ct)
    {
        var type = new DetailType(request.Title, request.OwnerModule, request.CodeFrom, request.CodeTo, request.PersonKind);
        await EnsureValidTypeAsync(type, ct);
        db.DetailTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return type.Id;
    }

    public async Task UpdateTypeAsync(short id, UpdateDetailTypeRequest request, CancellationToken ct)
    {
        var type = await db.DetailTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new NotFoundException("گروه تفصیلی پیدا نشد.");
        var used = await db.DetailAccounts.Where(d => d.DetailTypeId == id)
            .GroupBy(d => 1).Select(g => new { Min = g.Min(d => d.Code), Max = g.Max(d => d.Code) })
            .FirstOrDefaultAsync(ct);

        type.Rename(request.Title);
        type.ChangeOwner(request.OwnerModule, request.PersonKind, hasDetails: used is not null);
        type.ChangeRange(request.CodeFrom, request.CodeTo, used?.Min, used?.Max);
        if (request.IsActive) type.Activate(); else type.Deactivate();

        await EnsureValidTypeAsync(type, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف گروه تفصیلی؛ فقط وقتی تفصیلی ندارد و در قاعده هیچ معینی نیامده است.</summary>
    public async Task DeleteTypeAsync(short id, CancellationToken ct)
    {
        var type = await db.DetailTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new NotFoundException("گروه تفصیلی پیدا نشد.");
        var count = await db.DetailAccounts.CountAsync(d => d.DetailTypeId == id, ct);
        if (count > 0)
            throw new DomainException($"گروه «{type.Title}» {count} تفصیلی دارد و حذف نمی‌شود؛ غیرفعالش کنید.");
        var accounts = await db.Accounts.Where(a => a.DetailLevels.Any(l => l.AllowedTypes.Any(t => t.DetailTypeId == id)))
            .Select(a => a.Code).ToListAsync(ct);
        if (accounts.Count > 0)
            throw new DomainException($"گروه «{type.Title}» در قواعد تفصیلی معین {string.Join("، ", accounts)} آمده است؛ ابتدا از آن‌جا بردارید.");
        db.DetailTypes.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>تفصیلی‌های یک گروه (برای صفحه مدیریت تفصیلی‌ها)، با تعداد ردیف‌های سند.</summary>
    public async Task<List<DetailAccountRowDto>> ListAccountsAsync(short typeId, string? term, CancellationToken ct)
    {
        var query = db.DetailAccounts.AsNoTracking().Where(d => d.DetailTypeId == typeId);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null) query = query.Where(d => d.Title.Contains(term) || d.Code.StartsWith(term));
        return await query.OrderBy(d => d.Code).Take(500)
            .Select(d => new DetailAccountRowDto(d.Id, d.Code, d.Title, d.PersonId, d.IsActive,
                db.VoucherLines.Count(l => l.Detail1Id == d.Id || l.Detail2Id == d.Id || l.Detail3Id == d.Id
                                           || l.Detail4Id == d.Id || l.Detail5Id == d.Id || l.Detail6Id == d.Id)))
            .ToListAsync(ct);
    }

    /// <summary>ویرایش عنوان و وضعیت تفصیلی غیرشخصی (تفصیلی اشخاص از فرم شخص هماهنگ می‌شود).</summary>
    public async Task UpdateAccountAsync(int id, string title, bool isActive, CancellationToken ct)
    {
        var detail = await LoadNonPersonDetailAsync(id, ct);
        detail.Rename(title);
        if (isActive) detail.Activate(); else detail.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف تفصیلی غیرشخصی که در هیچ سندی نیامده است.</summary>
    public async Task DeleteAccountAsync(int id, CancellationToken ct)
    {
        var detail = await LoadNonPersonDetailAsync(id, ct);
        if (await db.VoucherLines.AnyAsync(l => l.Detail1Id == id || l.Detail2Id == id || l.Detail3Id == id
                                                || l.Detail4Id == id || l.Detail5Id == id || l.Detail6Id == id, ct))
            throw new DomainException($"تفصیلی {detail.Code} در سند استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.DetailAccounts.Remove(detail);
        await db.SaveChangesAsync(ct);
    }

    private async Task<DetailAccount> LoadNonPersonDetailAsync(int id, CancellationToken ct)
    {
        var detail = await db.DetailAccounts.FirstOrDefaultAsync(d => d.Id == id, ct)
                     ?? throw new NotFoundException("تفصیلی پیدا نشد.");
        if (detail.PersonId is not null)
            throw new DomainException("تفصیلی اشخاص و کارکنان از فرم شخص مدیریت می‌شود.");
        return detail;
    }

    /// <summary>هم‌پوشانی بازه و یکتایی گروه فعال اشخاص/کارکنان برای هر نوع شخص.</summary>
    private async Task EnsureValidTypeAsync(DetailType type, CancellationToken ct)
    {
        var others = await db.DetailTypes.AsNoTracking().Where(t => t.Id != type.Id).ToListAsync(ct);
        var overlap = others.FirstOrDefault(t => t.OverlapsWith(type.CodeFrom, type.CodeTo));
        if (overlap is not null)
            throw new DomainException($"بازه کد با گروه «{overlap.Title}» ({overlap.CodeFrom} تا {overlap.CodeTo}) هم‌پوشانی دارد.");

        if (type.IsActive && type.OwnerModule is OwnerModules.Persons or OwnerModules.Payroll)
        {
            var twin = others.FirstOrDefault(t => t.IsActive && t.OwnerModule == type.OwnerModule && t.PersonKind == type.PersonKind);
            if (twin is not null)
                throw new DomainException($"گروه فعال «{twin.Title}» همین نقش را دارد؛ برای هر نوع شخص فقط یک گروه فعال {(type.OwnerModule == OwnerModules.Persons ? "اشخاص" : "کارکنان")} مجاز است.");
        }
    }

    public Task<List<DetailAccountDto>> SearchAsync(short? detailTypeId, string? term, CancellationToken ct) =>
        SearchAsync(detailTypeId is null ? null : [detailTypeId.Value], term, ct);

    /// <summary>جستجوی تفصیلی‌های فعال در انواع مجاز (برای انتخاب در ردیف سند).</summary>
    public Task<List<DetailAccountDto>> SearchAsync(IReadOnlyCollection<short>? detailTypeIds, string? term, CancellationToken ct) =>
        SearchAsync(detailTypeIds, null, term, ct);

    /// <summary>
    /// مثل بالا، با فیلتر نرم نقش: تفصیلی‌های گروه اشخاص فقط وقتی می‌آیند که شخصشان یکی از این نقش‌ها را داشته باشد.
    /// تفصیلی‌های غیرشخصی (بانک، صندوق، ...) از این فیلتر تأثیر نمی‌گیرند. نقش خالی یعنی همه.
    /// </summary>
    public Task<List<DetailAccountDto>> SearchAsync(IReadOnlyCollection<short>? detailTypeIds, IReadOnlyCollection<short>? roleIds,
        string? term, CancellationToken ct)
    {
        var query = db.DetailAccounts.AsNoTracking();
        if (detailTypeIds is not null)
            query = query.Where(d => detailTypeIds.Contains(d.DetailTypeId) && d.IsActive);
        if (roleIds is { Count: > 0 })
        {
            var personTypes = db.DetailTypes.Where(t => t.OwnerModule == OwnerModules.Persons).Select(t => t.Id);
            var withRole = db.PersonRoleLinks.Where(r => roleIds.Contains(r.RoleId)).Select(r => r.PersonId);
            query = query.Where(d => !personTypes.Contains(d.DetailTypeId) || withRole.Contains(d.PersonId!.Value));
        }
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            query = query.Where(d => d.Title.Contains(term) || d.Code.StartsWith(term));

        return query.OrderBy(d => d.Code).Take(100)
            .Select(d => new DetailAccountDto(d.Id, d.Code, d.Title, d.DetailTypeId, d.PersonId, d.IsActive))
            .ToListAsync(ct);
    }

    public async Task<DetailAccountDto> GetAccountAsync(int id, CancellationToken ct) =>
        await db.DetailAccounts.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new DetailAccountDto(d.Id, d.Code, d.Title, d.DetailTypeId, d.PersonId, d.IsActive))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException($"تفصیلی {id} پیدا نشد.");

    /// <summary>
    /// تفصیلی غیرشخصی (صندوق، بانک، مرکز هزینه، پروژه و ...).
    /// تفصیلی اشخاص فقط از طریق تعریف شخص ساخته می‌شود.
    /// TODO: وقتی ماژول‌های خزانه، حقوق و دارایی ساخته شدند، تفصیلی‌های آن‌ها فقط از همان ماژول ساخته شود.
    /// </summary>
    public async Task<int> CreateAccountAsync(CreateDetailAccountRequest request, CancellationToken ct)
    {
        var type = await db.DetailTypes.FirstOrDefaultAsync(t => t.Id == request.DetailTypeId, ct)
                   ?? throw new NotFoundException($"نوع تفصیلی {request.DetailTypeId} پیدا نشد.");
        if (type.OwnerModule is OwnerModules.Persons or OwnerModules.Payroll)
            throw new DomainException("تفصیلی اشخاص و کارکنان از قسمت تعریف شخص ساخته می‌شود.");

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await NextCodeAsync(type, ct)
            : request.Code.Trim();
        if (await db.DetailAccounts.AnyAsync(d => d.Code == code, ct))
            throw new DomainException($"کد تفصیلی {code} تکراری است.");

        var detail = new DetailAccount(type, code, request.Title);
        db.DetailAccounts.Add(detail);
        await db.SaveChangesAsync(ct);
        return detail.Id;
    }

    internal static async Task<string> NextCodeAsync(IAccountingDbContext db, DetailType type, CancellationToken ct)
    {
        var last = await db.DetailAccounts
            .Where(d => d.DetailTypeId == type.Id)
            .OrderByDescending(d => d.Code)
            .Select(d => d.Code)
            .FirstOrDefaultAsync(ct);
        return type.NextCode(last);
    }

    private Task<string> NextCodeAsync(DetailType type, CancellationToken ct) => NextCodeAsync(db, type, ct);
}
