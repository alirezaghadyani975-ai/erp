using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

public sealed record GroupParamDto(string Code, decimal Value);

public sealed record EmploymentGroupDto(int Id, string Code, string Title, decimal? EmployeeRate, decimal? EmployerRate, decimal? UnemploymentRate,
    bool IsActive, string? Notes, List<GroupParamDto> Params, int TemplateCount);

public sealed record SaveGroupRequest(string Code, string Title, decimal? EmployeeRate, decimal? EmployerRate, decimal? UnemploymentRate,
    bool IsActive, string? Notes, List<GroupParamDto> Params);

/// <summary>قاعده استاندارد گروه با مقدار پارامتر سال جاری (برای نمایش پیش‌فرض در فرم گروه).</summary>
public sealed record GroupRuleDto(string Code, string Section, string Title, decimal? YearValue);

/// <param name="Formula">فرمول جایگزین در الگو (null = پیش‌فرض قلم)</param>
public sealed record TemplateItemDto(int PayItemId, string? Formula);

public sealed record CalcTemplateDto(int Id, string Code, string Title, int GroupId, bool IsActive, string? Notes, List<TemplateItemDto> Items,
    int EmployeeCount);

public sealed record CalcTemplateListItemDto(int Id, string Code, string Title, int GroupId, string Group, bool IsActive, int ItemCount, int EmployeeCount);

public sealed record SaveTemplateRequest(string Code, string Title, int GroupId, bool IsActive, string? Notes, List<TemplateItemDto> Items);

/// <summary>کارمند با الگوی فعلی (برای اتصال گروهی با فیلتر).</summary>
public sealed record TemplateEmployeeDto(int Id, string PersonnelNo, string Name, int WorkshopId, string Workshop, string? JobTitle,
    int CostCenterId, EmployeeStatus Status, int? TemplateId, string? Template, string? FromDate);

public sealed record EmployeeTemplateDto(int Id, int TemplateId, string Template, string FromDate);

public sealed record AssignTemplateRequest(List<int> EmployeeIds, int TemplateId, string FromDate);

/// <summary>
/// گروه‌های استخدامی (قواعد عیدی، سنوات، مرخصی، کارکرد و بیمه)، الگوهای محاسبه (اقلام و فرمول جایگزین) و اتصال تاریخ‌دار کارکنان.
/// تغییر الگو حقوق قطعی‌شده را عوض نمی‌کند؛ ماه‌های باز در محاسبه بعدی الگوی معتبر پایان ماه را می‌گیرند.
/// </summary>
public class PayTemplateService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock)
{
    // ---------------- گروه استخدامی ----------------

    public async Task<List<EmploymentGroupDto>> GroupsAsync(CancellationToken ct)
    {
        var counts = await db.Templates.GroupBy(t => t.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await db.Groups.AsNoTracking().Include(g => g.Params).OrderBy(g => g.Code).ToListAsync(ct))
            .Select(g => ToDto(g, counts.GetValueOrDefault(g.Id))).ToList();
    }

    /// <summary>قواعد استاندارد با مقدار پارامتر سال (آخرین سال تعریف‌شده).</summary>
    public async Task<List<GroupRuleDto>> RulesAsync(CancellationToken ct)
    {
        var lastYear = await db.YearParams.MaxAsync(p => (short?)p.Year, ct);
        var values = lastYear is short y
            ? await db.YearParams.Where(p => p.Year == y).ToDictionaryAsync(p => p.Code, p => p.Value, ct)
            : [];
        return GroupRules.Standard.Select(r => new GroupRuleDto(r.Code, r.Section, r.Title, values.TryGetValue(r.Code, out var v) ? v : null)).ToList();
    }

