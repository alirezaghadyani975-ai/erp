using Erp.SharedKernel.Text;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Persons;

/// <summary>نقش‌های شخص با شناسه نوع تفصیلی مشخص می‌شوند (مثلاً مشتری حقیقی، تأمین‌کننده حقیقی).</summary>
public sealed record CreatePersonRequest(PersonKind Kind, string DisplayName, string? NationalId, string? EconomicCode,
    List<short> RoleDetailTypeIds);

public sealed record PersonDto(int Id, PersonKind Kind, string DisplayName, string? NationalId, string? EconomicCode,
    bool IsActive, List<DetailAccountDto> Details);

/// <summary>
/// نسخه حداقلی تعریف اشخاص، تا ماژول کامل اشخاص ساخته شود.
/// هر شخص برای هر نقشش یک تفصیلی در بازه کد همان نقش می‌گیرد و همه با PersonId به هم وصل‌اند.
/// </summary>
public class PersonService(IAccountingDbContext db)
{
    public async Task<int> CreateAsync(CreatePersonRequest request, CancellationToken ct)
    {
        if (request.RoleDetailTypeIds.Count == 0)
            throw new DomainException("حداقل یک نقش (مشتری، تأمین‌کننده، ...) برای شخص لازم است.");
        if (!string.IsNullOrWhiteSpace(request.NationalId)
            && await db.Persons.AnyAsync(p => p.NationalId == PersianText.Normalize(request.NationalId), ct))
            throw new DomainException($"شخصی با کد/شناسه ملی {request.NationalId} قبلاً تعریف شده است.");

        var roleIds = request.RoleDetailTypeIds.Distinct().ToList();
        var roles = await db.DetailTypes.Where(t => roleIds.Contains(t.Id)).ToListAsync(ct);
        if (roles.Count != roleIds.Count)
            throw new DomainException("یک یا چند نقش انتخاب‌شده وجود ندارد.");
        foreach (var role in roles)
        {
            if (role.OwnerModule is not (OwnerModules.Persons or OwnerModules.Payroll))
                throw new DomainException($"«{role.Title}» نقش شخص نیست.");
            if (role.PersonKind is { } kind && kind != request.Kind)
                throw new DomainException($"نقش «{role.Title}» با نوع شخص (حقیقی/حقوقی) نمی‌خواند.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var person = new Person(request.Kind, request.DisplayName, request.NationalId, request.EconomicCode);
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);

        foreach (var role in roles)
        {
            var code = await DetailService.NextCodeAsync(db, role, ct);
            db.DetailAccounts.Add(new DetailAccount(role, code, person.DisplayName, person.Id,
                role.OwnerModule, person.Id));
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return person.Id;
    }

    public async Task<PersonDto> GetAsync(int id, CancellationToken ct) =>
        (await QueryAsync(db.Persons.Where(p => p.Id == id), ct)).FirstOrDefault()
        ?? throw new NotFoundException($"شخص با شناسه {id} پیدا نشد.");

    public Task<List<PersonDto>> SearchAsync(string? term, CancellationToken ct)
    {
        var query = db.Persons.AsQueryable();
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            query = query.Where(p => p.DisplayName.Contains(term) || p.NationalId!.StartsWith(term));
        return QueryAsync(query.OrderBy(p => p.DisplayName).Take(100), ct);
    }

    private async Task<List<PersonDto>> QueryAsync(IQueryable<Person> persons, CancellationToken ct)
    {
        var list = await persons.AsNoTracking().ToListAsync(ct);
        var ids = list.Select(p => p.Id).ToList();
        var details = await db.DetailAccounts.AsNoTracking()
            .Where(d => d.PersonId != null && ids.Contains(d.PersonId.Value))
            .OrderBy(d => d.Code)
            .Select(d => new DetailAccountDto(d.Id, d.Code, d.Title, d.DetailTypeId, d.PersonId, d.IsActive))
            .ToListAsync(ct);

        return list.Select(p => new PersonDto(p.Id, p.Kind, p.DisplayName, p.NationalId, p.EconomicCode, p.IsActive,
            details.Where(d => d.PersonId == p.Id).ToList())).ToList();
    }
}
