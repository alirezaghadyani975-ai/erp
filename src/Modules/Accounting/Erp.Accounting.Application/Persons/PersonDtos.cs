using Erp.Accounting.Application.Details;
using Erp.Accounting.Domain.BaseData;

namespace Erp.Accounting.Application.Persons;

public sealed record PersonRoleTypeDto(short Id, string? Code, string Title, bool IsSystem, bool IsActive);

public sealed class AddressDto
{
    public string? Title { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string Address { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class BankAccountDto
{
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public string? AccountNo { get; set; }
    public string? CardNo { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>اطلاعات پرسنلی؛ تاریخ‌ها شمسی (1405/01/01).</summary>
public sealed class EmployeeDto
{
    public string? EmployeeNo { get; set; }
    public string? HireDate { get; set; }
    public string? EndDate { get; set; }
    public ContractType? ContractType { get; set; }
    public string? JobTitle { get; set; }
    public InsuranceType? InsuranceType { get; set; }
    public string? InsuranceNo { get; set; }
    public string? InsuranceStartDate { get; set; }
    public string? InsuranceJobCode { get; set; }
    public string? WorkshopCode { get; set; }
    public EducationLevel? EducationLevel { get; set; }
    public string? EducationField { get; set; }
    public MilitaryStatus? MilitaryStatus { get; set; }
    public byte ChildrenCount { get; set; }
    public byte DependentsCount { get; set; }
    public TaxExemptionType? TaxExemptionType { get; set; }
}

/// <summary>
/// ثبت یا ویرایش شخص. فرم رابط کاربری مستقیماً به همین کلاس وصل است.
/// تاریخ‌ها شمسی‌اند؛ نوع شخص (حقیقی/حقوقی) بعد از ثبت عوض نمی‌شود.
/// </summary>
public class SavePersonRequest
{
    public PersonKind Kind { get; set; } = PersonKind.Natural;
    public bool IsActive { get; set; } = true;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FatherName { get; set; }
    public Gender? Gender { get; set; }
    public string? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? IdNumber { get; set; }
    public string? IdIssuePlace { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public bool IsForeign { get; set; }
    public string? Nationality { get; set; }
    public string? ForeignNationalCode { get; set; }
    public string? PassportNo { get; set; }

    public string? LegalName { get; set; }
    public LegalType? LegalType { get; set; }
    public string? RegistrationNo { get; set; }
    public string? RegistrationDate { get; set; }
    public string? RegistrationPlace { get; set; }

    public string? NationalId { get; set; }
    public string? EconomicCode { get; set; }
    public TaxPayerType? TaxPayerType { get; set; }
    public string? BranchCode { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }

    public List<short> RoleIds { get; set; } = [];
    public List<AddressDto> Addresses { get; set; } = [];
    public List<BankAccountDto> BankAccounts { get; set; } = [];
    public EmployeeDto? Employee { get; set; }
}

/// <summary>شخص با تفصیلی‌هایش (تفصیلی مشترک طرف‌حساب و تفصیلی کارمند).</summary>
public sealed class PersonDetailsDto : SavePersonRequest
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public List<DetailAccountDto> Details { get; set; } = [];
}

public sealed record PersonListItemDto(int Id, PersonKind Kind, string DisplayName, string? NationalId, string? Mobile,
    bool IsActive, List<string> Roles, string? DetailCode, string? EmployeeDetailCode);
