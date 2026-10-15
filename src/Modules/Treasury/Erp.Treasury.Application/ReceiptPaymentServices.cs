using System.Globalization;
using Erp.Accounting.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

public sealed record TreasuryVoucherDto(long VoucherId, int TempNo, int? VoucherNo, byte Status);

/// <summary>مشخصات چک در درخواست (تاریخ سررسید شمسی).</summary>
/// <param name="LeafId">چک صادره: برگ دسته‌چک (الزامی)؛ شماره و حساب بانکی از همان برگ می‌آید.</param>
public sealed record ChequeInput(string SerialNo, string? SayadId, string DueDate, short? BankId = null, string? Branch = null,
    string? DrawerName = null, string? AccountNo = null, long? LeafId = null);

public sealed record ChequeDto(long Id, string SerialNo, string? SayadId, string DueDate, short? BankId, string? BankTitle,
    string? Branch, string? DrawerName, string? AccountNo, decimal Amount, byte Status, string StatusTitle, long? LeafId = null);

public sealed record TreasuryLineRequest(Instrument Instrument, int? CashBoxId, int? BankAccountId, decimal Amount,
    string? RefNo = null, string? Description = null, ChequeInput? Cheque = null, long? ReceivedChequeId = null);

public sealed record SaveReceiptPaymentRequest(string Date, int PersonId, short TypeId, string? Description, List<TreasuryLineRequest> Lines);

public sealed record TreasuryLineDto(short RowNo, Instrument Instrument, int? CashBoxId, int? BankAccountId, long? ReceivedChequeId,
    decimal Amount, string? RefNo, string? Description, ChequeDto? Cheque);

public sealed record ReceiptPaymentDto(long Id, int DocNo, string Date, int PersonId, string PersonName, short TypeId, string TypeTitle,
    string? Description, decimal Total, List<TreasuryLineDto> Lines, TreasuryVoucherDto? Voucher, string? LockReason);

public sealed record ReceiptPaymentListItemDto(long Id, int DocNo, string Date, string PersonName, string TypeTitle, decimal Total,
    decimal CashTotal, decimal BankTotal, decimal ChequeTotal, string? Description, TreasuryVoucherDto? Voucher);

internal static class TreasuryText
{
    private static readonly PersianCalendar Calendar = new();

    public static string No(int docNo) => docNo.ToString("000000");

    public static int PersianMonth(DateOnly date) => Calendar.GetMonth(date.ToDateTime(TimeOnly.MinValue));

    public static string Clip(string text) => text.Length <= 500 ? text : text[..500];

    public static TreasuryVoucherDto? Voucher(PostedVoucher? v) =>
        v is null ? null : new TreasuryVoucherDto(v.VoucherId, v.TempNo, v.VoucherNo, v.Status);

    public static ChequeInfo Info(ChequeInput c) =>
        new(c.SerialNo, c.SayadId, PersianDate.Parse(c.DueDate), c.BankId, c.Branch, c.DrawerName, c.AccountNo);
}

