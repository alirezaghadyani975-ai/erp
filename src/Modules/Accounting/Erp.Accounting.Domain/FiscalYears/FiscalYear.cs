using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Domain.FiscalYears;

/// <summary>وضعیت سال یا دوره مالی.</summary>
public enum PeriodStatus : byte
{
    Open = 1,    // باز
    Locked = 2,  // قفل (موقت؛ قابل بازگشایی)
    Closed = 3   // بسته
}

/// <summary>
/// سال مالی (fin.FiscalYears) با دوره‌های ماهانه (fin.FiscalPeriods).
/// ساخت سال جدید با روال fin.usp_CreateFiscalYear انجام می‌شود که دوره‌ها را از تقویم شمسی می‌سازد.
/// </summary>
public class FiscalYear : AggregateRoot<short>
{
    private readonly List<FiscalPeriod> _periods = [];

    private FiscalYear() { } // EF

    public string Title { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PeriodStatus Status { get; private set; }

    public IReadOnlyCollection<FiscalPeriod> Periods => _periods.AsReadOnly();

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public void EnsureCanPostOn(DateOnly date)
    {
        if (Status != PeriodStatus.Open)
            throw new DomainException($"سال مالی {Title} باز نیست.");
        if (!Contains(date))
            throw new DomainException(
                $"تاریخ {PersianDate.ToPersian(date)} در سال مالی {Title} " +
                $"({PersianDate.ToPersian(StartDate)} تا {PersianDate.ToPersian(EndDate)}) نیست.");

        var period = _periods.FirstOrDefault(p => p.Contains(date));
        if (period is not null && period.Status != PeriodStatus.Open)
            throw new DomainException($"دوره «{period.Title}» باز نیست.");
    }

    public void SetPeriodStatus(byte periodNo, PeriodStatus status)
    {
        var period = _periods.FirstOrDefault(p => p.PeriodNo == periodNo)
                     ?? throw new DomainException($"دوره {periodNo} در سال {Title} وجود ندارد.");
        if (Status == PeriodStatus.Closed)
            throw new DomainException($"سال مالی {Title} بسته شده است.");
        period.Status = status;
    }

    public void Lock() => Status = Status == PeriodStatus.Closed
        ? throw new DomainException("سال بسته‌شده را نمی‌توان قفل کرد.")
        : PeriodStatus.Locked;

    public void Open() => Status = Status == PeriodStatus.Closed
        ? throw new DomainException("سال بسته‌شده را نمی‌توان باز کرد.")
        : PeriodStatus.Open;
}

public class FiscalPeriod : Entity<int>
{
    private FiscalPeriod() { } // EF

    public short FiscalYearId { get; private set; }
    public byte PeriodNo { get; private set; }
    public string Title { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PeriodStatus Status { get; internal set; }

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
