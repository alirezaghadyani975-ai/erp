using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Persons;

namespace Erp.BaseData.Application.Persons;

public sealed record PersonRoleTypeDto(short Id, string? Code, string Title, bool IsSystem, bool IsActive, int PersonCount = 0);

public sealed class AddressDto
{
    public int? AddressTypeId { get; set; }
    public int? ProvinceId { get; set; }
    public int? CityId { get; set; }
    public string Address { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class BankAccountDto
{
    /// <summary>خالی = از شبا یا کارت تشخیص داده می‌شود.</summary>
    public short? BankId { get; set; }
    public string? Iban { get; set; }
    public string? AccountNo { get; set; }
    public string? CardNo { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class ContactDto
{
    public string FullName { get; set; } = "";
    public int? ContactRoleId { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// ثبت یا ویرایش شخص. فرم رابط کاربری مستقیماً به همین کلاس وصل است.
/// تاریخ‌ها شمسی‌اند؛ فیلدهای *Id اقلام جدول پایه‌اند؛ نوع شخص بعد از ثبت عوض نمی‌شود.
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
    public int? BirthCityId { get; set; }
    public string? IdNumber { get; set; }
    public int? IdIssueCityId { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public bool IsForeign { get; set; }
    public int? NationalityId { get; set; }
    public string? ForeignNationalCode { get; set; }
    public string? PassportNo { get; set; }

    public string? LegalName { get; set; }
    public LegalType? LegalType { get; set; }
    public string? RegistrationNo { get; set; }
    public string? RegistrationDate { get; set; }
    public int? RegistrationCityId { get; set; }

    public string? NationalId { get; set; }
    public string? EconomicCode { get; set; }
    public TaxPayerType? TaxPayerType { get; set; }
    public string? BranchCode { get; set; }
    public int? PersonGroupId { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }

    public List<short> RoleIds { get; set; } = [];
    public List<AddressDto> Addresses { get; set; } = [];
    public List<BankAccountDto> BankAccounts { get; set; } = [];
    public List<ContactDto> Contacts { get; set; } = [];
}

public sealed record AttachmentDto(int Id, string Title, string FileName, string ContentType, int SizeBytes, string UploadedAt);

public sealed record AttachmentFile(string FileName, string ContentType, byte[] Content);

/// <summary>شخص با رکوردهای وابسته‌اش در ماژول‌های دیگر (مثلاً تفصیلی‌ها) و پیوست‌ها.</summary>
public sealed class PersonDetailsDto : SavePersonRequest
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public List<PersonLinkInfo> Links { get; set; } = [];
    public List<AttachmentDto> Attachments { get; set; } = [];
}

public sealed record PersonListItemDto(int Id, PersonKind Kind, string DisplayName, string? NationalId, string? Mobile,
    bool IsActive, List<string> Roles, string? GroupTitle, string? DetailCode, string? EmployeeDetailCode);

/// <summary>شخص مشابه برای هشدار پیش از ذخیره.</summary>
public sealed record DuplicateDto(int Id, string DisplayName, string Reason);
