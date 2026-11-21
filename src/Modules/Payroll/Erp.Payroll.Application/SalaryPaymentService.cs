using Erp.Accounting.Contracts;
using Erp.Payroll.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Payroll.Application;

/// <summary>
/// پرداخت گروهی خالص حقوق یک اجرای قطعی: سند «پرداخت حقوق» (بدهکار 5125 هر کارمند، بستانکار بانک) و فهرست بانک (شبا و مبلغ).
/// </summary>
public class SalaryPaymentService(IPayrollDbContext db, IAccessControl access, ICurrentUser user, TimeProvider clock,
    IVoucherPosting posting, IUnitOfWork uow)
{
    public async Task<List<BankAccountOptionDto>> BankAccountsAsync(CancellationToken ct) =>
        await db.BankAccounts.Where(b => b.IsActive).OrderBy(b => b.Title).Select(b => new BankAccountOptionDto(b.Id, b.Title)).ToListAsync(ct);

    /// <summary>کارکنان اجرا با خالص، پرداخت‌شده و مانده (پیش‌فرض فرم پرداخت).</summary>
    public async Task<List<PaymentCandidateDto>> CandidatesAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.PaymentView);
        var run = await LoadRunAsync(runId, ct);
        var items = await db.PayItems.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Kind, ct);
        var paid = await PaidAsync(runId, null, ct);
        var ids = run.Attendances.Select(a => a.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        return run.Attendances.Select(a =>
            {
                var e = employees[a.EmployeeId];
                var net = run.Lines.Where(l => l.EmployeeId == a.EmployeeId)
                    .Sum(l => items[l.PayItemId] switch { PayItemKind.Earning => l.Amount, PayItemKind.Deduction => -l.Amount, _ => 0 });
                var already = paid.GetValueOrDefault(e.Id);
                return new PaymentCandidateDto(e.Id, e.PersonnelNo, e.DisplayName, e.Iban, net, already, Math.Max(0, net - already));
            })
            .OrderBy(c => c.PersonnelNo).ToList();
    }

    public async Task<List<SalaryPaymentListItemDto>> ListAsync(int runId, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.PaymentView);
        var payments = await db.Payments.AsNoTracking().Include(p => p.Lines).Where(p => p.RunId == runId).OrderBy(p => p.DocNo).ToListAsync(ct);
        var banks = await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var vouchers = await posting.FindAsync(PayRunService.SourceModule, SalaryPayment.DocType, payments.Select(p => (long)p.Id).ToList(), ct);
        return payments.Select(p => new SalaryPaymentListItemDto(p.Id, p.DocNo, PersianDate.ToPersian(p.PayDate), banks.GetValueOrDefault(p.BankAccountId, "?"),
            p.Lines.Count, p.Total, p.Description, vouchers.GetValueOrDefault(p.Id)?.VoucherId, vouchers.GetValueOrDefault(p.Id)?.TempNo)).ToList();
    }

    public async Task<int> CreateAsync(SaveSalaryPaymentRequest r, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.PaymentCreate);
        var run = await LoadRunAsync(r.RunId, ct);
        if (run.Status != PayRunStatus.Final) throw new DomainException($"حقوق {run.Title} هنوز قطعی نشده است؛ پرداخت بعد از قطعی کردن ممکن است.");
        var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == r.BankAccountId, ct) ?? throw new DomainException("حساب بانکی پیدا نشد.");
        if (!bank.IsActive) throw new DomainException($"حساب بانکی «{bank.Title}» غیرفعال است.");

        var candidates = (await CandidatesAsync(r.RunId, ct)).ToDictionary(c => c.EmployeeId);
        foreach (var l in r.Lines.Where(l => l.Amount > 0))
        {
            var c = candidates.GetValueOrDefault(l.EmployeeId) ?? throw new DomainException("کارمند در حقوق این ماه نیست.");
            if (l.Amount > c.Remaining)
                throw new DomainException($"پرداخت {c.PersonnelNo} {c.Name} ({l.Amount:#,0}) از مانده خالص حقوقش ({c.Remaining:#,0}) بیشتر است.");
        }

        var no = (await db.Payments.MaxAsync(p => (int?)p.DocNo, ct) ?? 0) + 1;
        var payment = new SalaryPayment(r.RunId, no, user.UserId, clock.GetUtcNow().UtcDateTime);
        payment.Update(PersianDate.Parse(r.PayDate), bank.Id, r.Description,
            r.Lines.Select(l => (l.EmployeeId, l.Amount, candidates.GetValueOrDefault(l.EmployeeId)?.Iban)));

        await using var tx = await uow.BeginAsync(ct);
        db.Add(payment);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(await PostingRequestAsync(payment, run, bank.Title, ct), ct);
        await tx.CommitAsync(ct);
        return payment.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.PaymentDelete);
        var payment = await db.Payments.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("پرداخت پیدا نشد.");
        await using var tx = await uow.BeginAsync(ct);
        await posting.UnpostAsync(PayRunService.SourceModule, SalaryPayment.DocType, payment.Id, ct);
        db.Remove(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>فهرست بانک یک پرداخت (برای فایل پرداخت گروهی شبا).</summary>
    public async Task<List<BankListRowDto>> BankListAsync(int id, CancellationToken ct)
    {
        access.Demand(PayrollPermissions.PaymentView);
        var payment = await db.Payments.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("پرداخت پیدا نشد.");
        var run = await db.Runs.AsNoTracking().FirstAsync(r => r.Id == payment.RunId, ct);
        var ids = payment.Lines.Select(l => l.EmployeeId).ToList();
        var employees = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var row = 0;
        return payment.Lines.OrderBy(l => employees[l.EmployeeId].PersonnelNo)
            .Select(l => new BankListRowDto(++row, employees[l.EmployeeId].PersonnelNo, employees[l.EmployeeId].DisplayName,
                employees[l.EmployeeId].NationalId, l.Iban, l.Amount, $"حقوق {run.Title}")).ToList();
    }

    private async Task<PostingRequest> PostingRequestAsync(SalaryPayment p, PayRun run, string bank, CancellationToken ct)
    {
        var ids = p.Lines.Select(l => l.EmployeeId).ToList();
        var persons = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.PersonId, ct);
        var title = $"پرداخت حقوق {run.Title} - {bank}";
        var lines = p.Lines.Select(l => new PostingLine("payable", l.Amount, [new PostingDetail(PostingDetailKind.Person, persons[l.EmployeeId])], title))
            .Append(new PostingLine("bank", p.Total, [new PostingDetail(PostingDetailKind.BankAccount, p.BankAccountId)], title)).ToList();
        return new PostingRequest(PayRunService.SourceModule, SalaryPayment.DocType, p.Id, p.PayDate, title, lines);
    }

    private async Task<Dictionary<int, decimal>> PaidAsync(int runId, int? exceptPaymentId, CancellationToken ct) =>
        (await db.Payments.AsNoTracking().Where(p => p.RunId == runId && p.Id != exceptPaymentId)
            .SelectMany(p => p.Lines).ToListAsync(ct))
        .GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

    private async Task<PayRun> LoadRunAsync(int runId, CancellationToken ct) =>
        await db.Runs.AsNoTracking().Include(r => r.Attendances).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == runId, ct)
        ?? throw new NotFoundException("اجرای حقوق پیدا نشد.");
}
