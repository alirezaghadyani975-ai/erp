using Erp.Costing.Contracts;
using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Payroll.Application;

public interface IPayrollDbContext
{
    IQueryable<PayItem> PayItems { get; }
    IQueryable<YearParam> YearParams { get; }
    IQueryable<PaySettings> Settings { get; }
    IQueryable<Decree> Decrees { get; }

    /// <summary>فقط‌خواندنی از کارگزینی.</summary>
    IQueryable<EmployeeRef> Employees { get; }

    IQueryable<FactorRef> Factors { get; }
    IQueryable<AccountRef> Accounts { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class FactorRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public CostNature Nature { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>معین‌های فعال (سطح ۳).</summary>
public sealed class AccountRef
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public static class PayrollPermissions
{
    public const string Setup = "pay.setup.edit";
    public const string DecreeView = "pay.decree.view";
    public const string DecreeCreate = "pay.decree.create";
    public const string DecreeEdit = "pay.decree.edit";
    public const string DecreeDelete = "pay.decree.delete";

    public static IPermissionSource List { get; } = new PermissionList("حقوق و دستمزد")
        .Add("pay.setup", "اقلام، پارامترهای سال و تنظیمات حقوق", ("edit", "تعریف و اصلاح"))
        .Crud("pay.decree", "حکم حقوقی");
}

public sealed record PayItemDto(int Id, string Code, string Title, PayItemKind Kind, PayItemSource Source, string? Formula,
    bool InsuranceSubject, bool TaxSubject, int? CostFactorId, string? CostFactor, int? AccountId, string? Account,
    bool OnPayslip, int SortOrder, bool IsSystem, bool IsActive);

public sealed record SavePayItemRequest(string Code, string Title, PayItemKind Kind, PayItemSource Source, string? Formula,
    bool InsuranceSubject, bool TaxSubject, int? CostFactorId, int? AccountId, bool OnPayslip = true, int SortOrder = 100,
    bool IsActive = true);

/// <summary>یک متغیر قابل استفاده در فرمول (برای راهنمای فرم).</summary>
public sealed record FormulaVariableDto(string Code, string Title, string Group);

public sealed record FormulaCheckDto(bool Ok, string? Error, List<string> Variables);

public sealed record YearParamDto(short Year, string Code, string Title, decimal Value);

public sealed record SaveYearParamRequest(string Code, string Title, decimal Value);

public sealed record PaySettingsDto(MonthDaysMode MonthDaysMode);

public sealed record DecreeListItemDto(int Id, int DocNo, int EmployeeId, string PersonnelNo, string Employee, string EffectiveDate,
    DecreeType DecreeType, decimal DailyWage, string? Description);

public sealed record EmployeeOptionDto(int Id, string PersonnelNo, string Name, EmployeeStatus Status);

public sealed record DecreeItemDto(int PayItemId, string Code, string Title, decimal Amount);

public sealed record DecreeDto(int Id, int DocNo, int EmployeeId, string PersonnelNo, string Employee, string EffectiveDate,
    DecreeType DecreeType, string? Description, List<DecreeItemDto> Items);

public sealed record DecreeItemInput(int PayItemId, decimal Amount);

public sealed record SaveDecreeRequest(int EmployeeId, string EffectiveDate, DecreeType DecreeType, string? Description, List<DecreeItemInput> Items);

public static class DependencyInjection
{
    public static IServiceCollection AddPayrollApplication(this IServiceCollection services)
    {
        services.AddSingleton(PayrollPermissions.List);
        services.AddScoped<PayItemService>();
        services.AddScoped<YearParamService>();
        services.AddScoped<PaySettingsService>();
        services.AddScoped<DecreeService>();
        return services;
    }
}
