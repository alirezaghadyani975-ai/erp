using Erp.Personnel.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.SharedKernel.Validation;

namespace Erp.Personnel.Domain;

/// <summary>شغل (hr.Jobs) با کد شغل لیست بیمه تأمین اجتماعی.</summary>
public class Job : AggregateRoot<int>
{
    private Job() { } // EF

    public Job(string title, string? insuranceJobCode) => Update(title, insuranceJobCode);

    public string Title { get; private set; } = null!;
    public string? InsuranceJobCode { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string title, string? insuranceJobCode)
    {
        Title = Texts.Required(title, "عنوان شغل", 100);
        var code = IranianIds.Digits(insuranceJobCode);
        if (code.Length > 6) throw new DomainException("کد شغل بیمه حداکثر ۶ رقم است.");
        InsuranceJobCode = code.Length == 0 ? null : code;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>کارگاه تأمین اجتماعی (hr.Workshops) با نرخ‌های بیمه کارگاه.</summary>
public class Workshop : AggregateRoot<int>
{
    private Workshop() { } // EF

    public Workshop(string code, string title) => Update(code, title, null, null, "000", null, 7, 20, 3);

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? EmployerName { get; private set; }
    public string? Branch { get; private set; }
    public string ContractRow { get; private set; } = "000";
    public string? Address { get; private set; }
    public decimal EmployeeRate { get; private set; }
    public decimal EmployerRate { get; private set; }
    public decimal UnemploymentRate { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string code, string title, string? employerName, string? branch, string? contractRow, string? address,
        decimal employeeRate, decimal employerRate, decimal unemploymentRate)
    {
        var c = IranianIds.Digits(code);
        if (c.Length is 0 or > 10) throw new DomainException("کد کارگاه بیمه الزامی و حداکثر ۱۰ رقم است.");
        var row = IranianIds.Digits(contractRow);
        if (row.Length > 3) throw new DomainException("ردیف پیمان حداکثر ۳ رقم است.");
        foreach (var (rate, label) in new[] { (employeeRate, "نرخ سهم کارگر"), (employerRate, "نرخ سهم کارفرما"), (unemploymentRate, "نرخ بیمه بیکاری") })
            if (rate is < 0 or > 100) throw new DomainException($"{label} باید بین ۰ و ۱۰۰ باشد.");

        Code = c;
        Title = Texts.Required(title, "عنوان کارگاه", 100);
        EmployerName = Texts.Optional(employerName, "نام کارفرما", 100);
        Branch = Texts.Optional(branch, "شعبه", 100);
        ContractRow = row.Length == 0 ? "000" : row.PadLeft(3, '0');
        Address = Texts.Optional(address, "نشانی", 300);
        (EmployeeRate, EmployerRate, UnemploymentRate) = (employeeRate, employerRate, unemploymentRate);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// پرونده پرسنلی (hr.Employees): شخص حقیقی با نقش کارمند، کارگاه بیمه، شغل، قرارداد، مرکز هزینه پیش‌فرض کارکرد ماه،
/// وضعیت بیمه، فرزندان، شبا و ترک کار. وضعیت تأهل از خود شخص خوانده می‌شود.
/// </summary>
public class Employee : AggregateRoot<int>
{
    private Employee() { } // EF

    public Employee(int personId, DateTime nowUtc)
    {
        if (personId <= 0) throw new DomainException("شخص الزامی است.");
        PersonId = personId;
        CreatedAt = nowUtc;
    }

    public int PersonId { get; private set; }
    public string PersonnelNo { get; private set; } = null!;
    public int WorkshopId { get; private set; }
    public int? JobId { get; private set; }
    public ContractType ContractType { get; private set; } = ContractType.Temporary;
    public DateOnly HireDate { get; private set; }
    public DateOnly? LeaveDate { get; private set; }
    public string? LeaveReason { get; private set; }
    public string? InsuranceNo { get; private set; }
    public InsuranceStatus InsuranceStatus { get; private set; } = InsuranceStatus.Insured;
    public byte ChildrenCount { get; private set; }
    public int CostCenterId { get; private set; }
    public string? Iban { get; private set; }
    public decimal? TaxExemptPercent { get; private set; }
    public EmployeeStatus Status { get; private set; } = EmployeeStatus.Active;
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // اقلام فایل مالیات حقوق (جدول‌های راهنمای سازمان امور مالیاتی)
    public byte? Education { get; private set; }
    public byte? JobCategory { get; private set; }
    public byte EmploymentType { get; private set; } = 1;
    public byte TaxExemption { get; private set; } = 1;
    public byte ServiceLocation { get; private set; } = 1;

    /// <summary>کدهای فایل مالیات: مدرک (۱ تا ۷)، رسته (۱ تا ۱۹)، نوع استخدام (۱ تا ۱۲)، معافیت (۱ تا ۱۱)، محل خدمت (۱ تا ۴).</summary>
    public void SetTaxInfo(byte? education, byte? jobCategory, byte employmentType, byte taxExemption, byte serviceLocation)
    {
        if (education is not null and not (>= 1 and <= 7)) throw new DomainException("مدرک تحصیلی معتبر نیست.");
        if (jobCategory is not null and not (>= 1 and <= 19)) throw new DomainException("رسته شغلی معتبر نیست.");
        if (employmentType is < 1 or > 12) throw new DomainException("نوع استخدام معتبر نیست.");
        if (taxExemption is < 1 or > 11) throw new DomainException("نوع معافیت مالیاتی معتبر نیست.");
        if (serviceLocation is < 1 or > 4) throw new DomainException("وضعیت محل خدمت معتبر نیست.");
        (Education, JobCategory, EmploymentType, TaxExemption, ServiceLocation) = (education, jobCategory, employmentType, taxExemption, serviceLocation);
    }

    public void Update(string personnelNo, int workshopId, int? jobId, ContractType contractType, DateOnly hireDate, string? insuranceNo,
        InsuranceStatus insuranceStatus, byte childrenCount, int costCenterId, string? iban, decimal? taxExemptPercent, string? notes)
    {
        if (workshopId <= 0) throw new DomainException("کارگاه الزامی است.");
        if (costCenterId <= 0) throw new DomainException("مرکز هزینه پیش‌فرض الزامی است.");
        if (!Enum.IsDefined(contractType)) throw new DomainException("نوع قرارداد معتبر نیست.");
        if (!Enum.IsDefined(insuranceStatus)) throw new DomainException("وضعیت بیمه معتبر نیست.");
        if (childrenCount > 30) throw new DomainException("تعداد فرزندان معتبر نیست.");
        if (taxExemptPercent is < 0 or > 100) throw new DomainException("درصد معافیت مالیاتی باید بین ۰ و ۱۰۰ باشد.");
        if (LeaveDate is { } left && hireDate > left) throw new DomainException("تاریخ استخدام بعد از تاریخ ترک کار است.");

        var insurance = IranianIds.Normalize(IdKind.InsuranceNo, insuranceNo);
        if (IranianIds.Error(IdKind.InsuranceNo, insurance) is { } insError) throw new DomainException(insError);
        if (insuranceStatus != InsuranceStatus.NotInsured && insurance is null)
            throw new DomainException("شماره بیمه برای کارمند مشمول بیمه الزامی است.");
        var sheba = IranianIds.Normalize(IdKind.Iban, iban);
        if (IranianIds.Error(IdKind.Iban, sheba) is { } ibanError) throw new DomainException(ibanError);

        PersonnelNo = Texts.Required(personnelNo, "شماره پرسنلی", 20);
        WorkshopId = workshopId;
        JobId = jobId is > 0 ? jobId : null;
        ContractType = contractType;
        HireDate = hireDate;
        InsuranceNo = insurance;
        InsuranceStatus = insuranceStatus;
        ChildrenCount = childrenCount;
        CostCenterId = costCenterId;
        Iban = sheba;
        TaxExemptPercent = taxExemptPercent is 0 ? null : taxExemptPercent;
        Notes = Texts.Optional(notes, "توضیحات", 500);
    }

    public void Suspend(bool suspended)
    {
        if (Status == EmployeeStatus.Left) throw new DomainException("کارمندی که ترک کار کرده تعلیق نمی‌شود؛ اول بازگشت به کار ثبت کنید.");
        Status = suspended ? EmployeeStatus.Suspended : EmployeeStatus.Active;
    }

    /// <summary>ترک کار؛ تسویه حساب در ماژول حقوق انجام می‌شود.</summary>
    public void Leave(DateOnly date, string? reason)
    {
        if (date < HireDate) throw new DomainException("تاریخ ترک کار پیش از تاریخ استخدام است.");
        LeaveDate = date;
        LeaveReason = Texts.Optional(reason, "علت ترک کار", 200);
        Status = EmployeeStatus.Left;
    }

    /// <summary>بازگشت به کار (لغو ترک کار).</summary>
    public void Return()
    {
        LeaveDate = null;
        LeaveReason = null;
        Status = EmployeeStatus.Active;
    }
}

internal static class Texts
{
    public static string Required(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value) ?? throw new DomainException($"{label} الزامی است.");
        return v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }

    public static string? Optional(string? value, string label, int max)
    {
        var v = PersianText.NormalizeOrNull(value);
        return v is null || v.Length <= max ? v : throw new DomainException($"{label} حداکثر {max} نویسه است.");
    }
}
