using Erp.SharedKernel.Domain;

namespace Erp.Payroll.Domain;

/// <summary>وضعیت اجرای حقوق (pay.Runs.Status).</summary>
public enum PayRunStatus : byte
{
    /// <summary>کارکرد در حال ورود یا بعد از محاسبه تغییر کرده؛ نیازمند محاسبه.</summary>
    Draft = 1,
    Calculated = 2,
    Final = 3
}

/// <summary>
/// اجرای حقوق یک کارگاه در یک ماه شمسی (pay.Runs) با کارکرد کارکنان و نتیجه محاسبه.
/// هر تغییر کارکرد یا ورودی، اجرای محاسبه‌شده را «نیازمند محاسبه» می‌کند؛ اجرای قطعی تغییر نمی‌کند.
/// </summary>
public class PayRun : AggregateRoot<int>
{
    private readonly List<Attendance> _attendances = [];
    private readonly List<RunLine> _lines = [];
    private readonly List<RunLoanDeduction> _loanDeductions = [];

    private PayRun() { } // EF

    public PayRun(short year, byte month, int workshopId, byte monthDays, int userId, DateTime nowUtc)
    {
        if (year is < 1300 or > 1500) throw new DomainException("سال شمسی معتبر نیست.");
        if (month is < 1 or > 12) throw new DomainException("ماه باید بین ۱ و ۱۲ باشد.");
        if (workshopId <= 0) throw new DomainException("کارگاه الزامی است.");
        (Year, Month, WorkshopId, MonthDays, CreatedBy, CreatedAt) = (year, month, workshopId, monthDays, userId, nowUtc);
        Status = PayRunStatus.Draft;
    }

    public short Year { get; private set; }
    public byte Month { get; private set; }
    public int WorkshopId { get; private set; }
    public PayRunStatus Status { get; private set; }
    public byte MonthDays { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? CalculatedBy { get; private set; }
    public DateTime? CalculatedAt { get; private set; }
    public IReadOnlyList<Attendance> Attendances => _attendances.AsReadOnly();
    public IReadOnlyList<RunLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<RunLoanDeduction> LoanDeductions => _loanDeductions.AsReadOnly();

    public string Title => $"{Year}/{Month:00}";

    public void EnsureEditable()
    {
        if (Status == PayRunStatus.Final) throw new DomainException($"اجرای حقوق {Title} قطعی است و تغییر نمی‌کند.");
    }

    public void SetDescription(string? description)
    {
        EnsureEditable();
        var d = Erp.SharedKernel.Text.PersianText.NormalizeOrNull(description);
        if (d?.Length > 300) throw new DomainException("شرح حداکثر ۳۰۰ نویسه است.");
        Description = d;
    }

    public void AddAttendance(Attendance a)
    {
        EnsureEditable();
        if (_attendances.Any(x => x.EmployeeId == a.EmployeeId)) throw new DomainException("کارکرد این کارمند قبلاً در اجرا آمده است.");
        _attendances.Add(a);
        Changed();
    }

    public void RemoveAttendance(Attendance a)
    {
        EnsureEditable();
        _attendances.Remove(a);
        _lines.RemoveAll(l => l.EmployeeId == a.EmployeeId);
        _loanDeductions.Clear(); // با محاسبه دوباره ساخته می‌شود
        Changed();
    }

    /// <summary>کارکرد یا ورودی تغییر کرد: نتیجه قبلی دیگر معتبر نیست.</summary>
    public void Changed()
    {
        EnsureEditable();
        if (Status == PayRunStatus.Calculated) Status = PayRunStatus.Draft;
    }

    /// <summary>قطعی: کارکرد و نتیجه قفل می‌شود و سند حسابداری صادر می‌شود (در سرویس).</summary>
    public void MakeFinal()
    {
        if (Status == PayRunStatus.Final) throw new DomainException($"حقوق {Title} قبلاً قطعی شده است.");
        if (Status != PayRunStatus.Calculated) throw new DomainException($"حقوق {Title} نیازمند محاسبه است؛ اول «محاسبه» بزنید.");
        Status = PayRunStatus.Final;
    }

    /// <summary>برگشت از قطعی (سند حسابداری در سرویس حذف می‌شود؛ سند دائم مانع است).</summary>
    public void Reopen()
    {
        if (Status != PayRunStatus.Final) throw new DomainException($"حقوق {Title} قطعی نیست.");
        Status = PayRunStatus.Calculated;
    }

    public void SetResults(IEnumerable<RunLine> lines, IEnumerable<RunLoanDeduction> loanDeductions, int userId, DateTime nowUtc)
    {
        EnsureEditable();
        _lines.Clear();
        _lines.AddRange(lines);
        _loanDeductions.Clear();
        _loanDeductions.AddRange(loanDeductions);
        Status = PayRunStatus.Calculated;
        (CalculatedBy, CalculatedAt) = (userId, nowUtc);
    }
}

/// <summary>کارکرد ماه یک کارمند (pay.Attendance) با تقسیم مرکز هزینه و ورودی‌های ماه.</summary>
public class Attendance
{
    private readonly List<AttendanceCenter> _centers = [];
    private readonly List<AttendanceInput> _inputs = [];

    private Attendance() { } // EF

