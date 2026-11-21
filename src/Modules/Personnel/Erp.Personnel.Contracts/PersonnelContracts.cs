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
        SELECT e.Id, e.PersonId, e.PersonnelNo, p.DisplayName, e.WorkshopId, w.Title AS WorkshopTitle, e.CostCenterId,
               e.ChildrenCount, CAST(CASE WHEN p.MaritalStatus = 2 THEN 1 ELSE 0 END AS BIT) AS IsMarried,
               e.InsuranceStatus, e.[Status], e.HireDate, e.LeaveDate,
               w.EmployeeRate, w.EmployerRate, w.UnemploymentRate
        FROM hr.Employees e JOIN base.Persons p ON p.Id = e.PersonId JOIN hr.Workshops w ON w.Id = e.WorkshopId
        """;

    public int Id { get; init; }
    public int PersonId { get; init; }
    public string PersonnelNo { get; init; } = "";
    public string DisplayName { get; init; } = "";
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
}
