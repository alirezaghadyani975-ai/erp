using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.Details;
using Erp.BaseData.Contracts;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Details;

/// <summary>
/// هماهنگی تفصیلی‌های اشخاص با ماژول اطلاعات پایه.
/// هر شخص با نقش طرف‌حسابی یک تفصیلی مشترک در گروه «اشخاص» نوع خودش، و با نقش کارمند یک تفصیلی کارکنان می‌گیرد.
/// تفصیلی‌ها با تغییر نام شخص هم‌نام و با غیرفعال شدنش غیرفعال می‌شوند. نقش که برداشته شود تفصیلی می‌ماند.
/// </summary>
public class PersonDetailLinks(IAccountingDbContext db) : IPersonLinks
{
    public async Task SyncAsync(PersonSnapshot person, CancellationToken ct)
    {
        var details = await db.DetailAccounts.Where(d => d.PersonId == person.Id).ToListAsync(ct);
        var typeIds = details.Select(d => d.DetailTypeId).ToList();
        var owners = await db.DetailTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.OwnerModule, ct);

        var hasParty = details.Any(d => owners[d.DetailTypeId] == OwnerModules.Persons);
        var hasEmployee = details.Any(d => owners[d.DetailTypeId] == OwnerModules.Payroll);
        if (person.HasPartyRole && !hasParty)
            details.Add(await NewDetailAsync(person, OwnerModules.Persons, ct));
        if (person.HasEmployeeRole && !hasEmployee)
            details.Add(await NewDetailAsync(person, OwnerModules.Payroll, ct));

        foreach (var d in details)
        {
            if (d.Title != person.DisplayName) d.Rename(person.DisplayName);
            if (person.IsActive) d.Activate(); else d.Deactivate();
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> CannotDeleteReasonAsync(int personId, CancellationToken ct)
    {
        var used = await UsedDetailsAsync(personId).Select(d => d.Code).ToListAsync(ct);
        return used.Count == 0 ? null : $"تفصیلی {string.Join("، ", used)} در سند حسابداری استفاده شده است.";
    }

    public async Task DeleteAsync(int personId, CancellationToken ct)
    {
        if (await UsedDetailsAsync(personId).AnyAsync(ct))
            throw new DomainException("تفصیلی این شخص در سند استفاده شده است.");
        var details = await db.DetailAccounts.Where(d => d.PersonId == personId).ToListAsync(ct);
        db.DetailAccounts.RemoveRange(details);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PersonLinkInfo>> GetAsync(IReadOnlyCollection<int> personIds, CancellationToken ct)
    {
        var ids = personIds.ToList();
        var rows = await (from d in db.DetailAccounts.AsNoTracking()
                          join t in db.DetailTypes on d.DetailTypeId equals t.Id
                          where d.PersonId != null && ids.Contains(d.PersonId.Value)
                          orderby d.Code
                          select new { PersonId = d.PersonId!.Value, d.Code, d.Title, t.OwnerModule, TypeTitle = t.Title })
            .ToListAsync(ct);
        return rows.Select(r => new PersonLinkInfo(r.PersonId, "accounting",
            r.OwnerModule == OwnerModules.Payroll ? "employee" : "party", r.Code, r.Title, r.TypeTitle)).ToList();
    }

    public async Task<IReadOnlyList<int>> FindByCodeAsync(string codePrefix, CancellationToken ct) =>
        await db.DetailAccounts.AsNoTracking()
            .Where(d => d.PersonId != null && d.Code.StartsWith(codePrefix))
            .Select(d => d.PersonId!.Value).Distinct().Take(200).ToListAsync(ct);

    private IQueryable<DetailAccount> UsedDetailsAsync(int personId) =>
        db.DetailAccounts.Where(d => d.PersonId == personId
            && db.VoucherLines.Any(l => l.Detail1Id == d.Id || l.Detail2Id == d.Id || l.Detail3Id == d.Id
                                        || l.Detail4Id == d.Id || l.Detail5Id == d.Id || l.Detail6Id == d.Id));

    private async Task<DetailAccount> NewDetailAsync(PersonSnapshot person, string ownerModule, CancellationToken ct)
    {
        var candidates = await db.DetailTypes
            .Where(t => t.IsActive && t.OwnerModule == ownerModule && (t.PersonKind == null || t.PersonKind == person.Kind))
            .ToListAsync(ct);
        // گروه مخصوص همین نوع شخص بر گروه عمومی (بدون نوع شخص) مقدم است
        var type = candidates.OrderBy(t => t.PersonKind is null).ThenBy(t => t.CodeFrom).FirstOrDefault()
                   ?? throw new DomainException(ownerModule == OwnerModules.Payroll
                       ? "گروه تفصیلی کارکنان تعریف نشده است (صفحه گروه‌های تفصیلی)."
                       : $"گروه تفصیلی اشخاص {(person.Kind == PersonKind.Natural ? "حقیقی" : "حقوقی")} تعریف نشده است (صفحه گروه‌های تفصیلی).");

        var code = await DetailService.NextCodeAsync(db, type, ct);
        var detail = new DetailAccount(type, code, person.DisplayName, person.Id, ownerModule, person.Id);
        db.DetailAccounts.Add(detail);
        await db.SaveChangesAsync(ct); // کد بعدی برای تفصیلی بعدی همین تراکنش دیده شود
        return detail;
    }
}
