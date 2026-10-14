using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Persons;

/// <summary>
/// تعریف اشخاص و نقش‌هایشان.
/// هر شخصی که حداقل یک نقش طرف‌حسابی (غیر از کارمند) دارد یک تفصیلی مشترک در گروه «اشخاص» نوع خودش می‌گیرد؛
/// نقش کارمند یک تفصیلی جدا در گروه کارکنان (مالک: حقوق و دستمزد) می‌گیرد.
/// تفصیلی‌ها با تغییر نام شخص هم‌نام و با غیرفعال شدنش غیرفعال می‌شوند.
/// </summary>
public class PersonService(IAccountingDbContext db, TimeProvider clock)
{
    // ------------------------------------------------------------------ نقش‌ها

    public Task<List<PersonRoleTypeDto>> GetRoleTypesAsync(bool includeInactive, CancellationToken ct) =>
        db.PersonRoleTypes.AsNoTracking()
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Title)
            .Select(r => new PersonRoleTypeDto(r.Id, r.Code, r.Title, r.IsSystem, r.IsActive))
            .ToListAsync(ct);

    public async Task<short> CreateRoleTypeAsync(string title, CancellationToken ct)
    {
        var role = new PersonRoleType(title);
        if (await db.PersonRoleTypes.AnyAsync(r => r.Title == role.Title, ct))
            throw new DomainException($"نقش «{role.Title}» قبلاً تعریف شده است.");
        db.PersonRoleTypes.Add(role);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task UpdateRoleTypeAsync(short id, string title, bool isActive, CancellationToken ct)
    {
        var role = await db.PersonRoleTypes.FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new NotFoundException("نقش پیدا نشد.");
        role.Rename(title);
        if (isActive) role.Activate(); else role.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ اشخاص

    public async Task<int> CreateAsync(SavePersonRequest request, CancellationToken ct)
    {
        var today = Today();
        var person = Person.Create(request.Kind, ToProfile(request), today);
        await ApplyAsync(person, request, today, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        await SyncDetailsAsync(person, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return person.Id;
    }

    public async Task UpdateAsync(int id, SavePersonRequest request, CancellationToken ct)
    {
        var person = await LoadAsync(id, ct);
        if (person.Kind != request.Kind)
            throw new DomainException("نوع شخص (حقیقی/حقوقی) بعد از ثبت عوض نمی‌شود.");

        var today = Today();
        person.Update(ToProfile(request), today);
        await ApplyAsync(person, request, today, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await SyncDetailsAsync(person, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PersonDetailsDto> GetAsync(int id, CancellationToken ct)
    {
        var p = await db.Persons.AsNoTracking()
                    .Include(x => x.Roles).Include(x => x.Addresses).Include(x => x.BankAccounts).Include(x => x.Employee)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"شخص با شناسه {id} پیدا نشد.");

        var details = await db.DetailAccounts.AsNoTracking().Where(d => d.PersonId == id).OrderBy(d => d.Code)
            .Select(d => new DetailAccountDto(d.Id, d.Code, d.Title, d.DetailTypeId, d.PersonId, d.IsActive))
            .ToListAsync(ct);

        return new PersonDetailsDto
        {
            Id = p.Id, Kind = p.Kind, DisplayName = p.DisplayName, IsActive = p.IsActive,
            FirstName = p.FirstName, LastName = p.LastName, FatherName = p.FatherName, Gender = p.Gender,
            BirthDate = Date(p.BirthDate), BirthPlace = p.BirthPlace, IdNumber = p.IdNumber, IdIssuePlace = p.IdIssuePlace,
            MaritalStatus = p.MaritalStatus, IsForeign = p.IsForeign, Nationality = p.Nationality,
            ForeignNationalCode = p.ForeignNationalCode, PassportNo = p.PassportNo,
            LegalName = p.Kind == PersonKind.Legal ? p.DisplayName : null, LegalType = p.LegalType,
            RegistrationNo = p.RegistrationNo, RegistrationDate = Date(p.RegistrationDate), RegistrationPlace = p.RegistrationPlace,
            NationalId = p.NationalId, EconomicCode = p.EconomicCode, TaxPayerType = p.TaxPayerType, BranchCode = p.BranchCode,
            Mobile = p.Mobile, Phone = p.Phone, Email = p.Email, Website = p.Website, Notes = p.Notes,
            RoleIds = p.Roles.Select(r => r.RoleId).ToList(),
            Addresses = p.Addresses.OrderBy(a => a.Id).Select(a => new AddressDto
            {
                Title = a.Title, Province = a.Province, City = a.City, Address = a.Address,
                PostalCode = a.PostalCode, Phone = a.Phone, IsDefault = a.IsDefault
            }).ToList(),
            BankAccounts = p.BankAccounts.OrderBy(a => a.Id).Select(a => new BankAccountDto
            {
                BankName = a.BankName, Iban = a.Iban, AccountNo = a.AccountNo, CardNo = a.CardNo, IsDefault = a.IsDefault
            }).ToList(),
            Employee = p.Employee is not { } e ? null : new EmployeeDto
            {
                EmployeeNo = e.EmployeeNo, HireDate = Date(e.HireDate), EndDate = Date(e.EndDate),
                ContractType = e.ContractType, JobTitle = e.JobTitle, InsuranceType = e.InsuranceType,
                InsuranceNo = e.InsuranceNo, InsuranceStartDate = Date(e.InsuranceStartDate),
                InsuranceJobCode = e.InsuranceJobCode, WorkshopCode = e.WorkshopCode, EducationLevel = e.EducationLevel,
                EducationField = e.EducationField, MilitaryStatus = e.MilitaryStatus, ChildrenCount = e.ChildrenCount,
                DependentsCount = e.DependentsCount, TaxExemptionType = e.TaxExemptionType
            },
            Details = details
        };
    }

    /// <summary>جستجوی اشخاص با نام، کد ملی/شناسه، موبایل یا کد تفصیلی؛ با فیلتر اختیاری نقش.</summary>
    public async Task<List<PersonListItemDto>> SearchAsync(string? term, short? roleId, bool includeInactive, CancellationToken ct)
    {
        var query = db.Persons.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (roleId is short r) query = query.Where(p => p.Roles.Any(x => x.RoleId == r));
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
        {
            var codeMatches = db.DetailAccounts.Where(d => d.PersonId != null && d.Code.StartsWith(term)).Select(d => d.PersonId);
            query = query.Where(p => p.DisplayName.Contains(term) || p.NationalId!.StartsWith(term)
                                     || p.Mobile!.StartsWith(term) || codeMatches.Contains(p.Id));
        }

        var persons = await query.OrderBy(p => p.DisplayName).Take(200)
            .Select(p => new { p.Id, p.Kind, p.DisplayName, p.NationalId, p.Mobile, p.IsActive,
                Roles = p.Roles.Select(x => x.RoleId).ToList() })
            .ToListAsync(ct);

        var ids = persons.Select(p => p.Id).ToList();
        var details = await (from d in db.DetailAccounts.AsNoTracking()
                             join t in db.DetailTypes on d.DetailTypeId equals t.Id
                             where d.PersonId != null && ids.Contains(d.PersonId.Value)
                             select new { PersonId = d.PersonId!.Value, d.Code, t.OwnerModule })
            .ToListAsync(ct);
        var roleTitles = await db.PersonRoleTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Title, ct);

        return persons.Select(p => new PersonListItemDto(p.Id, p.Kind, p.DisplayName, p.NationalId, p.Mobile, p.IsActive,
                p.Roles.Select(x => roleTitles.GetValueOrDefault(x, "?")).ToList(),
                details.FirstOrDefault(d => d.PersonId == p.Id && d.OwnerModule == OwnerModules.Persons)?.Code,
                details.FirstOrDefault(d => d.PersonId == p.Id && d.OwnerModule == OwnerModules.Payroll)?.Code))
            .ToList();
    }

    // ------------------------------------------------------------------ کمکی‌ها

    private async Task ApplyAsync(Person person, SavePersonRequest request, DateOnly today, CancellationToken ct)
    {
        if (request.IsActive) person.Activate(); else person.Deactivate();

        var roleIds = request.RoleIds.Distinct().ToList();
        var roles = await db.PersonRoleTypes.Where(r => roleIds.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != roleIds.Count)
            throw new DomainException("یک یا چند نقش انتخاب‌شده وجود ندارد.");
        var newlyAdded = roles.Where(r => !r.IsActive && !person.HasRole(r.Id)).ToList();
        if (newlyAdded.Count > 0)
            throw new DomainException($"نقش «{newlyAdded[0].Title}» غیرفعال است.");

        person.SetRoles(roleIds, await EmployeeRoleIdAsync(ct));
        person.SetAddresses(request.Addresses.Select(a =>
            new AddressData(a.Title, a.Province, a.City, a.Address, a.PostalCode, a.Phone, a.IsDefault)));
        person.SetBankAccounts(request.BankAccounts.Select(a =>
            new BankAccountData(a.BankName, a.Iban, a.AccountNo, a.CardNo, a.IsDefault)));
        if (person.Employee is not null)
            person.SetEmployee(ToEmployeeData(request.Employee ?? new EmployeeDto()), today);

        // یکتایی شناسه‌ها
        if (person.NationalId is { } nationalId
            && await db.Persons.AnyAsync(p => p.Id != person.Id && p.NationalId == nationalId, ct))
            throw new DomainException($"شخص دیگری با کد/شناسه ملی {nationalId} تعریف شده است.");
        if (person.ForeignNationalCode is { } foreignCode
            && await db.Persons.AnyAsync(p => p.Id != person.Id && p.ForeignNationalCode == foreignCode, ct))
            throw new DomainException($"شخص دیگری با کد فراگیر {foreignCode} تعریف شده است.");
        if (person.Employee?.EmployeeNo is { } employeeNo
            && await db.Persons.AnyAsync(p => p.Id != person.Id && p.Employee!.EmployeeNo == employeeNo, ct))
            throw new DomainException($"شماره پرسنلی {employeeNo} قبلاً به کارمند دیگری داده شده است.");
    }

    /// <summary>ساخت تفصیلی‌های لازم و هم‌نام/هم‌وضعیت کردن تفصیلی‌های موجود.</summary>
    private async Task SyncDetailsAsync(Person person, CancellationToken ct)
    {
        var employeeRoleId = await EmployeeRoleIdAsync(ct);
        var details = await db.DetailAccounts.Where(d => d.PersonId == person.Id).ToListAsync(ct);
        var typeIds = details.Select(d => d.DetailTypeId).ToList();
        var types = await db.DetailTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var needsParty = person.Roles.Any(r => r.RoleId != employeeRoleId);
        var needsEmployee = person.HasRole(employeeRoleId);

        var hasParty = details.Any(d => types[d.DetailTypeId].OwnerModule == OwnerModules.Persons);
        var hasEmployee = details.Any(d => types[d.DetailTypeId].OwnerModule == OwnerModules.Payroll);

        if (needsParty && !hasParty)
            details.Add(await NewDetailAsync(person, OwnerModules.Persons, ct));
        if (needsEmployee && !hasEmployee)
            details.Add(await NewDetailAsync(person, OwnerModules.Payroll, ct));

        foreach (var d in details)
        {
            if (d.Title != person.DisplayName) d.Rename(person.DisplayName);
            if (person.IsActive) d.Activate(); else d.Deactivate();
        }
    }

    private async Task<DetailAccount> NewDetailAsync(Person person, string ownerModule, CancellationToken ct)
    {
        var candidates = await db.DetailTypes
            .Where(t => t.IsActive && t.OwnerModule == ownerModule && (t.PersonKind == null || t.PersonKind == person.Kind))
            .ToListAsync(ct);
        // نوع مخصوص همین نوع شخص بر نوع عمومی (بدون نوع شخص) مقدم است
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

    private async Task<short> EmployeeRoleIdAsync(CancellationToken ct) =>
        await db.PersonRoleTypes.Where(r => r.Code == RoleCodes.Employee).Select(r => (short?)r.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("نقش سیستمی «کارمند» در دیتابیس نیست.");

    private async Task<Person> LoadAsync(int id, CancellationToken ct) =>
        await db.Persons.Include(p => p.Roles).Include(p => p.Addresses).Include(p => p.BankAccounts).Include(p => p.Employee)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException($"شخص با شناسه {id} پیدا نشد.");

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static PersonProfile ToProfile(SavePersonRequest r) => new()
    {
        FirstName = r.FirstName, LastName = r.LastName, FatherName = r.FatherName, Gender = r.Gender,
        BirthDate = ParseDate(r.BirthDate, "تاریخ تولد"), BirthPlace = r.BirthPlace, IdNumber = r.IdNumber,
        IdIssuePlace = r.IdIssuePlace, MaritalStatus = r.MaritalStatus, IsForeign = r.IsForeign,
        Nationality = r.Nationality, ForeignNationalCode = r.ForeignNationalCode, PassportNo = r.PassportNo,
        LegalName = r.LegalName, LegalType = r.LegalType, RegistrationNo = r.RegistrationNo,
        RegistrationDate = ParseDate(r.RegistrationDate, "تاریخ ثبت"), RegistrationPlace = r.RegistrationPlace,
        NationalId = r.NationalId, EconomicCode = r.EconomicCode, TaxPayerType = r.TaxPayerType, BranchCode = r.BranchCode,
        Mobile = r.Mobile, Phone = r.Phone, Email = r.Email, Website = r.Website, Notes = r.Notes
    };

    private static EmployeeData ToEmployeeData(EmployeeDto e) => new()
    {
        EmployeeNo = e.EmployeeNo, HireDate = ParseDate(e.HireDate, "تاریخ استخدام"),
        EndDate = ParseDate(e.EndDate, "تاریخ ترک کار"), ContractType = e.ContractType, JobTitle = e.JobTitle,
        InsuranceType = e.InsuranceType, InsuranceNo = e.InsuranceNo,
        InsuranceStartDate = ParseDate(e.InsuranceStartDate, "تاریخ شروع بیمه"), InsuranceJobCode = e.InsuranceJobCode,
        WorkshopCode = e.WorkshopCode, EducationLevel = e.EducationLevel, EducationField = e.EducationField,
        MilitaryStatus = e.MilitaryStatus, ChildrenCount = e.ChildrenCount, DependentsCount = e.DependentsCount,
        TaxExemptionType = e.TaxExemptionType
    };

    private static DateOnly? ParseDate(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return PersianDate.TryParse(PersianText.Normalize(value), out var d)
            ? d
            : throw new DomainException($"{label} نامعتبر است: {value}");
    }

    private static string? Date(DateOnly? d) => d is { } x ? PersianDate.ToPersian(x) : null;
}
