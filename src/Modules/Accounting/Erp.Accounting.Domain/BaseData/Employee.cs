using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Validation;

namespace Erp.Accounting.Domain.BaseData;

/// <summary>اطلاعات پرسنلی قابل ویرایش (حقوق و دستمزد، بیمه، مالیات حقوق).</summary>
public sealed class EmployeeData
{
    public string? EmployeeNo { get; init; }
    public DateOnly? HireDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public ContractType? ContractType { get; init; }
    public string? JobTitle { get; init; }
    public InsuranceType? InsuranceType { get; init; }
    public string? InsuranceNo { get; init; }
    public DateOnly? InsuranceStartDate { get; init; }
    public string? InsuranceJobCode { get; init; }
    public string? WorkshopCode { get; init; }
    public EducationLevel? EducationLevel { get; init; }
    public string? EducationField { get; init; }
    public MilitaryStatus? MilitaryStatus { get; init; }
    public byte ChildrenCount { get; init; }
    public byte DependentsCount { get; init; }
    public TaxExemptionType? TaxExemptionType { get; init; }
}

/// <summary>
/// اطلاعات پرسنلی (hr.Employees) — یک‌به‌یک با شخص دارای نقش کارمند.
/// بعداً ماژول حقوق و دستمزد مالک این جدول می‌شود.
/// </summary>
public class Employee
{
    internal Employee() { }

    public int PersonId { get; private set; }
    public string? EmployeeNo { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public ContractType? ContractType { get; private set; }
    public string? JobTitle { get; private set; }
    public InsuranceType? InsuranceType { get; private set; }
    public string? InsuranceNo { get; private set; }
    public DateOnly? InsuranceStartDate { get; private set; }
    public string? InsuranceJobCode { get; private set; }
    public string? WorkshopCode { get; private set; }
    public EducationLevel? EducationLevel { get; private set; }
    public string? EducationField { get; private set; }
    public MilitaryStatus? MilitaryStatus { get; private set; }
    public byte ChildrenCount { get; private set; }
    public byte DependentsCount { get; private set; }
    public TaxExemptionType? TaxExemptionType { get; private set; }

    internal void Apply(EmployeeData d, DateOnly today)
    {
        if (d.EndDate is not null && d.HireDate is not null && d.EndDate < d.HireDate)
            throw new DomainException("تاریخ ترک کار قبل از تاریخ استخدام است.");
        if (d.ChildrenCount > 30 || d.DependentsCount > 30)
            throw new DomainException("تعداد فرزندان یا افراد تحت تکفل معتبر نیست.");
        var insuranceNo = Person.Checked(IdKind.InsuranceNo, d.InsuranceNo);
        if (insuranceNo is not null && d.InsuranceType is Erp.Accounting.Domain.BaseData.InsuranceType.None)
            throw new DomainException("برای «بدون بیمه» شماره بیمه وارد نکنید.");

        EmployeeNo = Person.Text(d.EmployeeNo);
        HireDate = d.HireDate;
        EndDate = d.EndDate;
        ContractType = d.ContractType;
        JobTitle = Person.Text(d.JobTitle);
        InsuranceType = d.InsuranceType;
        InsuranceNo = insuranceNo;
        InsuranceStartDate = Person.PastDate(d.InsuranceStartDate, today.AddYears(1), "تاریخ شروع بیمه");
        InsuranceJobCode = Person.Digits(d.InsuranceJobCode, "کد شغل بیمه", 10);
        WorkshopCode = Person.Digits(d.WorkshopCode, "کد کارگاه", 10);
        EducationLevel = d.EducationLevel;
        EducationField = Person.Text(d.EducationField);
        MilitaryStatus = d.MilitaryStatus;
        ChildrenCount = d.ChildrenCount;
        DependentsCount = d.DependentsCount;
        TaxExemptionType = d.TaxExemptionType;
    }
}