    public Attendance(int employeeId, int costCenterId)
    {
        EmployeeId = employeeId;
        _centers.Add(new AttendanceCenter(costCenterId, 100));
    }

    public int Id { get; private set; }
    public int RunId { get; private set; }
    public int EmployeeId { get; private set; }
    public decimal WorkDays { get; private set; }
    public byte InsuranceDays { get; private set; }
    public decimal OtHours { get; private set; }
    public decimal NightHours { get; private set; }
    public decimal HolidayHours { get; private set; }
    public decimal AbsentDays { get; private set; }
    public IReadOnlyList<AttendanceCenter> Centers => _centers.AsReadOnly();
    public IReadOnlyList<AttendanceInput> Inputs => _inputs.AsReadOnly();

    /// <param name="monthDays">سقف روز کارکرد (MONTH_DAYS)</param>
    /// <param name="calendarDays">روزهای واقعی ماه؛ سقف روز بیمه</param>
    public void Update(decimal workDays, int insuranceDays, decimal otHours, decimal nightHours, decimal holidayHours, decimal absentDays,
        int monthDays, int calendarDays)
    {
        if (workDays < 0 || workDays > monthDays) throw new DomainException($"روز کارکرد باید بین ۰ و {monthDays} باشد.");
        if (insuranceDays < 0 || insuranceDays > calendarDays) throw new DomainException($"روز بیمه باید بین ۰ و {calendarDays} باشد.");
        if (absentDays < 0 || absentDays > calendarDays) throw new DomainException("روز غیبت معتبر نیست.");
        if (otHours < 0 || nightHours < 0 || holidayHours < 0) throw new DomainException("ساعت‌ها منفی نمی‌شوند.");
        if (otHours > 400 || nightHours > 400 || holidayHours > 400) throw new DomainException("ساعت‌های ماه بیش از حد است.");
        WorkDays = Math.Round(workDays, 2);
        InsuranceDays = (byte)insuranceDays;
        OtHours = Math.Round(otHours, 2);
        NightHours = Math.Round(nightHours, 2);
        HolidayHours = Math.Round(holidayHours, 2);
        AbsentDays = Math.Round(absentDays, 2);
    }

    /// <summary>تقسیم هزینه بین مراکز به درصد؛ جمع باید ۱۰۰ باشد.</summary>
    public void SetCenters(IReadOnlyCollection<(int CenterId, decimal Percent)> centers)
    {
        if (centers.Count == 0) throw new DomainException("حداقل یک مرکز هزینه لازم است.");
        if (centers.Select(c => c.CenterId).Distinct().Count() != centers.Count) throw new DomainException("یک مرکز هزینه دو بار آمده است.");
        if (centers.Any(c => c.Percent is <= 0 or > 100)) throw new DomainException("درصد هر مرکز باید بیشتر از صفر و حداکثر ۱۰۰ باشد.");
        if (centers.Sum(c => Math.Round(c.Percent, 2)) != 100) throw new DomainException("جمع درصد مراکز هزینه باید ۱۰۰ باشد.");
        _centers.Clear();
        _centers.AddRange(centers.Select(c => new AttendanceCenter(c.CenterId, Math.Round(c.Percent, 2))));
    }

    /// <summary>مبلغ قلم ورود ماهانه / وام و مساعده / مالیات؛ صفر = حذف.</summary>
    public void SetInput(PayItem item, decimal amount)
    {
        if (item.Source is not (PayItemSource.MonthlyInput or PayItemSource.Loan or PayItemSource.TaxOffice))
            throw new DomainException($"قلم «{item.Title}» از ورودی ماه پر نمی‌شود.");
        if (amount < 0) throw new DomainException($"مبلغ «{item.Title}» منفی نمی‌شود.");
        var value = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        var existing = _inputs.FirstOrDefault(i => i.PayItemId == item.Id);
        if (existing is not null) _inputs.Remove(existing);
        if (value != 0) _inputs.Add(new AttendanceInput(item.Id, value));
    }

    public decimal InputOf(int payItemId) => _inputs.FirstOrDefault(i => i.PayItemId == payItemId)?.Amount ?? 0;
}

public class AttendanceCenter
{
    private AttendanceCenter() { } // EF

    internal AttendanceCenter(int costCenterId, decimal percent) => (CostCenterId, Percent) = (costCenterId, percent);

    public int AttendanceId { get; private set; }
    public int CostCenterId { get; private set; }
    public decimal Percent { get; private set; }
}

public class AttendanceInput
{
    private AttendanceInput() { } // EF

    internal AttendanceInput(int payItemId, decimal amount) => (PayItemId, Amount) = (payItemId, amount);

    public int AttendanceId { get; private set; }
    public int PayItemId { get; private set; }
    public decimal Amount { get; private set; }
}

/// <summary>نتیجه محاسبه: مبلغ یک قلم برای یک کارمند (pay.RunLines).</summary>
public class RunLine
{
    private RunLine() { } // EF

    public RunLine(int employeeId, int payItemId, decimal amount) => (EmployeeId, PayItemId, Amount) = (employeeId, payItemId, amount);

    public int RunId { get; private set; }
    public int EmployeeId { get; private set; }
    public int PayItemId { get; private set; }
    public decimal Amount { get; private set; }
}
