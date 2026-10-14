using Erp.SharedKernel.Text;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Details;

public sealed record DetailTypeDto(short Id, string Title, string OwnerModule, PersonKind? PersonKind,
    string CodeFrom, string CodeTo, bool IsActive);

public sealed record CreateDetailTypeRequest(string Title, string OwnerModule, string CodeFrom, string CodeTo, PersonKind? PersonKind);

public sealed record DetailAccountDto(int Id, string Code, string Title, short DetailTypeId, int? PersonId, bool IsActive);

/// <summary>اگر Code خالی باشد، اولین کد آزاد بعد از آخرین کد بازه داده می‌شود.</summary>
public sealed record CreateDetailAccountRequest(short DetailTypeId, string Title, string? Code);

public class DetailService(IAccountingDbContext db)
{
    public Task<List<DetailTypeDto>> GetTypesAsync(CancellationToken ct) =>
        db.DetailTypes.AsNoTracking().OrderBy(t => t.CodeFrom)
            .Select(t => new DetailTypeDto(t.Id, t.Title, t.OwnerModule, t.PersonKind, t.CodeFrom, t.CodeTo, t.IsActive))
            .ToListAsync(ct);

    public async Task<short> CreateTypeAsync(CreateDetailTypeRequest request, CancellationToken ct)
    {
        var type = new DetailType(request.Title, request.OwnerModule, request.CodeFrom, request.CodeTo, request.PersonKind);
        var others = await db.DetailTypes.AsNoTracking().ToListAsync(ct);
        var overlap = others.FirstOrDefault(t => t.OverlapsWith(type.CodeFrom, type.CodeTo));
        if (overlap is not null)
            throw new DomainException($"بازه کد با نوع تفصیلی «{overlap.Title}» ({overlap.CodeFrom} تا {overlap.CodeTo}) هم‌پوشانی دارد.");

        db.DetailTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return type.Id;
    }

    public Task<List<DetailAccountDto>> SearchAsync(short? detailTypeId, string? term, CancellationToken ct) =>
        SearchAsync(detailTypeId is null ? null : [detailTypeId.Value], term, ct);

    /// <summary>جستجوی تفصیلی‌های فعال در انواع مجاز (برای انتخاب در ردیف سند).</summary>
    public Task<List<DetailAccountDto>> SearchAsync(IReadOnlyCollection<short>? detailTypeIds, string? term, CancellationToken ct)
    {
        var query = db.DetailAccounts.AsNoTracking();
        if (detailTypeIds is not null)
            query = query.Where(d => detailTypeIds.Contains(d.DetailTypeId) && d.IsActive);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            query = query.Where(d => d.Title.Contains(term) || d.Code.StartsWith(term));

        return query.OrderBy(d => d.Code).Take(100)
            .Select(d => new DetailAccountDto(d.Id, d.Code, d.Title, d.DetailTypeId, d.PersonId, d.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>
    /// تفصیلی غیرشخصی (صندوق، بانک، مرکز هزینه، پروژه و ...).
    /// تفصیلی اشخاص فقط از طریق تعریف شخص ساخته می‌شود.
    /// TODO: وقتی ماژول‌های خزانه، حقوق و دارایی ساخته شدند، تفصیلی‌های آن‌ها فقط از همان ماژول ساخته شود.
    /// </summary>
    public async Task<int> CreateAccountAsync(CreateDetailAccountRequest request, CancellationToken ct)
    {
        var type = await db.DetailTypes.FirstOrDefaultAsync(t => t.Id == request.DetailTypeId, ct)
                   ?? throw new NotFoundException($"نوع تفصیلی {request.DetailTypeId} پیدا نشد.");
        if (type.OwnerModule == OwnerModules.Persons)
            throw new DomainException("تفصیلی اشخاص از قسمت تعریف شخص ساخته می‌شود.");

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
