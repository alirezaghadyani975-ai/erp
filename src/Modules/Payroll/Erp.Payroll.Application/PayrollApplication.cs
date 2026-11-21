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
    IQueryable<PayRun> Runs { get; }

    /// <summary>فقط‌خواندنی از کارگزینی.</summary>
    IQueryable<EmployeeRef> Employees { get; }

    IQueryable<FactorRef> Factors { get; }
    IQueryable<AccountRef> Accounts { get; }
    IQueryable<CenterRef> Centers { get; }
    IQueryable<WorkshopRef> Workshops { get; }
    IQueryable<PersonInfoRef> PersonInfos { get; }

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
    public const string RunView = "pay.run.view";
    public const string RunCreate = "pay.run.create";
    public const string RunEdit = "pay.run.edit";
    public const string RunDelete = "pay.run.delete";

    public static IPermissionSource List { get; } = new PermissionList("حقوق و دستمزد")
        .Add("pay.setup", "اقلام، پارامترهای سال و تنظیمات حقوق", ("edit", "تعریف و اصلاح"))
        .Crud("pay.decree", "حکم حقوقی")
        .Crud("pay.run", "اجرای حقوق (کارکرد، محاسبه، مالیات)");
}

public sealed record PayItemDto(int Id, string Code, string Title, PayItemKind Kind, PayItemSource Source, string? Formula,
    bool InsuranceSubject, bool TaxSubject, int? CostFactorId, string? CostFactor, int? AccountId, string? Account,
    bool OnPayslip, int SortOrder, bool IsSystem, bool IsActive, bool InsuranceWage, byte? TaxField);

public sealed record SavePayItemRequest(string Code, string Title, PayItemKind Kind, PayItemSource Source, string? Formula,
    bool InsuranceSubject, bool TaxSubject, int? CostFactorId, int? AccountId, bool OnPayslip = true, int SortOrder = 100,
    bool IsActive = true, bool InsuranceWage = false, byte? TaxField = null);

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
        services.AddScoped<PayRunService>();
        services.AddScoped<Files.PayrollFilesService>();
        return services;
    }
}

public sealed class CenterRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed record PayRunListItemDto(int Id, short Year, byte Month, string Title, int WorkshopId, string Workshop, PayRunStatus Status,
    int EmployeeCount, decimal Earnings, decimal Deductions, decimal Net, decimal EmployerShare, string? CalculatedAt, string? Description);

public sealed record CreatePayRunRequest(short Year, byte Month, int WorkshopId, string? Description = null);

/// <summary>ستون ورودی ماه در جدول کارکرد (اقلام ورود ماهانه، وام و مساعده، مالیات).</summary>
public sealed record InputColumnDto(int PayItemId, string Code, string Title, PayItemSource Source, PayItemKind Kind);

public sealed record CenterShareDto(int CostCenterId, string Title, decimal Percent);

public sealed record AttendanceRowDto(int Id, int EmployeeId, string PersonnelNo, string Name, string? NationalId, decimal WorkDays,
    int InsuranceDays, decimal OtHours, decimal NightHours, decimal HolidayHours, decimal AbsentDays, List<CenterShareDto> Centers,
    Dictionary<int, decimal> Inputs);

public sealed record PayRunDto(int Id, short Year, byte Month, string Title, int WorkshopId, string Workshop, PayRunStatus Status,
    int MonthDays, int CalendarDays, string? Description, string? CalculatedAt, List<InputColumnDto> InputColumns, List<AttendanceRowDto> Rows);

public sealed record CenterShareInput(int CostCenterId, decimal Percent);

public sealed record SaveAttendanceRequest(int AttendanceId, decimal WorkDays, int InsuranceDays, decimal OtHours, decimal NightHours,
    decimal HolidayHours, decimal AbsentDays, List<CenterShareInput> Centers, Dictionary<int, decimal> Inputs);

public sealed record CalcWarningDto(string PersonnelNo, string Employee, string Message);

public sealed record CalcResultDto(int Employees, decimal Earnings, decimal Deductions, decimal Net, decimal EmployerShare, List<CalcWarningDto> Warnings);

/// <summary>یک سطر لیست حقوق.</summary>
public sealed record RunSummaryRowDto(int EmployeeId, string PersonnelNo, string Name, string? NationalId, decimal WorkDays, int InsuranceDays,
    decimal Earnings, decimal InsuranceSubject, decimal TaxSubject, decimal InsuranceWorker, decimal Tax, decimal OtherDeductions, decimal Net,
    decimal EmployerShare, decimal Provisions);

public sealed record PayslipLineDto(string Code, string Title, decimal Amount);

public sealed record PayslipDto(string RunTitle, string Workshop, string PersonnelNo, string Name, string? NationalId, decimal WorkDays,
    int InsuranceDays, decimal OtHours, decimal NightHours, decimal HolidayHours, decimal AbsentDays, List<PayslipLineDto> Earnings,
    List<PayslipLineDto> Deductions, List<PayslipLineDto> EmployerShares, decimal TotalEarnings, decimal TotalDeductions, decimal Net,
    decimal InsuranceSubject);

/// <param name="Key">کد ملی یا شماره پرسنلی</param>
public sealed record TaxRowInput(string Key, decimal Amount);

public sealed record TaxImportResultDto(int Matched, List<string> Unmatched, CalcResultDto Calculation);
