using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Payroll.Domain;

/// <summary>نوع وام کارکنان (pay.Loans.Kind).</summary>
public enum LoanKind : byte
{
    /// <summary>وام اقساطی؛ قلم کسر LOAN، معین 2522.</summary>
    Loan = 1,
    /// <summary>مساعده؛ یک‌جا در ماه شروع کسر می‌شود؛ قلم ADVANCE، معین 2523.</summary>
    Advance = 2
}

/// <summary>
/// وام یا مساعده کارمند (pay.Loans): مبلغ و برنامه کسر از حقوق. پرداخت خود وام از خزانه‌داری ثبت می‌شود.
/// هر اجرای حقوق از ماه شروع، یک قسط (حداکثر تا مانده) کسر می‌کند.
/// </summary>
public class Loan : AggregateRoot<int>
{
    public const string LoanItemCode = "LOAN";
    public const string AdvanceItemCode = "ADVANCE";

    private Loan() { } // EF

    public Loan(int employeeId, LoanKind kind, int docNo, int userId, DateTime nowUtc)
    {
        if (employeeId <= 0) throw new DomainException("کارمند الزامی است.");
        if (!Enum.IsDefined(kind)) throw new DomainException("نوع وام معتبر نیست.");
        (EmployeeId, Kind, DocNo, CreatedBy, CreatedAt) = (employeeId, kind, docNo, userId, nowUtc);
    }

    public int EmployeeId { get; private set; }
    public LoanKind Kind { get; private set; }
    public int DocNo { get; private set; }
    public DateOnly LoanDate { get; private set; }
    public decimal Amount { get; private set; }
    public short Installments { get; private set; }
    public decimal InstallmentAmount { get; private set; }
    public short StartYear { get; private set; }
    public byte StartMonth { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string ItemCode => Kind == LoanKind.Loan ? LoanItemCode : AdvanceItemCode;

    /// <param name="installmentAmount">خالی = مبلغ ÷ تعداد اقساط (گرد به بالا تا آخرین قسط مانده را بگیرد).</param>
    /// <param name="deducted">جمع اقساط کسرشده تاکنون (مبلغ از آن کمتر نمی‌شود).</param>
    public void Update(DateOnly date, decimal amount, int installments, decimal? installmentAmount, short startYear, byte startMonth,
        string? description, decimal deducted)
    {
        if (amount <= 0) throw new DomainException("مبلغ وام باید بیشتر از صفر باشد.");
        amount = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (Kind == LoanKind.Advance) installments = 1;
        if (installments is < 1 or > 240) throw new DomainException("تعداد اقساط باید بین ۱ و ۲۴۰ باشد.");
        var installment = installmentAmount is > 0
            ? Math.Round(installmentAmount.Value, 0, MidpointRounding.AwayFromZero)
            : Math.Ceiling(amount / installments);
        if (Kind == LoanKind.Advance) installment = amount;
        if (installment > amount) throw new DomainException("مبلغ قسط از مبلغ وام بیشتر است.");
        if (startMonth is < 1 or > 12 || startYear is < 1300 or > 1500) throw new DomainException("ماه شروع کسر معتبر نیست.");
        if (amount < deducted) throw new DomainException($"تاکنون {deducted:#,0} ریال از این وام کسر شده؛ مبلغ از آن کمتر نمی‌شود.");
        var desc = PersianText.NormalizeOrNull(description);
        if (desc?.Length > 300) throw new DomainException("شرح حداکثر ۳۰۰ نویسه است.");

        (LoanDate, Amount, Installments, InstallmentAmount, StartYear, StartMonth, Description) =
            (date, amount, (short)installments, installment, startYear, startMonth, desc);
    }

    /// <summary>اصلاح کارمند؛ فقط پیش از کسر اولین قسط.</summary>
    public void ChangeEmployee(int employeeId, decimal deducted)
    {
        if (employeeId == EmployeeId) return;
        if (deducted > 0) throw new DomainException("از این وام قسط کسر شده؛ کارمندش عوض نمی‌شود.");
        if (employeeId <= 0) throw new DomainException("کارمند الزامی است.");
        EmployeeId = employeeId;
    }

    /// <summary>آیا کسر از این ماه شروع شده است؟</summary>
    public bool StartedBy(short year, byte month) => StartYear < year || (StartYear == year && StartMonth <= month);

    /// <summary>قسط این ماه با توجه به مانده (کسر در اجراهای دیگر).</summary>
    public decimal InstallmentFor(short year, byte month, decimal deductedElsewhere) =>
        StartedBy(year, month) ? Math.Max(0, Math.Min(InstallmentAmount, Amount - deductedElsewhere)) : 0;
}

/// <summary>قسط کسرشده یک وام در یک اجرای حقوق (pay.RunLoanDeductions).</summary>
public class RunLoanDeduction
{
    private RunLoanDeduction() { } // EF

    public RunLoanDeduction(int loanId, decimal amount) => (LoanId, Amount) = (loanId, amount);

    public int RunId { get; private set; }
    public int LoanId { get; private set; }
    public decimal Amount { get; private set; }
}