    public async Task<int> CreateGroupAsync(SaveGroupRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateCreate);
        await EnsureUniqueGroupAsync(r.Code, null, ct);
        var g = new EmploymentGroup(r.Code, r.Title);
        g.Update(r.Code, r.Title, r.EmployeeRate, r.EmployerRate, r.UnemploymentRate, r.IsActive, r.Notes);
        g.SetParams(r.Params.Select(p => (p.Code, p.Value)));
        db.Add(g);
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task UpdateGroupAsync(int id, SaveGroupRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateEdit);
        var g = await db.Groups.Include(x => x.Params).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("گروه استخدامی پیدا نشد.");
        await EnsureUniqueGroupAsync(r.Code, id, ct);
        g.Update(r.Code, r.Title, r.EmployeeRate, r.EmployerRate, r.UnemploymentRate, r.IsActive, r.Notes);
        g.SetParams(r.Params.Select(p => (p.Code, p.Value)));
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateDelete);
        var g = await db.Groups.Include(x => x.Params).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("گروه استخدامی پیدا نشد.");
        if (await db.Templates.AnyAsync(t => t.GroupId == id, ct))
            throw new DomainException($"گروه «{g.Title}» در الگوی محاسبه به کار رفته است؛ اول الگو را به گروه دیگری ببرید یا غیرفعال کنید.");
        db.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- الگوی محاسبه ----------------

    public async Task<List<CalcTemplateListItemDto>> TemplatesAsync(CancellationToken ct)
    {
        var groups = await db.Groups.ToDictionaryAsync(g => g.Id, g => g.Title, ct);
        var current = await CurrentAssignmentsAsync(null, ct);
        var counts = current.Values.GroupBy(x => x.TemplateId).ToDictionary(g => g.Key, g => g.Count());
        return (await db.Templates.AsNoTracking().Include(t => t.Items).OrderBy(t => t.Code).ToListAsync(ct))
            .Select(t => new CalcTemplateListItemDto(t.Id, t.Code, t.Title, t.GroupId, groups.GetValueOrDefault(t.GroupId, "?"), t.IsActive, t.Items.Count,
                counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<CalcTemplateDto> TemplateAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateView);
        var t = await db.Templates.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("الگوی محاسبه پیدا نشد.");
        var current = await CurrentAssignmentsAsync(null, ct);
        return new CalcTemplateDto(t.Id, t.Code, t.Title, t.GroupId, t.IsActive, t.Notes,
            t.Items.Select(i => new TemplateItemDto(i.PayItemId, i.Formula)).ToList(), current.Values.Count(x => x.TemplateId == t.Id));
    }

    public async Task<int> CreateTemplateAsync(SaveTemplateRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateCreate);
        await EnsureUniqueTemplateAsync(r.Code, null, ct);
        var t = new CalcTemplate(r.Code, r.Title, r.GroupId);
        t.Update(r.Code, r.Title, await GroupIdAsync(r.GroupId, ct), r.IsActive, r.Notes);
        t.SetItems(await ItemsAsync(r.Items, ct));
        db.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task UpdateTemplateAsync(int id, SaveTemplateRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateEdit);
        var t = await db.Templates.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("الگوی محاسبه پیدا نشد.");
        await EnsureUniqueTemplateAsync(r.Code, id, ct);
        t.Update(r.Code, r.Title, await GroupIdAsync(r.GroupId, ct), r.IsActive, r.Notes);
        t.SetItems(await ItemsAsync(r.Items, ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>کپی الگو با کد و عنوان تازه (همان گروه، اقلام و فرمول‌ها).</summary>
    public async Task<int> CopyTemplateAsync(int id, string code, string title, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateCreate);
        var source = await TemplateAsync(id, ct);
        return await CreateTemplateAsync(new SaveTemplateRequest(code, title, source.GroupId, true, source.Notes, source.Items), ct);
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateDelete);
        var t = await db.Templates.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("الگوی محاسبه پیدا نشد.");
        if (await db.EmployeeTemplates.AnyAsync(x => x.TemplateId == id, ct))
            throw new DomainException($"الگوی «{t.Title}» به کارکنان وصل است؛ اول اتصال‌ها را عوض کنید یا الگو را غیرفعال کنید.");
        if (await db.Runs.AnyAsync(r => r.Attendances.Any(a => a.TemplateId == id), ct))
            throw new DomainException($"الگوی «{t.Title}» در محاسبه حقوق به کار رفته است و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- اتصال کارکنان ----------------

    /// <summary>کارکنان با الگوی معتبر امروز (برای فیلتر و اتصال گروهی).</summary>
    public async Task<List<TemplateEmployeeDto>> EmployeesAsync(CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateView);
        var employees = await db.Employees.OrderBy(e => e.PersonnelNo).ToListAsync(ct);
        var current = await CurrentAssignmentsAsync(null, ct);
        var templates = await db.Templates.ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        return employees.Select(e =>
        {
            var a = current.GetValueOrDefault(e.Id);
            return new TemplateEmployeeDto(e.Id, e.PersonnelNo, e.DisplayName, e.WorkshopId, e.WorkshopTitle, e.JobTitle, e.CostCenterId, e.Status,
                a?.TemplateId, a is null ? null : templates.GetValueOrDefault(a.TemplateId), a is null ? null : PersianDate.ToPersian(a.FromDate));
        }).ToList();
    }

    public async Task<List<EmployeeTemplateDto>> HistoryAsync(int employeeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateView);
        var templates = await db.Templates.ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        return (await db.EmployeeTemplates.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.FromDate).ToListAsync(ct))
            .Select(x => new EmployeeTemplateDto(x.Id, x.TemplateId, templates.GetValueOrDefault(x.TemplateId, "?"), PersianDate.ToPersian(x.FromDate))).ToList();
    }

    /// <summary>اتصال تکی یا گروهی از تاریخ؛ اگر کارمند در همان تاریخ اتصال دارد، الگویش عوض می‌شود. برمی‌گرداند: تعداد کارکنان.</summary>
    public async Task<int> AssignAsync(AssignTemplateRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateAssign);
        if (r.EmployeeIds.Count == 0) throw new DomainException("دست‌کم یک کارمند انتخاب کنید.");
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == r.TemplateId, ct) ?? throw new NotFoundException("الگوی محاسبه پیدا نشد.");
        if (!template.IsActive) throw new DomainException($"الگوی «{template.Title}» غیرفعال است.");
        var from = PersianDate.Parse(r.FromDate);
        var ids = r.EmployeeIds.Distinct().ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        if (employees.Count != ids.Count) throw new DomainException("کارمند پیدا نشد.");
        var finalized = await FinalizedThroughAsync(ids, ct);
        var existing = await db.EmployeeTemplates.Where(x => ids.Contains(x.EmployeeId) && x.FromDate == from).ToDictionaryAsync(x => x.EmployeeId, ct);
        foreach (var id in ids)
        {
            if (finalized.TryGetValue(id, out var last) && from <= last)
                throw new DomainException($"{employees[id].DisplayName}: حقوق تا {PersianDate.ToPersian(last)} قطعی شده است؛ الگوی تازه از بعد از آن تاریخ.");
            if (existing.TryGetValue(id, out var x)) x.ChangeTemplate(template.Id);
            else db.Add(new EmployeeTemplate(id, template.Id, from, user.UserId, clock.GetUtcNow().UtcDateTime));
        }
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task RemoveAssignmentAsync(int assignmentId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.TemplateAssign);
        var x = await db.EmployeeTemplates.FirstOrDefaultAsync(a => a.Id == assignmentId, ct) ?? throw new NotFoundException("اتصال پیدا نشد.");
        var finalized = await FinalizedThroughAsync([x.EmployeeId], ct);
        if (finalized.TryGetValue(x.EmployeeId, out var last) && x.FromDate <= last)
            throw new DomainException($"این اتصال ماه‌های قطعی‌شده (تا {PersianDate.ToPersian(last)}) را می‌پوشاند و حذف نمی‌شود.");
        db.Remove(x);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- کمکی ----------------

    /// <summary>آخرین روز ماهی که حقوق هر کارمند در آن قطعی شده است.</summary>
    private async Task<Dictionary<int, DateOnly>> FinalizedThroughAsync(List<int> ids, CancellationToken ct)
    {
        var rows = await db.Runs.AsNoTracking().Where(r => r.Status >= PayRunStatus.Final)
            .SelectMany(r => r.Attendances.Where(a => ids.Contains(a.EmployeeId)), (r, a) => new { a.EmployeeId, r.Year, r.Month })
            .ToListAsync(ct);
        return rows.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g =>
        {
            var last = g.OrderByDescending(x => x.Year * 100 + x.Month).First();
            return PersianDate.FromPersian(last.Year, last.Month, PersianDate.DaysInMonth(last.Year, last.Month));
        });
    }

    private async Task<Dictionary<int, EmployeeTemplate>> CurrentAssignmentsAsync(DateOnly? asOf, CancellationToken ct)
    {
        var day = asOf ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return (await db.EmployeeTemplates.AsNoTracking().Where(x => x.FromDate <= day).ToListAsync(ct))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FromDate).First());
    }

    private async Task<int> GroupIdAsync(int groupId, CancellationToken ct) =>
        await db.Groups.AnyAsync(g => g.Id == groupId, ct) ? groupId : throw new DomainException("گروه استخدامی الگو را انتخاب کنید.");

    private async Task<List<(PayItem, string?)>> ItemsAsync(List<TemplateItemDto> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.PayItemId).ToList();
        var items = await db.PayItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return rows.Select(r => (items.GetValueOrDefault(r.PayItemId) ?? throw new DomainException("قلم حقوق پیدا نشد."), r.Formula)).ToList();
    }

    private async Task EnsureUniqueGroupAsync(string code, int? exceptId, CancellationToken ct)
    {
        var c = Erp.SharedKernel.Text.PersianText.NormalizeOrNull(code);
        if (c is not null && await db.Groups.AnyAsync(g => g.Code == c && g.Id != exceptId, ct)) throw new DomainException($"کد گروه «{c}» تکراری است.");
    }

    private async Task EnsureUniqueTemplateAsync(string code, int? exceptId, CancellationToken ct)
    {
        var c = Erp.SharedKernel.Text.PersianText.NormalizeOrNull(code);
        if (c is not null && await db.Templates.AnyAsync(t => t.Code == c && t.Id != exceptId, ct)) throw new DomainException($"کد الگو «{c}» تکراری است.");
    }

    private static EmploymentGroupDto ToDto(EmploymentGroup g, int templates) =>
        new(g.Id, g.Code, g.Title, g.EmployeeRate, g.EmployerRate, g.UnemploymentRate, g.IsActive, g.Notes,
            g.Params.OrderBy(p => p.Code).Select(p => new GroupParamDto(p.Code, p.Value)).ToList(), templates);
}