/// <summary>
/// دریافت: ثبت، اصلاح و حذف با سند خودکار. چک‌های دریافتی با همین فرم ساخته می‌شوند؛
/// فرمی که چکش عملیات خورده (واگذاری، خرج، ...) دیگر اصلاح یا حذف نمی‌شود.
/// </summary>
public class ReceiptService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock)
{
    public async Task<List<ReceiptPaymentListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        var query = db.Receipts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }
        var rows = await query.OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo)
            .Select(r => new
            {
                r.Id, r.DocNo, r.DocDate, r.Description,
                Person = db.Persons.Where(p => p.Id == r.PersonId).Select(p => p.DisplayName).FirstOrDefault(),
                Type = db.TransactionTypes.Where(t => t.Id == r.TypeId).Select(t => t.Title).FirstOrDefault(),
                Cash = r.Lines.Where(l => l.Instrument == Instrument.Cash).Sum(l => (decimal?)l.Amount) ?? 0,
                Bank = r.Lines.Where(l => l.Instrument == Instrument.Bank).Sum(l => (decimal?)l.Amount) ?? 0,
                Cheque = r.Lines.Where(l => l.Instrument == Instrument.Cheque).Sum(l => (decimal?)l.Amount) ?? 0
            }).ToListAsync(ct);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Person ?? "").Contains(term) || (r.Description ?? "").Contains(term)
                                   || TreasuryText.No(r.DocNo).Contains(term)).ToList();
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Receipt.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new ReceiptPaymentListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.Person ?? "?",
            r.Type ?? "?", r.Cash + r.Bank + r.Cheque, r.Cash, r.Bank, r.Cheque, r.Description,
            TreasuryText.Voucher(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<ReceiptPaymentDto> GetAsync(long id, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: false, ct);
        var person = await db.Persons.Where(p => p.Id == r.PersonId).Select(p => p.DisplayName).FirstOrDefaultAsync(ct);
        var type = await db.TransactionTypes.Where(t => t.Id == r.TypeId).Select(t => t.Title).FirstOrDefaultAsync(ct);
        var banks = await db.Banks.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Receipt.DocType, [id], ct);
        return new ReceiptPaymentDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.PersonId, person ?? "?", r.TypeId, type ?? "?",
            r.Description, r.Total,
            r.Lines.OrderBy(l => l.RowNo).Select(l => new TreasuryLineDto(l.RowNo, l.Instrument, l.CashBoxId, l.BankAccountId, null,
                l.Amount, l.RefNo, l.Description, l.Cheque is { } c ? ToDto(c, banks) : null)).ToList(),
            TreasuryText.Voucher(vouchers.GetValueOrDefault(id)), await LockReasonAsync(r, ct));
    }

    public async Task<long> CreateAsync(SaveReceiptPaymentRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.Receipts.MaxAsync(r => (int?)r.DocNo, ct) ?? 0) + 1;
        var receipt = new Receipt(no, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (person, type) = await ApplyAsync(receipt, request, ct);
        db.Add(receipt);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(receipt, person, type), ct);
        await tx.CommitAsync(ct);
        return receipt.Id;
    }

    public async Task UpdateAsync(long id, SaveReceiptPaymentRequest request, CancellationToken ct)
    {
        await posting.EnsureChangeableAsync(TreasuryModule.Name, Receipt.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var receipt = await LoadAsync(id, tracking: true, ct);
        if (await LockReasonAsync(receipt, ct) is { } reason) throw new DomainException(reason);
        receipt.ClearLines();
        await db.SaveChangesAsync(ct);
        var (person, type) = await ApplyAsync(receipt, request, ct);
        receipt.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(receipt, person, type), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var receipt = await LoadAsync(id, tracking: true, ct);
        if (await LockReasonAsync(receipt, ct) is { } reason) throw new DomainException(reason);
        await posting.UnpostAsync(TreasuryModule.Name, Receipt.DocType, id, ct);
        db.Remove(receipt);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task<(PersonRef Person, TransactionType Type)> ApplyAsync(Receipt receipt, SaveReceiptPaymentRequest r, CancellationToken ct)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == r.PersonId, ct)
                     ?? throw new DomainException("پرداخت‌کننده (طرف حساب) را انتخاب کنید.");
        var type = await db.TransactionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TypeId, ct)
                   ?? throw new DomainException("نوع دریافت را انتخاب کنید.");
        if (type.Kind != TransactionKind.Receipt) throw new DomainException($"«{type.Title}» نوع پرداخت است، نه دریافت.");
        if (r.Lines.Count == 0) throw new DomainException("حداقل یک ردیف (نقد، بانکی یا چک) لازم است.");
        await FundsActiveAsync(r.Lines, ct);

        receipt.SetHeader(PersianDate.Parse(r.Date), person.Id, type.Id, r.Description);
        foreach (var l in r.Lines)
            receipt.AddLine(l.Instrument, l.CashBoxId, l.BankAccountId, l.Amount, l.RefNo, l.Description,
                l.Cheque is { } c ? TreasuryText.Info(c) : null, type.AccountId);

        var sayads = receipt.Lines.Select(l => l.Cheque?.SayadId).OfType<string>().ToList();
        if (sayads.Count != sayads.Distinct().Count()) throw new DomainException("شناسه صیادی تکراری در ردیف‌ها.");
        if (sayads.Count > 0)
        {
            // ردیف‌های قبلی همین فرم پیش از این حذف و ذخیره شده‌اند
            var dup = await db.ReceivedCheques.Where(c => c.SayadId != null && sayads.Contains(c.SayadId))
                .Select(c => c.SayadId).FirstOrDefaultAsync(ct);
            if (dup is not null) throw new DomainException($"چکی با شناسه صیادی {dup} قبلاً ثبت شده است.");
        }
        return (person, type);
    }

    private async Task FundsActiveAsync(List<TreasuryLineRequest> lines, CancellationToken ct)
    {
        var boxIds = lines.Select(l => l.CashBoxId).OfType<int>().Distinct().ToList();
        var bankIds = lines.Select(l => l.BankAccountId).OfType<int>().Distinct().ToList();
        var inactiveBox = await db.CashBoxes.Where(c => boxIds.Contains(c.Id) && !c.IsActive).Select(c => c.Title).FirstOrDefaultAsync(ct);
        if (inactiveBox is not null) throw new DomainException($"صندوق «{inactiveBox}» غیرفعال است.");
        var inactiveBank = await db.BankAccounts.Where(b => bankIds.Contains(b.Id) && !b.IsActive).Select(b => b.Title).FirstOrDefaultAsync(ct);
        if (inactiveBank is not null) throw new DomainException($"حساب بانکی «{inactiveBank}» غیرفعال است.");
    }

    private static PostingRequest PostingRequest(Receipt r, PersonRef person, TransactionType type)
    {
        var lines = r.Lines.OrderBy(l => l.RowNo).Select(l => l.Instrument switch
        {
            Instrument.Cash => new PostingLine("cash", l.Amount, [new PostingDetail(PostingDetailKind.CashBox, l.CashBoxId!.Value)],
                l.Description, FollowUpNo: l.RefNo),
            Instrument.Bank => new PostingLine("bank", l.Amount, [new PostingDetail(PostingDetailKind.BankAccount, l.BankAccountId!.Value)],
                l.Description, FollowUpNo: l.RefNo),
            _ => new PostingLine("cheque", l.Amount, [new PostingDetail(PostingDetailKind.CashBox, l.CashBoxId!.Value)],
                $"چک {l.Cheque!.SerialNo} سررسید {PersianDate.ToPersian(l.Cheque.DueDate)} از {person.DisplayName}",
                FollowUpNo: l.Cheque.SerialNo, FollowUpDate: l.Cheque.DueDate)
        }).ToList();
        lines.Add(new PostingLine("counter", r.Total, [new PostingDetail(PostingDetailKind.Person, person.Id)],
            $"{type.Title} - دریافت {TreasuryText.No(r.DocNo)}", AccountId: type.AccountId));
        var description = TreasuryText.Clip($"دریافت {TreasuryText.No(r.DocNo)} از {person.DisplayName} ({type.Title})"
                                            + (r.Description is { } d ? $" - {d}" : ""));
        return new PostingRequest(TreasuryModule.Name, Receipt.DocType, r.Id, r.DocDate, description, lines);
    }

    /// <summary>دلیل قفل بودن فرم (چک عملیات خورده)؛ null یعنی قابل اصلاح.</summary>
    private async Task<string?> LockReasonAsync(Receipt r, CancellationToken ct)
    {
        var cheques = r.Lines.Where(l => l.Cheque is not null).Select(l => l.Cheque!).ToList();
        var moved = cheques.FirstOrDefault(c => c.Status != ReceivedChequeStatus.InBox);
        if (moved is not null)
            return $"چک {moved.SerialNo} این فرم «{ChequeTitles.Of(moved.Status)}» است؛ فرم اصلاح یا حذف نمی‌شود.";
        var ids = cheques.Select(c => c.Id).ToList();
        if (ids.Count > 0 && await db.ChequeOperationLines.AnyAsync(l => l.ReceivedChequeId != null && ids.Contains(l.ReceivedChequeId.Value), ct))
            return "چک‌های این فرم عملیات دارند؛ ابتدا عملیات را حذف کنید.";
        return null;
    }

    private async Task<Receipt> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.Receipts.Include(r => r.Lines).ThenInclude(l => l.Cheque).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException($"دریافت با شناسه {id} پیدا نشد.");
    }

    internal static ChequeDto ToDto(ReceivedCheque c, IReadOnlyDictionary<short, string> banks) =>
        new(c.Id, c.SerialNo, c.SayadId, PersianDate.ToPersian(c.DueDate), c.BankId, c.BankId is short b ? banks.GetValueOrDefault(b) : null,
            c.Branch, c.DrawerName, c.AccountNo, c.Amount, (byte)c.Status, ChequeTitles.Of(c.Status));
}

