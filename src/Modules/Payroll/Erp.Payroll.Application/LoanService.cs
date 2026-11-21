using Erp.Payroll.Domain;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>
/// وام و مساعده کارکنان: مبلغ و برنامه کسر. پرداخت خود وام از خزانه‌داری (نوع پرداخت با معین وام/مساعده کارکنان) ثبت می‌شود.
/// اقساط در محاسبه حقوق هر ماه کسر می‌شود؛ مانده = مبلغ − اقساط کسرشده.
/// </summary>
public class LoanService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock)
{
    public async Task<List<LoanListItemDto>> ListAsync(int? employeeId, bool openOnly, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanView);
        var query = db.Loans.AsNoTracking();
        if (employeeId is int e) query = query.Where(l => l.EmployeeId == e);
        var loans = await query.OrderByDescending(l => l.LoanDate).ThenByDescending(l => l.DocNo).ToListAsync(ct);
        var ids = loans.Select(l => l.Id).ToList();
        var deductions = await DeductionsAsync(ids, ct);
        var empIds = loans.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Where(x => empIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return loans.Select(l =>
            {
                var mine = deductions.Where(d => d.LoanId == l.Id).ToList();
                var deducted = mine.Sum(d => d.Amount);
                return new LoanListItemDto(l.Id, l.Kind, l.DocNo, l.EmployeeId, employees[l.EmployeeId].PersonnelNo, employees[l.EmployeeId].DisplayName,
                    PersianDate.ToPersian(l.LoanDate), l.Amount, l.Installments, l.InstallmentAmount, $"{l.StartYear}/{l.StartMonth:00}",
                    deducted, mine.Where(d => d.Status == PayRunStatus.Final).Sum(d => d.Amount), l.Amount - deducted, l.Description);
            })
            .Where(x => !openOnly || x.Remaining > 0).ToList();
    }

    /// <summary>کارکنان برای انتخاب در فرم وام (شاغل، به‌علاوه includeId).</summary>
    public async Task<List<EmployeeOptionDto>> EmployeesAsync(int? includeId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanView);
        return await db.Employees.Where(e => e.Status != EmployeeStatus.Left || e.Id == includeId).OrderBy(e => e.PersonnelNo)
            .Select(e => new EmployeeOptionDto(e.Id, e.PersonnelNo, e.DisplayName, e.Status)).ToListAsync(ct);
    }

    public async Task<LoanDto> GetAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanView);
        var l = await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("وام پیدا نشد.");
        var employee = await db.Employees.FirstAsync(x => x.Id == l.EmployeeId, ct);
        var deductions = await DeductionsAsync([l.Id], ct);
        return new LoanDto(l.Id, l.Kind, l.DocNo, l.EmployeeId, $"{employee.PersonnelNo} {employee.DisplayName}", PersianDate.ToPersian(l.LoanDate),
            l.Amount, l.Installments, l.InstallmentAmount, l.StartYear, l.StartMonth, l.Description, deductions.Sum(d => d.Amount),
            deductions.Select(d => new LoanDeductionDto(d.RunId, d.Run, d.Status, d.Amount)).ToList());
    }

    public async Task<int> CreateAsync(SaveLoanRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanCreate);
        var no = (await db.Loans.Where(l => l.Kind == r.Kind).MaxAsync(l => (int?)l.DocNo, ct) ?? 0) + 1;
        var loan = new Loan(r.EmployeeId, r.Kind, no, user.UserId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(loan, r, 0, ct);
        db.Add(loan);
        await db.SaveChangesAsync(ct);
        return loan.Id;
    }

    public async Task UpdateAsync(int id, SaveLoanRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanEdit);
        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("وام پیدا نشد.");
        var deducted = (await DeductionsAsync([id], ct)).Sum(d => d.Amount);
        if (loan.EmployeeId != r.EmployeeId && deducted > 0) throw new DomainException("از این وام قسط کسر شده؛ کارمندش عوض نمی‌شود.");
        if (loan.Kind != r.Kind) throw new DomainException("نوع وام عوض نمی‌شود.");
        loan.ChangeEmployee(r.EmployeeId, deducted);
        await ApplyAsync(loan, r, deducted, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.LoanDelete);
        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("وام پیدا نشد.");
        if ((await DeductionsAsync([id], ct)).Count > 0)
            throw new DomainException("از این وام در حقوق قسط کسر شده و حذف نمی‌شود؛ اول آن اجراها را دوباره محاسبه کنید.");
        db.Remove(loan);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Loan loan, SaveLoanRequest r, decimal deducted, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == r.EmployeeId, ct) ?? throw new DomainException("کارمند انتخاب نشده یا پیدا نشد.");
        if (employee.Status == EmployeeStatus.Left && loan.Id == 0) throw new DomainException($"{employee.DisplayName} ترک کار کرده است.");
        loan.Update(PersianDate.Parse(r.LoanDate), r.Amount, r.Installments, r.InstallmentAmount, r.StartYear, r.StartMonth, r.Description, deducted);
    }

    private sealed record Deduction(int LoanId, int RunId, string Run, PayRunStatus Status, decimal Amount);

    private async Task<List<Deduction>> DeductionsAsync(List<int> loanIds, CancellationToken ct) =>
        (await db.Runs.AsNoTracking()
            .SelectMany(r => r.LoanDeductions.Where(d => loanIds.Contains(d.LoanId)),
                (r, d) => new { d.LoanId, RunId = r.Id, r.Year, r.Month, r.Status, d.Amount })
            .ToListAsync(ct))
        .Select(x => new Deduction(x.LoanId, x.RunId, $"{x.Year}/{x.Month:00}", x.Status, x.Amount)).ToList();
}
