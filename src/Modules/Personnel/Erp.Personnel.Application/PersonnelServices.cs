using Erp.Personnel.Contracts;
using Erp.Personnel.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Personnel.Application;

/// <summary>مشاغل (با کد شغل بیمه). فهرست آزاد است؛ فرم پرونده لازمش دارد.</summary>
public class JobService(IPersonnelDbContext db, IAccessControl access)
{
    public async Task<List<JobDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.Employees.Where(e => e.JobId != null).GroupBy(e => e.JobId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await db.Jobs.AsNoTracking().Where(j => includeInactive || j.IsActive).OrderBy(j => j.Title).ToListAsync(ct))
            .Select(j => new JobDto(j.Id, j.Title, j.InsuranceJobCode, j.IsActive, counts.GetValueOrDefault(j.Id))).ToList();
    }

    public async Task<int> CreateAsync(SaveJobRequest request, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var job = new Job(request.Title, request.InsuranceJobCode);
        job.SetActive(request.IsActive);
        await EnsureUniqueAsync(job, ct);
        db.Add(job);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    public async Task UpdateAsync(int id, SaveJobRequest request, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("شغل پیدا نشد.");
        job.Update(request.Title, request.InsuranceJobCode);
        job.SetActive(request.IsActive);
        await EnsureUniqueAsync(job, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("شغل پیدا نشد.");
        if (await db.Employees.AnyAsync(e => e.JobId == id, ct))
            throw new DomainException($"شغل «{job.Title}» در پرونده پرسنلی آمده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(job);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(Job job, CancellationToken ct)
    {
        if (await db.Jobs.AnyAsync(j => j.Id != job.Id && j.Title == job.Title, ct))
            throw new DomainException($"شغل «{job.Title}» قبلاً تعریف شده است.");
    }
}

/// <summary>کارگاه‌های تأمین اجتماعی با نرخ‌های بیمه هر کارگاه.</summary>
public class WorkshopService(IPersonnelDbContext db, IAccessControl access)
{
    public async Task<List<WorkshopDto>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.Employees.GroupBy(e => e.WorkshopId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await db.Workshops.AsNoTracking().Where(w => includeInactive || w.IsActive).OrderBy(w => w.Code).ToListAsync(ct))
            .Select(w => new WorkshopDto(w.Id, w.Code, w.Title, w.EmployerName, w.Branch, w.ContractRow, w.Address, w.EmployeeRate,
                w.EmployerRate, w.UnemploymentRate, w.IsActive, counts.GetValueOrDefault(w.Id))).ToList();
    }

    public async Task<int> CreateAsync(SaveWorkshopRequest r, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var w = new Workshop(r.Code, r.Title);
        Apply(w, r);
        await EnsureUniqueAsync(w, ct);
        db.Add(w);
        await db.SaveChangesAsync(ct);
        return w.Id;
    }

    public async Task UpdateAsync(int id, SaveWorkshopRequest r, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var w = await db.Workshops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("کارگاه پیدا نشد.");
        Apply(w, r);
        await EnsureUniqueAsync(w, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.Setup);
        var w = await db.Workshops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("کارگاه پیدا نشد.");
        if (await db.Employees.AnyAsync(e => e.WorkshopId == id, ct))
            throw new DomainException($"کارگاه «{w.Title}» کارمند دارد و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(w);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Workshop w, SaveWorkshopRequest r)
    {
        w.Update(r.Code, r.Title, r.EmployerName, r.Branch, r.ContractRow, r.Address, r.EmployeeRate, r.EmployerRate, r.UnemploymentRate);
        w.SetActive(r.IsActive);
    }

    private async Task EnsureUniqueAsync(Workshop w, CancellationToken ct)
    {
        if (await db.Workshops.AnyAsync(x => x.Id != w.Id && x.Code == w.Code, ct))
            throw new DomainException($"کارگاه با کد {w.Code} قبلاً تعریف شده است.");
    }
}

/// <summary>
/// پرونده پرسنلی. فقط شخص حقیقی با نقش «کارمند» (تفصیلی کارکنان را اطلاعات پایه با همان نقش می‌سازد)؛
/// هر شخص یک پرونده. ترک کار و بازگشت به کار ثبت می‌شود؛ پرونده‌ای که در حقوق آمده حذف نمی‌شود.
/// </summary>
public class EmployeeService(IPersonnelDbContext db, IAccessControl access, TimeProvider clock)
{
    public async Task<List<EmployeeListItemDto>> ListAsync(string? term, int? workshopId, EmployeeStatus? status, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeView);
        var query = db.Employees.AsNoTracking();
        if (workshopId is int w) query = query.Where(e => e.WorkshopId == w);
        if (status is { } s) query = query.Where(e => e.Status == s);

        var rows = await query.Select(e => new
            {
                e.Id, e.PersonnelNo, e.PersonId, e.ContractType, e.HireDate, e.LeaveDate, e.Status,
                Person = db.Persons.Where(p => p.Id == e.PersonId).Select(p => new { p.DisplayName, p.NationalId }).First(),
                Workshop = db.Workshops.Where(x => x.Id == e.WorkshopId).Select(x => x.Title).First(),
                Job = db.Jobs.Where(j => j.Id == e.JobId).Select(j => j.Title).FirstOrDefault(),
                Center = db.Centers.Where(c => c.Id == e.CostCenterId).Select(c => c.Title).FirstOrDefault()
            })
            .OrderBy(e => e.PersonnelNo).ToListAsync(ct);

        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => r.Person.DisplayName.Contains(term) || r.PersonnelNo.Contains(term) || (r.Person.NationalId ?? "").StartsWith(term)).ToList();

        return rows.Select(r => new EmployeeListItemDto(r.Id, r.PersonnelNo, r.PersonId, r.Person.DisplayName, r.Person.NationalId, r.Workshop,
            r.Job, r.ContractType, PersianDate.ToPersian(r.HireDate), r.LeaveDate is { } l ? PersianDate.ToPersian(l) : null, r.Status,
            r.Center ?? "?")).ToList();
    }

    public async Task<EmployeeDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeView);
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
        var person = await db.Persons.FirstAsync(p => p.Id == e.PersonId, ct);
        return new EmployeeDto(e.Id, e.PersonId, person.DisplayName, person.NationalId, e.PersonnelNo, e.WorkshopId, e.JobId, e.ContractType,
            PersianDate.ToPersian(e.HireDate), e.LeaveDate is { } l ? PersianDate.ToPersian(l) : null, e.LeaveReason, e.InsuranceNo,
            e.InsuranceStatus, e.ChildrenCount, e.CostCenterId, e.Iban, e.TaxExemptPercent, e.Status, e.Notes);
    }

    public async Task<int> CreateAsync(SaveEmployeeRequest request, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeCreate);
        var e = new Employee(request.PersonId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(e, request, ct);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeEdit);
        var e = await LoadAsync(id, ct);
        if (e.PersonId != request.PersonId) throw new DomainException("شخص پرونده پرسنلی عوض نمی‌شود.");
        await ApplyAsync(e, request, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task LeaveAsync(int id, string date, string? reason, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeEdit);
        var e = await LoadAsync(id, ct);
        e.Leave(PersianDate.Parse(date), reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReturnAsync(int id, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeEdit);
        var e = await LoadAsync(id, ct);
        e.Return();
        await db.SaveChangesAsync(ct);
    }

    public async Task SuspendAsync(int id, bool suspended, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeEdit);
        var e = await LoadAsync(id, ct);
        e.Suspend(suspended);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PersonnelPermissions.EmployeeDelete);
        var e = await LoadAsync(id, ct);
        if (await db.IsEmployeeUsedAsync(id, ct))
            throw new DomainException($"پرونده {e.PersonnelNo} در حقوق (حکم یا کارکرد) آمده و حذف نمی‌شود؛ ترک کار ثبت کنید.");
        db.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>شماره پرسنلی بعدی (بزرگ‌ترین شماره عددی + ۱، از 1001).</summary>
    public async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var numbers = await db.Employees.Select(e => e.PersonnelNo).ToListAsync(ct);
        var max = numbers.Select(n => long.TryParse(n, out var v) ? v : 0).DefaultIfEmpty(1000).Max();
        return (Math.Max(max, 1000) + 1).ToString();
    }

    private async Task ApplyAsync(Employee e, SaveEmployeeRequest r, CancellationToken ct)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == r.PersonId, ct) ?? throw new DomainException("شخص انتخاب نشده یا پیدا نشد.");
        if (!person.IsNatural) throw new DomainException($"«{person.DisplayName}» شخص حقوقی است؛ پرونده پرسنلی فقط برای شخص حقیقی است.");
        if (!person.IsEmployee) throw new DomainException($"«{person.DisplayName}» نقش «کارمند» ندارد؛ اول در اطلاعات اشخاص نقش کارمند را اضافه کنید.");
        if (await db.Employees.AnyAsync(x => x.Id != e.Id && x.PersonId == r.PersonId, ct))
            throw new DomainException($"«{person.DisplayName}» پرونده پرسنلی دارد.");

        var workshop = await db.Workshops.FirstOrDefaultAsync(w => w.Id == r.WorkshopId, ct) ?? throw new DomainException("کارگاه انتخاب نشده یا پیدا نشد.");
        if (!workshop.IsActive && workshop.Id != e.WorkshopId) throw new DomainException($"کارگاه «{workshop.Title}» غیرفعال است.");
        if (r.JobId is int jobId && !await db.Jobs.AnyAsync(j => j.Id == jobId, ct)) throw new DomainException("شغل پیدا نشد.");
        var center = await db.Centers.FirstOrDefaultAsync(c => c.Id == r.CostCenterId, ct) ?? throw new DomainException("مرکز هزینه انتخاب نشده یا پیدا نشد.");
        if (!center.IsActive && center.Id != e.CostCenterId) throw new DomainException($"مرکز هزینه «{center.Title}» غیرفعال است.");

        var number = PersianText.NormalizeOrNull(r.PersonnelNo) ?? await NextNumberAsync(ct);
        if (await db.Employees.AnyAsync(x => x.Id != e.Id && x.PersonnelNo == number, ct))
            throw new DomainException($"شماره پرسنلی {number} قبلاً ثبت شده است.");

        e.Update(number, workshop.Id, r.JobId, r.ContractType, PersianDate.Parse(r.HireDate), r.InsuranceNo, r.InsuranceStatus, r.ChildrenCount,
            center.Id, r.Iban, r.TaxExemptPercent, r.Notes);
    }

    private async Task<Employee> LoadAsync(int id, CancellationToken ct) =>
        await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("پرونده پرسنلی پیدا نشد.");
}
