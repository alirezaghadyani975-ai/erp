using Erp.Payroll.Domain;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>طرح محاسبه یک کارمند در یک ماه: الگو، محاسبه‌گر اقلام الگو، پارامترهای سال با جایگزین گروه و نرخ‌های بیمه گروه.</summary>
/// <param name="EmployeeRate">نرخ سهم کارگر گروه (null = نرخ کارگاه)</param>
public sealed record PayPlan(int? TemplateId, string TemplateTitle, PayCalculator Calculator, IReadOnlyDictionary<string, decimal> Parameters,
    decimal? EmployeeRate, decimal? EmployerRate, decimal? UnemploymentRate);

/// <summary>
/// پیدا کردن الگوی معتبر هر کارمند در تاریخ (آخرین اتصال با «از تاریخ» تا آن روز) و ساخت طرح محاسبه.
/// کارمند بدون اتصال: الگوی «پیش‌فرض» (کد DEFAULT) و اگر نبود همه اقلام فعال با پارامترهای سال (رفتار پیش از الگو).
/// </summary>
public static class PayPlans
{
    public const string DefaultCode = "DEFAULT";

    public static async Task<Dictionary<int, PayPlan>> LoadAsync(IPayrollDbContext db, IReadOnlyCollection<int> employeeIds, DateOnly asOf,
        IReadOnlyDictionary<string, decimal> yearParams, CancellationToken ct)
    {
        var ids = employeeIds.ToList();
        var assigned = (await db.EmployeeTemplates.AsNoTracking().Where(t => ids.Contains(t.EmployeeId) && t.FromDate <= asOf).ToListAsync(ct))
            .GroupBy(t => t.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.FromDate).First().TemplateId);
        var fallback = await db.Templates.AsNoTracking().Where(t => t.Code == DefaultCode).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);

        var templateIds = assigned.Values.Append(fallback ?? 0).Where(id => id > 0).Distinct().ToList();
        var templates = await db.Templates.AsNoTracking().Include(t => t.Items).Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var groupIds = templates.Values.Select(t => t.GroupId).Distinct().ToList();
        var groups = await db.Groups.AsNoTracking().Include(g => g.Params).Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var items = await db.PayItems.AsNoTracking().ToListAsync(ct);

        var cache = new Dictionary<int, PayPlan>();
        PayPlan Build(int? templateId)
        {
            if (templateId is not int id || !templates.TryGetValue(id, out var t))
                return new PayPlan(null, "همه اقلام (بدون الگو)", new PayCalculator(items.Where(i => i.IsActive)), yearParams, null, null, null);
            if (cache.TryGetValue(id, out var plan)) return plan;
            var included = t.Items.ToDictionary(i => i.PayItemId);
            var calc = new PayCalculator(items.Where(i => included.ContainsKey(i.Id)), included.ToDictionary(x => x.Key, x => x.Value.Formula));
            var group = groups[t.GroupId];
            var parameters = new Dictionary<string, decimal>(yearParams);
            foreach (var p in group.Params) parameters[p.Code] = p.Value;
            return cache[id] = new PayPlan(t.Id, t.Title, calc, parameters, group.EmployeeRate, group.EmployerRate, group.UnemploymentRate);
        }

        try
        {
            return ids.ToDictionary(id => id, id => Build(assigned.TryGetValue(id, out var t) ? t : fallback));
        }
        catch (DomainException ex)
        {
            throw new DomainException($"فرمول‌های الگوی محاسبه: {ex.Message}");
        }
    }
}
