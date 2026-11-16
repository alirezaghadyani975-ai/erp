using Erp.SharedKernel.Security;
using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Lookups;
using Erp.BaseData.Domain.Persons;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Application.Persons;

/// <summary>
/// تعریف اشخاص، نقش‌ها، نشانی‌ها، حساب‌های بانکی، رابطین و پیوست‌ها.
/// ماژول‌های دیگر (حسابداری: تفصیلی‌ها) با <see cref="IPersonLinks"/> داخل همان تراکنش هماهنگ می‌شوند.
/// </summary>
public class PersonService(
    IBaseDataDbContext db,
    IUnitOfWork uow,
    IEnumerable<IPersonLinks> links,
    ICurrentUser user,
    TimeProvider clock,
    IAccessControl access)
{
    // ------------------------------------------------------------------ نقش‌ها

    public async Task<List<PersonRoleTypeDto>> GetRoleTypesAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.Persons.SelectMany(p => p.Roles).GroupBy(r => r.RoleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var roles = await db.PersonRoleTypes.AsNoTracking()
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Title)
            .ToListAsync(ct);
        return roles.Select(r => new PersonRoleTypeDto(r.Id, r.Code, r.Title, r.IsSystem, r.IsActive, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public async Task<short> CreateRoleTypeAsync(string title, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonRoles);
        var role = new PersonRoleType(title);
        if (await db.PersonRoleTypes.AnyAsync(r => r.Title == role.Title, ct))
            throw new DomainException($"نقش «{role.Title}» قبلاً تعریف شده است.");
        db.PersonRoleTypes.Add(role);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task UpdateRoleTypeAsync(short id, string title, bool isActive, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonRoles);
        var role = await db.PersonRoleTypes.FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new NotFoundException("نقش پیدا نشد.");
        role.Rename(title);
        if (isActive) role.Activate(); else role.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف نقش تعریف کاربر؛ اگر شخص یا قاعده تفصیلی معینی از آن استفاده کند، حذف نمی‌شود.</summary>
    public async Task DeleteRoleTypeAsync(short id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonRoles);
        var role = await db.PersonRoleTypes.FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new NotFoundException("نقش پیدا نشد.");
        if (role.IsSystem)
            throw new DomainException("نقش‌های سیستمی حذف نمی‌شوند.");
        var used = await db.Persons.CountAsync(p => p.Roles.Any(r => r.RoleId == id), ct);
        if (used > 0)
            throw new DomainException($"نقش «{role.Title}» به {used} شخص داده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.PersonRoleTypes.Remove(role);
        await db.SaveChangesAsync(ct); // اگر در قاعده تفصیلی معینی باشد، کلید خارجی جلوی حذف را می‌گیرد
    }

    // ------------------------------------------------------------------ ثبت و ویرایش

    public async Task<int> CreateAsync(SavePersonRequest request, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonCreate);
        var today = Today();
        var person = Person.Create(request.Kind, await ToProfileAsync(request, ct), today);
        await ApplyAsync(person, request, ct);

        await using var tx = await uow.BeginAsync(ct);
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        await SyncLinksAsync(person, ct);
        await tx.CommitAsync(ct);
        return person.Id;
    }

    public async Task UpdateAsync(int id, SavePersonRequest request, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonEdit);
        var person = await LoadAsync(id, ct);
        if (person.Kind != request.Kind)
            throw new DomainException("نوع شخص (حقیقی/حقوقی) بعد از ثبت عوض نمی‌شود.");

        person.Update(await ToProfileAsync(request, ct), Today());
        await ApplyAsync(person, request, ct);

        await using var tx = await uow.BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        await SyncLinksAsync(person, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// حذف شخص؛ فقط وقتی رکوردهای وابسته (مثلاً تفصیلی با گردش در سند) ندارد.
    /// تفصیلی‌های بدون گردش و نشانی‌ها، حساب‌ها، رابطین و پیوست‌هایش هم حذف می‌شوند.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonDelete);
        var person = await LoadAsync(id, ct);
        foreach (var link in links)
            if (await link.CannotDeleteReasonAsync(id, ct) is { } reason)
                throw new DomainException($"«{person.DisplayName}» حذف نمی‌شود: {reason} می‌توانید غیرفعالش کنید.");

        await using var tx = await uow.BeginAsync(ct);
        foreach (var link in links) await link.DeleteAsync(id, ct);
        db.Persons.Remove(person);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>اشخاص مشابه (نام یکسان، موبایل یکسان، کد/شناسه ملی یکسان) برای هشدار پیش از ذخیره.</summary>
    public async Task<List<DuplicateDto>> FindDuplicatesAsync(SavePersonRequest request, int? excludeId, CancellationToken ct)
    {
        var name = PersianText.NormalizeOrNull(request.Kind == PersonKind.Natural
            ? $"{request.FirstName} {request.LastName}"
            : request.LegalName);
        var mobile = IranianIds.Error(IdKind.Mobile, request.Mobile) is null ? IranianIds.Normalize(IdKind.Mobile, request.Mobile) : null;
        var nationalId = IranianIds.Digits(request.NationalId);

        var candidates = await db.Persons.AsNoTracking()
            .Where(p => p.Id != (excludeId ?? 0)
                        && ((name != null && p.DisplayName == name)
                            || (mobile != null && p.Mobile == mobile)
                            || (nationalId != "" && p.NationalId == nationalId)))
            .Select(p => new { p.Id, p.DisplayName, p.Mobile, p.NationalId })
            .Take(10)
            .ToListAsync(ct);

        return candidates.Select(c => new DuplicateDto(c.Id, c.DisplayName,
                nationalId != "" && c.NationalId == nationalId ? "کد/شناسه ملی یکسان"
                : mobile is not null && c.Mobile == mobile ? "موبایل یکسان" : "نام یکسان"))
            .ToList();
    }

    // ------------------------------------------------------------------ خواندن

    public async Task<PersonDetailsDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonView);
        var p = await db.Persons.AsNoTracking()
                    .Include(x => x.Roles).Include(x => x.Addresses).Include(x => x.BankAccounts).Include(x => x.Contacts)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"شخص با شناسه {id} پیدا نشد.");

        var personLinks = new List<PersonLinkInfo>();
        foreach (var link in links) personLinks.AddRange(await link.GetAsync([id], ct));

        return new PersonDetailsDto
        {
            Id = p.Id, Kind = p.Kind, DisplayName = p.DisplayName, IsActive = p.IsActive,
            FirstName = p.FirstName, LastName = p.LastName, FatherName = p.FatherName, Gender = p.Gender,
            BirthDate = Date(p.BirthDate), BirthCityId = p.BirthCityId, IdNumber = p.IdNumber, IdIssueCityId = p.IdIssueCityId,
            MaritalStatus = p.MaritalStatus, IsForeign = p.IsForeign, NationalityId = p.NationalityId,
            ForeignNationalCode = p.ForeignNationalCode, PassportNo = p.PassportNo,
            LegalName = p.Kind == PersonKind.Legal ? p.DisplayName : null, LegalType = p.LegalType,
            RegistrationNo = p.RegistrationNo, RegistrationDate = Date(p.RegistrationDate), RegistrationCityId = p.RegistrationCityId,
            NationalId = p.NationalId, EconomicCode = p.EconomicCode, TaxPayerType = p.TaxPayerType, BranchCode = p.BranchCode,
            PersonGroupId = p.PersonGroupId, Mobile = p.Mobile, Phone = p.Phone, Email = p.Email, Website = p.Website, Notes = p.Notes,
            RoleIds = p.Roles.Select(r => r.RoleId).ToList(),
            Addresses = p.Addresses.OrderBy(a => a.Id).Select(a => new AddressDto
            {
                AddressTypeId = a.AddressTypeId, ProvinceId = a.ProvinceId, CityId = a.CityId, Address = a.Address,
                PostalCode = a.PostalCode, Phone = a.Phone, IsDefault = a.IsDefault
            }).ToList(),
            BankAccounts = p.BankAccounts.OrderBy(a => a.Id).Select(a => new BankAccountDto
            {
                BankId = a.BankId, Iban = a.Iban, AccountNo = a.AccountNo, CardNo = a.CardNo, IsDefault = a.IsDefault
            }).ToList(),
            Contacts = p.Contacts.OrderBy(c => c.Id).Select(c => new ContactDto
            {
                FullName = c.FullName, ContactRoleId = c.ContactRoleId, Mobile = c.Mobile, Phone = c.Phone, Email = c.Email, Notes = c.Notes
            }).ToList(),
            Links = personLinks,
            Attachments = await ListAttachmentsAsync(id, ct)
        };
    }

    /// <summary>جستجوی اشخاص با نام، کد ملی، موبایل یا کد تفصیلی؛ با فیلتر نقش و گروه.</summary>
    public async Task<List<PersonListItemDto>> SearchAsync(string? term, short? roleId, int? groupId, bool includeInactive,
        CancellationToken ct)
    {
        var query = db.Persons.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (roleId is short r) query = query.Where(p => p.Roles.Any(x => x.RoleId == r));
        if (groupId is int g)
        {
            var groupIds = await DescendantsAsync(g, ct);
            query = query.Where(p => p.PersonGroupId != null && groupIds.Contains(p.PersonGroupId.Value));
        }

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
        {
            var byCode = new List<int>();
            foreach (var link in links) byCode.AddRange(await link.FindByCodeAsync(term, ct));
            query = query.Where(p => p.DisplayName.Contains(term) || p.NationalId!.StartsWith(term)
                                     || p.Mobile!.StartsWith(term) || byCode.Contains(p.Id));
        }

        var persons = await query.OrderBy(p => p.DisplayName).Take(200)
            .Select(p => new { p.Id, p.Kind, p.DisplayName, p.NationalId, p.Mobile, p.IsActive, p.PersonGroupId,
                Roles = p.Roles.Select(x => x.RoleId).ToList() })
            .ToListAsync(ct);

        var ids = persons.Select(p => p.Id).ToList();
        var personLinks = new List<PersonLinkInfo>();
        foreach (var link in links) personLinks.AddRange(await link.GetAsync(ids, ct));
        var roleTitles = await db.PersonRoleTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        var groupIdsUsed = persons.Select(p => p.PersonGroupId).OfType<int>().Distinct().ToList();
        var groups = await db.LookupItems.AsNoTracking().Where(i => groupIdsUsed.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);

        return persons.Select(p => new PersonListItemDto(p.Id, p.Kind, p.DisplayName, p.NationalId, p.Mobile, p.IsActive,
                p.Roles.Select(x => roleTitles.GetValueOrDefault(x, "?")).ToList(),
                p.PersonGroupId is int pg ? groups.GetValueOrDefault(pg) : null,
                personLinks.FirstOrDefault(l => l.PersonId == p.Id && l.Kind == "party")?.Code,
                personLinks.FirstOrDefault(l => l.PersonId == p.Id && l.Kind == "employee")?.Code))
            .ToList();
    }

    // ------------------------------------------------------------------ پیوست‌ها

    public async Task<List<AttachmentDto>> ListAttachmentsAsync(int personId, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonView);
        var rows = await db.PersonAttachments.AsNoTracking().Where(a => a.PersonId == personId).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Title, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt })
            .ToListAsync(ct);
        return rows.Select(a => new AttachmentDto(a.Id, a.Title, a.FileName, a.ContentType, a.SizeBytes,
            PersianDate.ToPersian(DateOnly.FromDateTime(a.UploadedAt)))).ToList();
    }

    public async Task<int> AddAttachmentAsync(int personId, string? title, string fileName, string contentType, byte[] content,
        CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonEdit);
        if (!await db.Persons.AnyAsync(p => p.Id == personId, ct))
            throw new NotFoundException("شخص پیدا نشد.");
        var attachment = new PersonAttachment(personId, title ?? "", fileName, contentType, content, user.UserId);
        db.PersonAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return attachment.Id;
    }

    public async Task<AttachmentFile> GetAttachmentAsync(int id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonView);
        return await db.PersonAttachments.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new AttachmentFile(a.FileName, a.ContentType, a.Content)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("پیوست پیدا نشد.");
    }

    public async Task DeleteAttachmentAsync(int id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.PersonEdit);
        var count = await db.PersonAttachments.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        if (count == 0) throw new NotFoundException("پیوست پیدا نشد.");
    }

    // ------------------------------------------------------------------ کمکی‌ها

    private async Task ApplyAsync(Person person, SavePersonRequest request, CancellationToken ct)
    {
        if (request.IsActive) person.Activate(); else person.Deactivate();

        var roleIds = request.RoleIds.Distinct().ToList();
        var roles = await db.PersonRoleTypes.Where(r => roleIds.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != roleIds.Count)
            throw new DomainException("یک یا چند نقش انتخاب‌شده وجود ندارد.");
        if (roles.FirstOrDefault(r => !r.IsActive && !person.HasRole(r.Id)) is { } inactive)
            throw new DomainException($"نقش «{inactive.Title}» غیرفعال است.");
        person.SetRoles(roleIds, await EmployeeRoleIdAsync(ct));

        // اقلام جدول پایه نشانی‌ها و رابطین
        var lookupIds = request.Addresses.SelectMany(a => new[] { a.AddressTypeId, a.ProvinceId, a.CityId })
            .Concat(request.Contacts.Select(c => c.ContactRoleId)).OfType<int>().Distinct().ToList();
        var items = await LookupMapAsync(lookupIds, ct);
        var row = 0;
        foreach (var a in request.Addresses)
        {
            row++;
            CheckLookup(items, a.AddressTypeId, LookupCodes.AddressType, $"نشانی {row}: نوع نشانی");
            CheckLookup(items, a.ProvinceId, LookupCodes.Province, $"نشانی {row}: استان");
            CheckLookup(items, a.CityId, LookupCodes.City, $"نشانی {row}: شهر");
            if (a.CityId is int city && a.ProvinceId is int province && items[city].ParentId is int cityProvince && cityProvince != province)
                throw new DomainException($"نشانی {row}: شهر «{items[city].Title}» در استان انتخاب‌شده نیست.");
        }
        row = 0;
        foreach (var c in request.Contacts)
            CheckLookup(items, c.ContactRoleId, LookupCodes.ContactRole, $"رابط {++row}: سمت");

        person.SetAddresses(request.Addresses.Select(a =>
            new AddressData(a.AddressTypeId, a.ProvinceId, a.CityId, a.Address, a.PostalCode, a.Phone, a.IsDefault)));
        person.SetContacts(request.Contacts.Select(c =>
            new ContactData(c.FullName, c.ContactRoleId, c.Mobile, c.Phone, c.Email, c.Notes)));

        // بانک: اگر انتخاب نشده باشد از شبا یا کارت
        var banks = await db.Banks.AsNoTracking().ToListAsync(ct);
        person.SetBankAccounts(request.BankAccounts.Select((a, i) =>
        {
            var bankId = a.BankId;
            if (bankId is short chosen && banks.All(b => b.Id != chosen))
                throw new DomainException($"حساب بانکی {i + 1}: بانک انتخاب‌شده وجود ندارد.");
            bankId ??= banks.FirstOrDefault(b => b.IbanCode is not null && b.IbanCode == IranianIds.IbanBankCode(a.Iban))?.Id
                       ?? banks.FirstOrDefault(b => b.MatchesCard(a.CardNo))?.Id;
            return new BankAccountData(bankId, a.Iban, a.AccountNo, a.CardNo, a.IsDefault);
        }).ToList());

        // یکتایی شناسه‌ها
        if (person.NationalId is { } nationalId
            && await db.Persons.AnyAsync(p => p.Id != person.Id && p.NationalId == nationalId, ct))
            throw new DomainException($"شخص دیگری با کد/شناسه ملی {nationalId} تعریف شده است.");
        if (person.ForeignNationalCode is { } foreignCode
            && await db.Persons.AnyAsync(p => p.Id != person.Id && p.ForeignNationalCode == foreignCode, ct))
            throw new DomainException($"شخص دیگری با کد فراگیر {foreignCode} تعریف شده است.");
    }

    private async Task<PersonProfile> ToProfileAsync(SavePersonRequest r, CancellationToken ct)
    {
        var ids = new[] { r.BirthCityId, r.IdIssueCityId, r.RegistrationCityId, r.NationalityId, r.PersonGroupId }
            .OfType<int>().Distinct().ToList();
        var items = await LookupMapAsync(ids, ct);
        CheckLookup(items, r.BirthCityId, LookupCodes.City, "محل تولد");
        CheckLookup(items, r.IdIssueCityId, LookupCodes.City, "محل صدور");
        CheckLookup(items, r.RegistrationCityId, LookupCodes.City, "محل ثبت");
        CheckLookup(items, r.NationalityId, LookupCodes.Country, "تابعیت");
        CheckLookup(items, r.PersonGroupId, LookupCodes.PersonGroup, "گروه شخص");

        return new PersonProfile
        {
            FirstName = r.FirstName, LastName = r.LastName, FatherName = r.FatherName, Gender = r.Gender,
            BirthDate = ParseDate(r.BirthDate, "تاریخ تولد"), BirthCityId = r.BirthCityId, IdNumber = r.IdNumber,
            IdIssueCityId = r.IdIssueCityId, MaritalStatus = r.MaritalStatus, IsForeign = r.IsForeign,
            NationalityId = r.NationalityId, ForeignNationalCode = r.ForeignNationalCode, PassportNo = r.PassportNo,
            LegalName = r.LegalName, LegalType = r.LegalType, RegistrationNo = r.RegistrationNo,
            RegistrationDate = ParseDate(r.RegistrationDate, "تاریخ ثبت"), RegistrationCityId = r.RegistrationCityId,
            NationalId = r.NationalId, EconomicCode = r.EconomicCode, TaxPayerType = r.TaxPayerType, BranchCode = r.BranchCode,
            PersonGroupId = r.PersonGroupId, Mobile = r.Mobile, Phone = r.Phone, Email = r.Email, Website = r.Website, Notes = r.Notes
        };
    }

    private sealed record ItemRef(int Id, string Title, string CategoryCode, int? ParentId);

    private async Task<Dictionary<int, ItemRef>> LookupMapAsync(List<int> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await (from i in db.LookupItems.AsNoTracking()
                     join c in db.LookupCategories on i.CategoryId equals c.Id
                     where ids.Contains(i.Id)
                     select new ItemRef(i.Id, i.Title, c.Code, i.ParentId)).ToDictionaryAsync(x => x.Id, ct);

    private static void CheckLookup(Dictionary<int, ItemRef> items, int? id, string categoryCode, string label)
    {
        if (id is not int value) return;
        if (!items.TryGetValue(value, out var item) || item.CategoryCode != categoryCode)
            throw new DomainException($"{label} از فهرست درست انتخاب نشده است.");
    }

    /// <summary>گروه و همه زیرگروه‌هایش.</summary>
    private async Task<List<int>> DescendantsAsync(int groupId, CancellationToken ct)
    {
        var all = await db.LookupItems.AsNoTracking().Where(i => i.ParentId != null)
            .Select(i => new { i.Id, Parent = i.ParentId!.Value }).ToListAsync(ct);
        var result = new List<int> { groupId };
        for (var i = 0; i < result.Count; i++)
            result.AddRange(all.Where(x => x.Parent == result[i]).Select(x => x.Id));
        return result;
    }

    private async Task SyncLinksAsync(Person person, CancellationToken ct)
    {
        var employeeRoleId = await EmployeeRoleIdAsync(ct);
        var snapshot = new PersonSnapshot(person.Id, person.Kind, person.DisplayName, person.IsActive,
            HasPartyRole: person.Roles.Any(r => r.RoleId != employeeRoleId),
            HasEmployeeRole: person.HasRole(employeeRoleId));
        foreach (var link in links) await link.SyncAsync(snapshot, ct);
    }

    private async Task<short> EmployeeRoleIdAsync(CancellationToken ct) =>
        await db.PersonRoleTypes.Where(r => r.Code == RoleCodes.Employee).Select(r => (short?)r.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("نقش سیستمی «کارمند» در دیتابیس نیست.");

    private async Task<Person> LoadAsync(int id, CancellationToken ct) =>
        await db.Persons.Include(p => p.Roles).Include(p => p.Addresses).Include(p => p.BankAccounts).Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException($"شخص با شناسه {id} پیدا نشد.");

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static DateOnly? ParseDate(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return PersianDate.TryParse(PersianText.Normalize(value), out var d)
            ? d
            : throw new DomainException($"{label} نامعتبر است: {value}");
    }

    private static string? Date(DateOnly? d) => d is { } x ? PersianDate.ToPersian(x) : null;
}