/// <summary>
/// پرداخت: ثبت، اصلاح و حذف با سند خودکار. چک صادره با همین فرم ساخته می‌شود و چک دریافتیِ خرج‌شده
/// از صندوق بیرون می‌رود؛ با حذف یا اصلاح فرم، چک خرج‌شده به صندوق برمی‌گردد.
/// </summary>
public class PaymentService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock)
{
    public async Task<List<ReceiptPaymentListItemDto>> ListAsync(string? term, string? fromDate, string? toDate, CancellationToken ct)
    {
        var query = db.Payments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }
        var rows = await query.OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo)
            .Select(r => new
            {
                r.Id, r.DocNo, r.DocDate, r.Description,
                Person = db.Persons.Where(p => p.Id == r.PersonId).Select(p => p.DisplayName).FirstOrDefault(),
                Type = db.TransactionTypes.Where(t => t.Id == r.TypeId).Select(t => t.Title).FirstOrDefault(),
                Cash = r.Lines.Where(l => l.Instrument == Instrument.Cash).Sum(l => (decimal?)l.Amount) ?? 0,
                Bank = r.Lines.Where(l => l.Instrument == Instrument.Bank).Sum(l => (decimal?)l.Amount) ?? 0,
                Cheque = r.Lines.Where(l => l.Instrument == Instrument.Cheque || l.Instrument == Instrument.SpendCheque).Sum(l => (decimal?)l.Amount) ?? 0
            }).ToListAsync(ct);
        term = PersianText.NormalizeOrNull(term);
        if (term is not null)
            rows = rows.Where(r => (r.Person ?? "").Contains(term) || (r.Description ?? "").Contains(term)
                                   || TreasuryText.No(r.DocNo).Contains(term)).ToList();
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Payment.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new ReceiptPaymentListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.Person ?? "?",
            r.Type ?? "?", r.Cash + r.Bank + r.Cheque, r.Cash, r.Bank, r.Cheque, r.Description,
            TreasuryText.Voucher(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<ReceiptPaymentDto> GetAsync(long id, CancellationToken ct)
    {
        var p = await LoadAsync(id, tracking: false, ct);
        var person = await db.Persons.Where(x => x.Id == p.PersonId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
        var type = await db.TransactionTypes.Where(t => t.Id == p.TypeId).Select(t => t.Title).FirstOrDefaultAsync(ct);
        var banks = await db.Banks.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var spentIds = p.Lines.Select(l => l.ReceivedChequeId).OfType<long>().ToList();
        var spent = await db.ReceivedCheques.AsNoTracking().Where(c => spentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Payment.DocType, [id], ct);
        return new ReceiptPaymentDto(p.Id, p.DocNo, PersianDate.ToPersian(p.DocDate), p.PersonId, person ?? "?", p.TypeId, type ?? "?",
            p.Description, p.Total,
            p.Lines.OrderBy(l => l.RowNo).Select(l => new TreasuryLineDto(l.RowNo, l.Instrument, l.CashBoxId, l.BankAccountId,
                l.ReceivedChequeId, l.Amount, l.RefNo, l.Description,
                l.Cheque is { } c
                    ? new ChequeDto(c.Id, c.SerialNo, c.SayadId, PersianDate.ToPersian(c.DueDate), null, null, null, null, null, c.Amount,
                        (byte)c.Status, ChequeTitles.Of(c.Status), c.LeafId)
                    : l.ReceivedChequeId is long rc && spent.TryGetValue(rc, out var s) ? ReceiptService.ToDto(s, banks) : null)).ToList(),
            TreasuryText.Voucher(vouchers.GetValueOrDefault(id)), await LockReasonAsync(p, ct));
    }

    /// <summary>چک‌های در صندوق برای خرج کردن در پرداخت.</summary>
    public async Task<List<ChequeDto>> GetSpendableChequesAsync(CancellationToken ct)
    {
        var banks = await db.Banks.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var rows = await db.ReceivedCheques.AsNoTracking().Where(c => c.Status == ReceivedChequeStatus.InBox)
            .OrderBy(c => c.DueDate).ToListAsync(ct);
        return rows.Select(c => ReceiptService.ToDto(c, banks)).ToList();
    }

    public async Task<long> CreateAsync(SaveReceiptPaymentRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.Payments.MaxAsync(r => (int?)r.DocNo, ct) ?? 0) + 1;
        var payment = new Payment(no, user.UserId, clock.GetUtcNow().UtcDateTime);
        var (person, type, spent) = await ApplyAsync(payment, request, ct);
        db.Add(payment);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(payment, person, type, spent), ct);
        await tx.CommitAsync(ct);
        return payment.Id;
    }

    public async Task UpdateAsync(long id, SaveReceiptPaymentRequest request, CancellationToken ct)
    {
        await posting.EnsureChangeableAsync(TreasuryModule.Name, Payment.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var payment = await LoadAsync(id, tracking: true, ct);
        if (await LockReasonAsync(payment, ct) is { } reason) throw new DomainException(reason);
        await ReleaseSpentAsync(payment, ct);
        await ReleaseLeavesAsync(payment, ct);
        payment.ClearLines();
        await db.SaveChangesAsync(ct);
        var (person, type, spent) = await ApplyAsync(payment, request, ct);
        payment.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(payment, person, type, spent), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var payment = await LoadAsync(id, tracking: true, ct);
        if (await LockReasonAsync(payment, ct) is { } reason) throw new DomainException(reason);
        await posting.UnpostAsync(TreasuryModule.Name, Payment.DocType, id, ct);
        await ReleaseSpentAsync(payment, ct);
        await ReleaseLeavesAsync(payment, ct);
        db.Remove(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    /// <summary>برگ‌های دسته‌چک چک‌های این فرم دوباره سفید می‌شوند (پیش از بازنویسی یا حذف).</summary>
    private async Task ReleaseLeavesAsync(Payment payment, CancellationToken ct)
    {
        var ids = payment.Lines.Select(l => l.Cheque?.LeafId).OfType<long>().ToList();
        foreach (var leaf in await db.InstrumentLeaves.Where(x => ids.Contains(x.Id)).ToListAsync(ct)) leaf.Release();
    }

    private async Task ReleaseSpentAsync(Payment payment, CancellationToken ct)
    {
        var ids = payment.Lines.Select(l => l.ReceivedChequeId).OfType<long>().ToList();
        var cheques = await db.ReceivedCheques.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        foreach (var c in cheques.Where(c => c.Status == ReceivedChequeStatus.Spent))
            c.MoveTo(ReceivedChequeStatus.InBox, c.CashBoxId, null);
    }

    private async Task<(PersonRef, TransactionType, Dictionary<long, ReceivedCheque>)> ApplyAsync(Payment payment,
        SaveReceiptPaymentRequest r, CancellationToken ct)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == r.PersonId, ct)
                     ?? throw new DomainException("دریافت‌کننده (طرف حساب) را انتخاب کنید.");
        var type = await db.TransactionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TypeId, ct)
                   ?? throw new DomainException("نوع پرداخت را انتخاب کنید.");
        if (type.Kind != TransactionKind.Payment) throw new DomainException($"«{type.Title}» نوع دریافت است، نه پرداخت.");
        if (r.Lines.Count == 0) throw new DomainException("حداقل یک ردیف (نقد، بانکی، چک یا خرج چک) لازم است.");

        var boxIds = r.Lines.Select(l => l.CashBoxId).OfType<int>().Distinct().ToList();
        var bankIds = r.Lines.Select(l => l.BankAccountId).OfType<int>().Distinct().ToList();
        if (await db.CashBoxes.Where(c => boxIds.Contains(c.Id) && !c.IsActive).Select(c => c.Title).FirstOrDefaultAsync(ct) is { } box)
            throw new DomainException($"صندوق «{box}» غیرفعال است.");
        if (await db.BankAccounts.Where(b => bankIds.Contains(b.Id) && !b.IsActive).Select(b => b.Title).FirstOrDefaultAsync(ct) is { } bank)
            throw new DomainException($"حساب بانکی «{bank}» غیرفعال است.");

        var spentIds = r.Lines.Select(l => l.ReceivedChequeId).OfType<long>().ToList();
        if (spentIds.Count != spentIds.Distinct().Count()) throw new DomainException("یک چک دو بار خرج شده است.");
        var spent = await db.ReceivedCheques.Where(c => spentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        // چک صادره فقط از برگ سفید دسته‌چک همان حساب بانکی
        var leafIds = r.Lines.Where(l => l.Instrument == Instrument.Cheque).Select(l => l.Cheque?.LeafId).OfType<long>().ToList();
        if (leafIds.Count != leafIds.Distinct().Count()) throw new DomainException("یک برگ چک دو بار انتخاب شده است.");
        var leaves = await db.InstrumentLeaves.Where(x => leafIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var bookIds = leaves.Values.Select(x => x.BookId).Distinct().ToList();
        var books = await db.InstrumentBooks.AsNoTracking().Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        payment.SetHeader(PersianDate.Parse(r.Date), person.Id, type.Id, r.Description);
        var row = 0;
        foreach (var l in r.Lines)
        {
            row++;
            ReceivedCheque? cheque = l.ReceivedChequeId is long rc ? spent.GetValueOrDefault(rc) : null;
            var info = l.Cheque is { } c ? TreasuryText.Info(c) : null;
            var bankAccountId = l.BankAccountId;
            InstrumentLeaf? leaf = null;
            if (l.Instrument == Instrument.Cheque)
            {
                leaf = l.Cheque?.LeafId is long lid ? leaves.GetValueOrDefault(lid) : null;
                if (leaf is null) throw new DomainException($"ردیف {row}: برگ چک را از دسته‌چک انتخاب کنید.");
                var book = books[leaf.BookId];
                if (book.Kind != InstrumentKind.ChequeBook) throw new DomainException($"ردیف {row}: برگ انتخاب‌شده از دسته‌چک نیست.");
                if (bankAccountId is int ba && ba != book.BankAccountId)
                    throw new DomainException($"ردیف {row}: برگ {leaf.SerialNo} از دسته‌چک حساب بانکی دیگری است.");
                bankAccountId = book.BankAccountId;
                info = (info ?? throw new DomainException($"ردیف {row}: مشخصات چک لازم است.")) with { SerialNo = leaf.SerialNo };
            }
            var line = payment.AddLine(l.Instrument, l.CashBoxId, bankAccountId, cheque, l.Amount, l.RefNo, l.Description, info,
                type.AccountId);
            if (leaf is not null)
            {
                leaf.Use();
                line.Cheque!.LinkLeaf(leaf.Id);
            }
            if (l.Instrument == Instrument.SpendCheque) cheque!.MoveTo(ReceivedChequeStatus.Spent, cheque.CashBoxId, null);
        }

        // شماره چک صادره در هر حساب بانکی تکراری نباشد
        foreach (var ic in payment.Lines.Where(l => l.Cheque is not null).Select(l => l.Cheque!))
        {
            if (payment.Lines.Count(l => l.Cheque is { } o && o.BankAccountId == ic.BankAccountId && o.SerialNo == ic.SerialNo) > 1
                || await db.IssuedCheques.AnyAsync(o => o.BankAccountId == ic.BankAccountId && o.SerialNo == ic.SerialNo
                                                       && o.PaymentLineId != ic.PaymentLineId, ct))
                throw new DomainException($"چک شماره {ic.SerialNo} از این حساب بانکی قبلاً صادر شده است.");
        }
        return (person, type, spent);
    }

    private static PostingRequest PostingRequest(Payment p, PersonRef person, TransactionType type, Dictionary<long, ReceivedCheque> spent)
    {
        var lines = new List<PostingLine>
        {
            new("counter", p.Total, [new PostingDetail(PostingDetailKind.Person, person.Id)],
                $"{type.Title} - پرداخت {TreasuryText.No(p.DocNo)}", AccountId: type.AccountId)
        };
        foreach (var l in p.Lines.OrderBy(l => l.RowNo))
        {
            lines.Add(l.Instrument switch
            {
                Instrument.Cash => new PostingLine("cash", l.Amount, [new PostingDetail(PostingDetailKind.CashBox, l.CashBoxId!.Value)],
                    l.Description, FollowUpNo: l.RefNo),
                Instrument.Bank => new PostingLine("bank", l.Amount, [new PostingDetail(PostingDetailKind.BankAccount, l.BankAccountId!.Value)],
                    l.Description, FollowUpNo: l.RefNo),
                Instrument.Cheque => new PostingLine("cheque", l.Amount,
                    [new PostingDetail(PostingDetailKind.DueMonth, TreasuryText.PersianMonth(l.Cheque!.DueDate))],
                    $"چک {l.Cheque.SerialNo} سررسید {PersianDate.ToPersian(l.Cheque.DueDate)} به {person.DisplayName}",
                    FollowUpNo: l.Cheque.SerialNo, FollowUpDate: l.Cheque.DueDate),
                _ => SpendLine(l, spent[l.ReceivedChequeId!.Value], person)
            });
        }
        var description = TreasuryText.Clip($"پرداخت {TreasuryText.No(p.DocNo)} به {person.DisplayName} ({type.Title})"
                                            + (p.Description is { } d ? $" - {d}" : ""));
        return new PostingRequest(TreasuryModule.Name, Payment.DocType, p.Id, p.DocDate, description, lines);
    }

    private static PostingLine SpendLine(PaymentLine l, ReceivedCheque c, PersonRef person) =>
        new("spend", l.Amount, [new PostingDetail(PostingDetailKind.CashBox, c.CashBoxId!.Value)],
            $"خرج چک {c.SerialNo} سررسید {PersianDate.ToPersian(c.DueDate)} به {person.DisplayName}",
            FollowUpNo: c.SerialNo, FollowUpDate: c.DueDate);

    private async Task<string?> LockReasonAsync(Payment p, CancellationToken ct)
    {
        var moved = p.Lines.Select(l => l.Cheque).FirstOrDefault(c => c is not null && c.Status != IssuedChequeStatus.Issued);
        if (moved is not null)
            return $"چک {moved.SerialNo} این فرم «{ChequeTitles.Of(moved.Status)}» است؛ فرم اصلاح یا حذف نمی‌شود.";
        var spentIds = p.Lines.Select(l => l.ReceivedChequeId).OfType<long>().ToList();
        if (spentIds.Count > 0 && await db.ReceivedCheques.AnyAsync(c => spentIds.Contains(c.Id) && c.Status != ReceivedChequeStatus.Spent, ct))
            return "وضعیت چک خرج‌شده این فرم تغییر کرده است.";
        return null;
    }

    private async Task<Payment> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.Payments.Include(p => p.Lines).ThenInclude(l => l.Cheque).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException($"پرداخت با شناسه {id} پیدا نشد.");
    }
}
