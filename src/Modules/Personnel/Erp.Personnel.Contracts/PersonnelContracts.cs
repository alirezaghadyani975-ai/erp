namespace Erp.Personnel.Contracts;

/// <summary>نوع قرارداد کار (hr.Employees.ContractType).</summary>
public enum ContractType : byte
{
    Permanent = 1,
    Temporary = 2,
    PartTime = 3,
    Project = 4
}

/// <summary>وضعیت بیمه کارمند (hr.Employees.InsuranceStatus).</summary>
public enum InsuranceStatus : byte
{
    Insured = 1,
    /// <summary>بازنشسته شاغل: سهم بیمه متفاوت (قابل تنظیم در اقلام).</summary>
    RetiredWorking = 2,
    NotInsured = 3
}

/// <summary>وضعیت پرونده پرسنلی (hr.Employees.Status).</summary>
public enum EmployeeStatus : byte
{
    Active = 1,
    Suspended = 2,
    Left = 3
}

/// <summary>
/// نگاشت فقط‌خواندنی پرونده پرسنلی با شخص و کارگاه برای ماژول حقوق (ToSqlQuery روی hr.Employees).
/// </summary>
public sealed class EmployeeRef
{
    public const string Sql = """
        SELECT e.Id, e.PersonId, e.PersonnelNo, p.DisplayName, p.NationalId, e.WorkshopId, w.Title AS WorkshopTitle, e.CostCenterId,
               e.ChildrenCount, CAST(CASE WHEN p.MaritalStatus = 2 THEN 1 ELSE 0 END AS BIT) AS IsMarried,
               e.InsuranceStatus, e.[Status], e.HireDate, e.LeaveDate,
               w.EmployeeRate, w.EmployerRate, w.UnemploymentRate, e.InsuranceNo, j.Title AS JobTitle, j.InsuranceJobCode,
               e.Education, e.JobCategory, e.EmploymentType, e.TaxExemption, e.ServiceLocation, e.Iban
        FROM hr.Employees e JOIN base.Persons p ON p.Id = e.PersonId JOIN hr.Workshops w ON w.Id = e.WorkshopId
        LEFT JOIN hr.Jobs j ON j.Id = e.JobId
        """;

    public int Id { get; init; }
    public int PersonId { get; init; }
    public string PersonnelNo { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? NationalId { get; init; }
    public int WorkshopId { get; init; }
    public string WorkshopTitle { get; init; } = "";
    public int CostCenterId { get; init; }
    public byte ChildrenCount { get; init; }
    public bool IsMarried { get; init; }
    public InsuranceStatus InsuranceStatus { get; init; }
    public EmployeeStatus Status { get; init; }
    public DateOnly HireDate { get; init; }
    public DateOnly? LeaveDate { get; init; }
    public decimal EmployeeRate { get; init; }
    public decimal EmployerRate { get; init; }
    public decimal UnemploymentRate { get; init; }
    public string? InsuranceNo { get; init; }
    public string? JobTitle { get; init; }
    public string? InsuranceJobCode { get; init; }
    public byte? Education { get; init; }
    public byte? JobCategory { get; init; }
    public byte EmploymentType { get; init; }
    public byte TaxExemption { get; init; }
    public byte ServiceLocation { get; init; }
    public string? Iban { get; init; }
}

/// <summary>کارگاه بیمه برای سربرگ لیست (ToSqlQuery روی hr.Workshops).</summary>
public sealed class WorkshopRef
{
    public const string Sql = "SELECT Id, Code, Title, EmployerName, Branch, ContractRow, Address FROM hr.Workshops";

    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string? EmployerName { get; init; }
    public string? Branch { get; init; }
    public string ContractRow { get; init; } = "000";
    public string? Address { get; init; }
}

/// <summary>مشخصات هویتی شخص برای لیست بیمه و فایل مالیات (ToSqlQuery روی base.Persons و جدول‌های پایه).</summary>
public sealed class PersonInfoRef
{
    public const string Sql = """
        SELECT p.Id, p.FirstName, p.LastName, p.FatherName, p.Gender, p.BirthDate, p.IdNumber, p.IsForeign,
               bc.Title AS BirthCity, ic.Title AS IssueCity, n.Title AS Nationality
        FROM base.Persons p
        LEFT JOIN base.LookupItems bc ON bc.Id = p.BirthCityId
        LEFT JOIN base.LookupItems ic ON ic.Id = p.IdIssueCityId
        LEFT JOIN base.LookupItems n ON n.Id = p.NationalityId
        """;

    public int Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? FatherName { get; init; }
    /// <summary>۱ مرد، ۲ زن</summary>
    public byte? Gender { get; init; }
    public DateOnly? BirthDate { get; init; }
    public string? IdNumber { get; init; }
    public bool IsForeign { get; init; }
    public string? BirthCity { get; init; }
    public string? IssueCity { get; init; }
    public string? Nationality { get; init; }
}
