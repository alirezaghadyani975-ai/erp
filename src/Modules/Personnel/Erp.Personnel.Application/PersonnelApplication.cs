using Erp.Personnel.Contracts;
using Erp.Personnel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Personnel.Application;

public interface IPersonnelDbContext
{
    IQueryable<Job> Jobs { get; }
    IQueryable<Workshop> Workshops { get; }
    IQueryable<Employee> Employees { get; }

    /// <summary>فقط‌خواندنی از اطلاعات پایه: اشخاص با نشان نقش کارمند.</summary>
    IQueryable<PersonRef> Persons { get; }

    /// <summary>فقط‌خواندنی از بهای تمام‌شده: مراکز هزینه.</summary>
    IQueryable<CenterRef> Centers { get; }

    /// <summary>پرونده در حقوق (حکم یا کارکرد) استفاده شده است؟</summary>
    Task<bool> IsEmployeeUsedAsync(int employeeId, CancellationToken ct);

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class PersonRef
{
    public int Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string? NationalId { get; init; }
    public bool IsNatural { get; init; }
    public bool IsEmployee { get; init; }
    public bool IsActive { get; init; }
}

public sealed class CenterRef
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public static class PersonnelPermissions
{
    public const string EmployeeView = "hr.employee.view";
    public const string EmployeeCreate = "hr.employee.create";
    public const string EmployeeEdit = "hr.employee.edit";
    public const string EmployeeDelete = "hr.employee.delete";
    public const string Setup = "hr.setup.edit";

    public static IPermissionSource List { get; } = new PermissionList("کارگزینی")
        .Crud("hr.employee", "پرونده پرسنلی")
        .Add("hr.setup", "کارگاه‌ها و مشاغل", ("edit", "تعریف و اصلاح"));
}

public sealed record JobDto(int Id, string Title, string? InsuranceJobCode, bool IsActive, int EmployeeCount);

public sealed record SaveJobRequest(string Title, string? InsuranceJobCode, bool IsActive = true);

public sealed record WorkshopDto(int Id, string Code, string Title, string? EmployerName, string? Branch, string ContractRow, string? Address,
    decimal EmployeeRate, decimal EmployerRate, decimal UnemploymentRate, bool IsActive, int EmployeeCount);

public sealed record SaveWorkshopRequest(string Code, string Title, string? EmployerName = null, string? Branch = null, string? ContractRow = null,
    string? Address = null, decimal EmployeeRate = 7, decimal EmployerRate = 20, decimal UnemploymentRate = 3, bool IsActive = true);

public sealed record EmployeeListItemDto(int Id, string PersonnelNo, int PersonId, string Name, string? NationalId, string Workshop,
    string? Job, ContractType ContractType, string HireDate, string? LeaveDate, EmployeeStatus Status, string CostCenter);

public sealed record EmployeeDto(int Id, int PersonId, string PersonName, string? NationalId, string PersonnelNo, int WorkshopId, int? JobId,
    ContractType ContractType, string HireDate, string? LeaveDate, string? LeaveReason, string? InsuranceNo, InsuranceStatus InsuranceStatus,
    byte ChildrenCount, int CostCenterId, string? Iban, decimal? TaxExemptPercent, EmployeeStatus Status, string? Notes,
    byte? Education, byte? JobCategory, byte EmploymentType, byte TaxExemption, byte ServiceLocation);

/// <param name="PersonnelNo">خالی = شماره بعدی خودکار.</param>
public sealed record SaveEmployeeRequest(int PersonId, string? PersonnelNo, int WorkshopId, int? JobId, ContractType ContractType, string HireDate,
    string? InsuranceNo, InsuranceStatus InsuranceStatus, byte ChildrenCount, int CostCenterId, string? Iban = null,
    decimal? TaxExemptPercent = null, string? Notes = null, byte? Education = null, byte? JobCategory = null, byte EmploymentType = 1,
    byte TaxExemption = 1, byte ServiceLocation = 1);

public static class DependencyInjection
{
    public static IServiceCollection AddPersonnelApplication(this IServiceCollection services)
    {
        services.AddSingleton(PersonnelPermissions.List);
        services.AddScoped<JobService>();
        services.AddScoped<WorkshopService>();
        services.AddScoped<EmployeeService>();
        return services;
    }
}
