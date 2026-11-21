using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;

namespace Erp.Payroll.Domain;

/// <summary>
/// پرداخت گروهی خالص حقوق یک اجرای قطعی از یک حساب بانکی (pay.SalaryPayments) با سند «پرداخت حقوق».
/// هر اجرا می‌تواند چند پرداخت داشته باشد؛ جمع پرداخت هر کارمند از خالص حقوقش بیشتر نمی‌شود.
/// </summary>
public class SalaryPayment : AggregateRoot<int>
{
    public const string DocType = "pay_payment";

    private readonly List<SalaryPaymentLine> _lines = [];

    private SalaryPayment() { } // EF

    public SalaryPayment(int runId, int docNo, int userId, DateTime nowUtc) => (RunId, DocNo, CreatedBy, CreatedAt) = (runId, docNo, userId, nowUtc);

    public int RunId { get; private set; }
    public int DocNo { get; private set; }
    public DateOnly PayDate { get; private set; }
    public int BankAccountId { get; private set; }
    public string? Description { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<SalaryPaymentLine> Lines => _lines.AsReadOnly();

    public decimal Total => _lines.Sum(l => l.Amount);

    /// <param name="lines">کارمند، مبلغ، شبا؛ مبلغ صفر نادیده گرفته می‌شود.</param>
    public void Update(DateOnly date, int bankAccountId, string? description, IEnumerable<(int EmployeeId, decimal Amount, string? Iban)> lines)
    {
        if (bankAccountId <= 0) throw new DomainException("حساب بانکی الزامی است.");
        var desc = PersianText.NormalizeOrNull(description);
        if (desc?.Length > 300) throw new DomainException("شرح حداکثر ۳۰۰ نویسه است.");
        var rows = new List<SalaryPaymentLine>();
        foreach (var (employeeId, amount, iban) in lines)
        {
            if (amount < 0) throw new DomainException("مبلغ پرداخت منفی نمی‌شود.");
            if (amount == 0) continue;
            if (rows.Any(r => r.EmployeeId == employeeId)) throw new DomainException("یک کارمند دو بار در پرداخت آمده است.");
            rows.Add(new SalaryPaymentLine(employeeId, Math.Round(amount, 0, MidpointRounding.AwayFromZero), iban));
        }
        if (rows.Count == 0) throw new DomainException("پرداخت حداقل یک کارمند با مبلغ دارد.");
        (PayDate, BankAccountId, Description) = (date, bankAccountId, desc);
        _lines.Clear();
        _lines.AddRange(rows);
    }
}

public class SalaryPaymentLine
{
    private SalaryPaymentLine() { } // EF

    internal SalaryPaymentLine(int employeeId, decimal amount, string? iban) => (EmployeeId, Amount, Iban) = (employeeId, amount, iban);

    public int PaymentId { get; private set; }
    public int EmployeeId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Iban { get; private set; }
}
