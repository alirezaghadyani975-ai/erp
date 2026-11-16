using Erp.SharedKernel.Security;
using Erp.Accounting.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

public sealed record SaveChequeOperationRequest(ChequeOperationType Operation, string Date, int? CashBoxId, int? BankAccountId,
    string? Description, List<long> ChequeIds);

/// <summary>چک در فهرست انتخاب یا گزارش (دریافتنی یا پرداختنی).</summary>
public sealed record ChequeRowDto(long Id, ChequeSide Side, string SerialNo, string? SayadId, string DueDate, decimal Amount,
    int PersonId, string PersonName, byte Status, string StatusTitle, string? BankTitle, string? PlaceTitle, string? SourceDocNo,
    long? SourceId);

public sealed record ChequeOperationListItemDto(long Id, ChequeSide Side, int DocNo, string Date, ChequeOperationType Operation,
    string OperationTitle, int ChequeCount, decimal Total, string? TargetTitle, string? Description, TreasuryVoucherDto? Voucher);

public sealed record ChequeOperationDto(long Id, ChequeSide Side, int DocNo, string Date, ChequeOperationType Operation,
    string OperationTitle, int? CashBoxId, int? BankAccountId, string? TargetTitle, string? Description, List<ChequeRowDto> Cheques,
    decimal Total, TreasuryVoucherDto? Voucher, string? LockReason);

/// <summary>
/// عملیات چک: واگذاری، وصول، برگشت، حقوقی، عودت، برگشت از واگذاری (دریافتنی)؛ پاس و ابطال (پرداختنی).
/// هر عملیات روی چند چک با یک سند حسابداری. حذف عملیات فقط وقتی همه چک‌هایش هنوز در وضعیت همین عملیات‌اند.
/// </summary>
public class ChequeOperationService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock,
    TreasuryReportService reports, IAccessControl access)
{
    public async Task<List<ChequeOperationListItemDto>> ListAsync(ChequeSide side, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.View(side));
        var rows = (await db.ChequeOperations.AsNoTracking().Own(access).Include(o => o.Lines).Where(o => o.Side == side)
            .OrderByDescending(o => o.DocDate).ThenByDescending(o => o.DocNo).ToListAsync(ct))
            .Where(o => (o.CashBoxId is not int box || access.CanUse(ScopeKind.CashBox, box))
                        && (o.BankAccountId is not int bank || access.CanUse(ScopeKind.BankAccount, bank)))
            .ToList();
        var amounts = await AmountsAsync(rows.SelectMany(o => o.Lines).ToList(), ct);
        var places = await PlaceTitlesAsync(ct);
        var docType = side == ChequeSide.Receivable ? ChequeOperation.ReceivableDocType : ChequeOperation.PayableDocType;
        var vouchers = await posting.FindAsync(TreasuryModule.Name, docType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(o => new ChequeOperationListItemDto(o.Id, o.Side, o.DocNo, PersianDate.ToPersian(o.DocDate), o.Operation,
            ChequeTitles.Of(o.Operation), o.Lines.Count, o.Lines.Sum(l => amounts.GetValueOrDefault(Key(l))), Target(o, places),
            o.Description, TreasuryText.Voucher(vouchers.GetValueOrDefault(o.Id)))).ToList();
    }

    public async Task<ChequeOperationDto> GetAsync(long id, CancellationToken ct)
    {
        var o = await db.ChequeOperations.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("عملیات چک پیدا نشد.");
        access.Demand(TreasuryPermissions.View(o.Side));
        DemandOperation(o);
        var cheques = o.Side == ChequeSide.Receivable
            ? await reports.ReceivedCoreAsync(new ChequeFilter(Ids: o.Lines.Select(l => l.ReceivedChequeId!.Value).ToList()), ct)
            : await reports.IssuedCoreAsync(new ChequeFilter(Ids: o.Lines.Select(l => l.IssuedChequeId!.Value).ToList()), ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, o.DocType, [id], ct);
        return new ChequeOperationDto(o.Id, o.Side, o.DocNo, PersianDate.ToPersian(o.DocDate), o.Operation, ChequeTitles.Of(o.Operation),
            o.CashBoxId, o.BankAccountId, Target(o, await PlaceTitlesAsync(ct)), o.Description, cheques, cheques.Sum(c => c.Amount),
            TreasuryText.Voucher(vouchers.GetValueOrDefault(id)), await LockReasonAsync(o, ct));
    }

    /// <summary>چک‌هایی که این عملیات رویشان ممکن است.</summary>
    public async Task<List<ChequeRowDto>> CandidatesAsync(ChequeOperationType op, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Operation(op));
        if (ChequeRules.SideOf(op) == ChequeSide.Payable)
            return await reports.IssuedCoreAsync(new ChequeFilter(Statuses: [(byte)IssuedChequeStatus.Issued]), ct);
        return await reports.ReceivedCoreAsync(new ChequeFilter(Statuses: ChequeRules.AllowedFrom(op).Select(s => (byte)s).ToList()), ct);
    }

    public async Task<long> CreateAsync(SaveChequeOperationRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Operation(r.Operation));
        access.DemandFunds([r.CashBoxId], [r.BankAccountId]);
        if (r.ChequeIds.Count == 0) throw new DomainException("حداقل یک چک انتخاب کنید.");
        var side = ChequeRules.SideOf(r.Operation);
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.ChequeOperations.Where(o => o.Side == side).MaxAsync(o => (int?)o.DocNo, ct) ?? 0) + 1;
        var op = new ChequeOperation(r.Operation, no, user.UserId, clock.GetUtcNow().UtcDateTime);
        op.SetHeader(PersianDate.Parse(r.Date), r.CashBoxId, r.BankAccountId, r.Description);

        var lines = new List<PostingLine>();
        var persons = await db.Persons.ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        if (side == ChequeSide.Receivable)
        {
            var cheques = await db.ReceivedCheques.Where(c => r.ChequeIds.Contains(c.Id)).ToListAsync(ct);
            foreach (var id in r.ChequeIds)
            {
                var c = cheques.FirstOrDefault(x => x.Id == id) ?? throw new DomainException("چک انتخاب‌شده پیدا نشد.");
                access.DemandFunds([c.CashBoxId], [c.BankAccountId]);
                var before = (c.Status, c.CashBoxId, c.BankAccountId);
                op.Apply(c);
                lines.AddRange(ReceivableLines(op.Operation, c, before, persons.GetValueOrDefault(c.PersonId, "?")));
            }
        }
        else
        {
            var cheques = await db.IssuedCheques.Where(c => r.ChequeIds.Contains(c.Id)).ToListAsync(ct);
            foreach (var id in r.ChequeIds)
            {
                var c = cheques.FirstOrDefault(x => x.Id == id) ?? throw new DomainException("چک انتخاب‌شده پیدا نشد.");
                access.DemandScope(ScopeKind.BankAccount, c.BankAccountId);
                op.Apply(c);
                lines.AddRange(PayableLines(op.Operation, c, persons.GetValueOrDefault(c.PersonId, "?")));
            }
        }
        db.Add(op);
        await db.SaveChangesAsync(ct);

        var description = TreasuryText.Clip($"{ChequeTitles.Of(op.Operation)} - عملیات {TreasuryText.No(op.DocNo)}"
                                            + (op.Description is { } d ? $" - {d}" : ""));
        await posting.PostAsync(new PostingRequest(TreasuryModule.Name, op.DocType, op.Id, op.DocDate, description, lines), ct);
        await tx.CommitAsync(ct);
        return op.Id;
    }

    /// <summary>فرم خود کاربر (اگر «فقط فرم‌های خودش») و صندوق/حساب مقصد عملیات در محدوده مجاز.</summary>
    private void DemandOperation(ChequeOperation o)
    {
        access.DemandDoc(o);
        access.DemandFunds([o.CashBoxId], [o.BankAccountId]);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var op = await db.ChequeOperations.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
                 ?? throw new NotFoundException("عملیات چک پیدا نشد.");
        access.Demand(TreasuryPermissions.Delete(op.Side));
        DemandOperation(op);
        if (await LockReasonAsync(op, ct) is { } reason) throw new DomainException(reason);
        await posting.UnpostAsync(TreasuryModule.Name, op.DocType, id, ct);
        foreach (var l in op.Lines)
        {
            if (l.ReceivedChequeId is long rc)
            {
                var c = await db.ReceivedCheques.FirstAsync(x => x.Id == rc, ct);
                c.MoveTo((ReceivedChequeStatus)l.FromStatus, l.FromCashBoxId, l.FromBankAccountId);
            }
            else
            {
                var c = await db.IssuedCheques.FirstAsync(x => x.Id == l.IssuedChequeId!.Value, ct);
                c.MoveTo((IssuedChequeStatus)l.FromStatus);
            }
        }
        db.Remove(op);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------

    /// <summary>ردیف‌های سند یک چک دریافتنی: مقصد بدهکار، مبدأ بستانکار (مبلغ منفی).</summary>
    private static IEnumerable<PostingLine> ReceivableLines(ChequeOperationType op, ReceivedCheque c,
        (ReceivedChequeStatus Status, int? Box, int? Bank) before, string person)
    {
        var text = $"چک {c.SerialNo} سررسید {PersianDate.ToPersian(c.DueDate)} {person}";
        PostingLine Line(string key, decimal amount, PostingDetail detail, int? account = null) =>
            new(key, amount, [detail], $"{ChequeTitles.Of(op)} {text}", account, c.SerialNo, c.DueDate);
        var personDetail = new PostingDetail(PostingDetailKind.Person, c.PersonId);

        // مبدأ: جایی که چک تا حالا بوده
        var source = before.Status switch
        {
            ReceivedChequeStatus.InBox => Line("box", -c.Amount, new PostingDetail(PostingDetailKind.CashBox, before.Box!.Value)),
            ReceivedChequeStatus.InCollection => Line("collection", -c.Amount, new PostingDetail(PostingDetailKind.BankAccount, before.Bank!.Value)),
            ReceivedChequeStatus.Bounced => Line("bounced", -c.Amount, personDetail),
            ReceivedChequeStatus.Legal => Line("legal", -c.Amount, personDetail),
            _ => throw new DomainException($"چک {c.SerialNo} در وضعیت قابل عملیات نیست.")
        };
        // مقصد
        var target = op switch
        {
            ChequeOperationType.Deposit => Line("collection", c.Amount, new PostingDetail(PostingDetailKind.BankAccount, c.BankAccountId!.Value)),
            ChequeOperationType.Collect when c.BankAccountId is int bank => Line("bank", c.Amount, new PostingDetail(PostingDetailKind.BankAccount, bank)),
            ChequeOperationType.Collect => Line("cash", c.Amount, new PostingDetail(PostingDetailKind.CashBox, c.CashBoxId!.Value)),
            ChequeOperationType.Bounce => Line("bounced", c.Amount, personDetail),
            ChequeOperationType.Legal => Line("legal", c.Amount, personDetail),
            ChequeOperationType.ReturnToDrawer => Line("counter", c.Amount, personDetail, c.CounterAccountId),
            ChequeOperationType.Withdraw => Line("box", c.Amount, new PostingDetail(PostingDetailKind.CashBox, c.CashBoxId!.Value)),
            _ => throw new DomainException("عملیات معتبر نیست.")
        };
        return [target, source];
    }

    private static IEnumerable<PostingLine> PayableLines(ChequeOperationType op, IssuedCheque c, string person)
    {
        var text = $"{ChequeTitles.Of(op)} چک {c.SerialNo} سررسید {PersianDate.ToPersian(c.DueDate)} {person}";
        var notes = new PostingLine("notes", c.Amount, [new PostingDetail(PostingDetailKind.DueMonth, TreasuryText.PersianMonth(c.DueDate))],
            text, null, c.SerialNo, c.DueDate);
        var other = op == ChequeOperationType.Clear
            ? new PostingLine("bank", -c.Amount, [new PostingDetail(PostingDetailKind.BankAccount, c.BankAccountId)], text, null, c.SerialNo, c.DueDate)
            : new PostingLine("counter", -c.Amount, [new PostingDetail(PostingDetailKind.Person, c.PersonId)], text, c.CounterAccountId,
                c.SerialNo, c.DueDate);
        return [notes, other];
    }

    /// <summary>عملیات فقط وقتی برمی‌گردد که برای هیچ چکش عملیات بعدی ثبت نشده باشد.</summary>
    private async Task<string?> LockReasonAsync(ChequeOperation op, CancellationToken ct)
    {
        foreach (var l in op.Lines)
        {
            var later = l.ReceivedChequeId is long rc
                ? await db.ChequeOperationLines.AnyAsync(x => x.ReceivedChequeId == rc && x.OperationId > op.Id, ct)
                  || await db.PaymentLines.AnyAsync(p => p.ReceivedChequeId == rc, ct)
                : await db.ChequeOperationLines.AnyAsync(x => x.IssuedChequeId == l.IssuedChequeId && x.OperationId > op.Id, ct);
            if (later) return "روی چک‌های این عملیات، عملیات بعدی (یا خرج) ثبت شده است؛ ابتدا آن را حذف کنید.";
        }
        return null;
    }

    private async Task<Dictionary<string, decimal>> AmountsAsync(List<ChequeOperationLine> lines, CancellationToken ct)
    {
        var rIds = lines.Select(l => l.ReceivedChequeId).OfType<long>().ToList();
        var iIds = lines.Select(l => l.IssuedChequeId).OfType<long>().ToList();
        var result = await db.ReceivedCheques.Where(c => rIds.Contains(c.Id)).ToDictionaryAsync(c => $"r{c.Id}", c => c.Amount, ct);
        foreach (var (k, v) in await db.IssuedCheques.Where(c => iIds.Contains(c.Id)).ToDictionaryAsync(c => $"i{c.Id}", c => c.Amount, ct))
            result[k] = v;
        return result;
    }

    private static string Key(ChequeOperationLine l) => l.ReceivedChequeId is long r ? $"r{r}" : $"i{l.IssuedChequeId}";

    private async Task<(Dictionary<int, string> Boxes, Dictionary<int, string> Banks)> PlaceTitlesAsync(CancellationToken ct) =>
        (await db.CashBoxes.ToDictionaryAsync(c => c.Id, c => c.Title, ct), await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct));

    private static string? Target(ChequeOperation o, (Dictionary<int, string> Boxes, Dictionary<int, string> Banks) p) =>
        o.BankAccountId is int b ? p.Banks.GetValueOrDefault(b) : o.CashBoxId is int c ? p.Boxes.GetValueOrDefault(c) : null;
}

// ----------------------------------------------------------------------
// اسناد تضمینی
// ----------------------------------------------------------------------

/// <param name="LeafId">پرداختی چک/سفته: برگ دسته‌چک یا دفتر سفته (الزامی).</param>
/// <param name="BankGuaranteeId">پرداختی ضمانت‌نامه بانکی: ضمانت‌نامه تعریف‌شده (الزامی)؛ مبلغ، شماره و بانک از همان.</param>
public sealed record SaveGuaranteeRequest(GuaranteeDirection Direction, GuaranteeKind Kind, string Date, int PersonId, int? BankAccountId,
    string? Number, decimal Amount, string? DueDate, string? Subject, string? Description, long? LeafId = null, int? BankGuaranteeId = null);

public sealed record GuaranteeDto(long Id, int DocNo, GuaranteeDirection Direction, GuaranteeKind Kind, string Date, int PersonId,
    string PersonName, int? BankAccountId, string? BankTitle, string? Number, decimal Amount, string? DueDate, string? Subject,
    string? Description, bool IsReturned, string? ReturnDate, TreasuryVoucherDto? Voucher, TreasuryVoucherDto? ReturnVoucher,
    long? LeafId = null, int? BankGuaranteeId = null);

/// <summary>اسناد تضمینی دریافتی و پرداختی؛ ثبت و استرداد هر کدام سند انتظامی جدا دارد.</summary>
public class GuaranteeService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock,
    IAccessControl access)
{
    public async Task<List<GuaranteeDto>> ListAsync(GuaranteeDirection? direction, bool? returned, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeView);
        var query = db.Guarantees.AsNoTracking().Own(access);
        if (direction is not null) query = query.Where(g => g.Direction == direction);
        if (returned is bool r) query = query.Where(g => g.IsReturned == r);
        var rows = await query.OrderByDescending(g => g.DocDate).ThenByDescending(g => g.DocNo).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<GuaranteeDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeView);
        var g = await db.Guarantees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("سند تضمینی پیدا نشد.");
        access.DemandDoc(g);
        return (await ToDtosAsync([g], ct))[0];
    }

    public async Task<long> CreateAsync(SaveGuaranteeRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeCreate);
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.Guarantees.MaxAsync(g => (int?)g.DocNo, ct) ?? 0) + 1;
        var g = new Guarantee(r.Direction, no, user.UserId, clock.GetUtcNow().UtcDateTime);
        var person = await ApplyAsync(g, r, ct);
        db.Add(g);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(g, person, Guarantee.DocType, g.DocDate), ct);
        await tx.CommitAsync(ct);
        return g.Id;
    }

    public async Task UpdateAsync(long id, SaveGuaranteeRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeEdit);
        await posting.EnsureChangeableAsync(TreasuryModule.Name, Guarantee.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var g = await db.Guarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("سند تضمینی پیدا نشد.");
        access.DemandDoc(g);
        if (g.Direction != r.Direction) throw new DomainException("جهت سند تضمینی (دریافتی/پرداختی) عوض نمی‌شود.");
        var person = await ApplyAsync(g, r, ct);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(PostingRequest(g, person, Guarantee.DocType, g.DocDate), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeDelete);
        await using var tx = await uow.BeginAsync(ct);
        var g = await db.Guarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("سند تضمینی پیدا نشد.");
        access.DemandDoc(g);
        if (g.IsReturned) throw new DomainException("سند مسترد شده؛ ابتدا استرداد را لغو کنید.");
        await posting.UnpostAsync(TreasuryModule.Name, Guarantee.DocType, id, ct);
        await ReleaseSourceAsync(g, ct);
        db.Remove(g);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>استرداد سند تضمینی با سند انتظامی معکوس.</summary>
    public async Task ReturnAsync(long id, string date, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeReturn);
        await using var tx = await uow.BeginAsync(ct);
        var g = await db.Guarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("سند تضمینی پیدا نشد.");
        access.DemandDoc(g);
        var d = PersianDate.Parse(date);
        g.MarkReturned(d);
        if (g.BankGuaranteeId is int bgId) (await db.BankGuarantees.FirstAsync(x => x.Id == bgId, ct)).TakeBack();
        await db.SaveChangesAsync(ct);
        var person = await db.Persons.FirstAsync(p => p.Id == g.PersonId, ct);
        await posting.PostAsync(PostingRequest(g, person, Guarantee.ReturnDocType, d), ct);
        await tx.CommitAsync(ct);
    }

    public async Task UndoReturnAsync(long id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.GuaranteeReturn);
        await using var tx = await uow.BeginAsync(ct);
        var g = await db.Guarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("سند تضمینی پیدا نشد.");
        access.DemandDoc(g);
        g.UndoReturn();
        if (g.BankGuaranteeId is int bgId) (await db.BankGuarantees.FirstAsync(x => x.Id == bgId, ct)).Deliver(g.DocDate);
        await posting.UnpostAsync(TreasuryModule.Name, Guarantee.ReturnDocType, id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<PersonRef> ApplyAsync(Guarantee g, SaveGuaranteeRequest r, CancellationToken ct)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == r.PersonId, ct) ?? throw new DomainException("شخص را انتخاب کنید.");
        var date = PersianDate.Parse(r.Date);
        var (number, bank, amount) = (r.Number, r.BankAccountId, r.Amount);
        long? leafId = null;
        int? bgId = null;
        var issued = r.Direction == GuaranteeDirection.Issued;

        if (issued && r.Kind is GuaranteeKind.Cheque or GuaranteeKind.PromissoryNote)
        {
            var leaf = r.LeafId is long lid ? await db.InstrumentLeaves.FirstOrDefaultAsync(x => x.Id == lid, ct) : null;
            if (leaf is null)
                throw new DomainException(r.Kind == GuaranteeKind.Cheque
                    ? "برگ چک تضمینی را از دسته‌چک‌های تعریف‌شده انتخاب کنید."
                    : "برگ سفته را از دفترهای سفته تعریف‌شده انتخاب کنید.");
            var book = await db.InstrumentBooks.AsNoTracking().FirstAsync(b => b.Id == leaf.BookId, ct);
            var wanted = r.Kind == GuaranteeKind.Cheque ? InstrumentKind.ChequeBook : InstrumentKind.PromissoryNoteBook;
            if (book.Kind != wanted)
                throw new DomainException(r.Kind == GuaranteeKind.Cheque ? "برگ انتخاب‌شده از دسته‌چک نیست." : "برگ انتخاب‌شده از دفتر سفته نیست.");
            if (book.MaxAmount is decimal max && amount > max)
                throw new DomainException($"مبلغ از سقف سفته‌های «{book.Title}» ({max:#,0}) بیشتر است.");
            if (leaf.Id != g.LeafId) leaf.Use();
            (leafId, number, bank) = (leaf.Id, leaf.SerialNo, book.BankAccountId);
        }
        else if (issued && r.Kind == GuaranteeKind.BankGuarantee)
        {
            var bg = r.BankGuaranteeId is int id ? await db.BankGuarantees.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
            if (bg is null) throw new DomainException("ضمانت‌نامه را از ضمانت‌نامه‌های بانکی تعریف‌شده انتخاب کنید.");
            if (bg.Id != g.BankGuaranteeId) bg.Deliver(date);
            (bgId, number, bank, amount) = (bg.Id, bg.Number, bg.BankAccountId, bg.Amount);
            if (string.IsNullOrWhiteSpace(r.DueDate) && bg.ExpiryDate is { } e) r = r with { DueDate = PersianDate.ToPersian(e) };
        }

        // منبع قبلی (اگر عوض شده) آزاد شود
        if (g.LeafId is long oldLeaf && oldLeaf != leafId)
            (await db.InstrumentLeaves.FirstAsync(x => x.Id == oldLeaf, ct)).Release();
        if (g.BankGuaranteeId is int oldBg && oldBg != bgId)
            (await db.BankGuarantees.FirstAsync(x => x.Id == oldBg, ct)).TakeBack();

        g.Set(date, r.Kind, person.Id, bank, number, amount,
            string.IsNullOrWhiteSpace(r.DueDate) ? null : PersianDate.Parse(r.DueDate), r.Subject, r.Description);
        g.LinkSource(leafId, bgId, number, bank);
        return person;
    }

    private async Task ReleaseSourceAsync(Guarantee g, CancellationToken ct)
    {
        if (g.LeafId is long leaf) (await db.InstrumentLeaves.FirstAsync(x => x.Id == leaf, ct)).Release();
        if (g.BankGuaranteeId is int bg) (await db.BankGuarantees.FirstAsync(x => x.Id == bg, ct)).TakeBack();
    }

    private static PostingRequest PostingRequest(Guarantee g, PersonRef person, string docType, DateOnly date)
    {
        var party = new PostingDetail(PostingDetailKind.Person, person.Id);
        var kind = g.Kind switch
        {
            GuaranteeKind.Cheque => "چک",
            GuaranteeKind.PromissoryNote => "سفته",
            GuaranteeKind.BankGuarantee => "ضمانت‌نامه بانکی",
            _ => "سند"
        };
        var text = $"{kind} تضمینی {g.Number} {person.DisplayName}" + (g.Subject is { } s ? $" بابت {s}" : "");
        var lines = g.Direction == GuaranteeDirection.Received
            ? new List<PostingLine> { new("received", g.Amount, [party], text), new("received_party", g.Amount, [party], text) }
            : g.Kind == GuaranteeKind.BankGuarantee && g.BankAccountId is int bank
                ? [new("issued_bank", g.Amount, [new PostingDetail(PostingDetailKind.BankAccount, bank)], text), new("issued_party", g.Amount, [party], text)]
                : [new("issued", g.Amount, [party], text), new("issued_party", g.Amount, [party], text)];
        var title = docType == Guarantee.ReturnDocType ? "استرداد" : "ثبت";
        var description = TreasuryText.Clip($"{title} سند تضمینی {TreasuryText.No(g.DocNo)} ({(g.Direction == GuaranteeDirection.Received ? "دریافتی" : "پرداختی")}) - {text}");
        return new PostingRequest(TreasuryModule.Name, docType, g.Id, date, description, lines);
    }

    private async Task<List<GuaranteeDto>> ToDtosAsync(List<Guarantee> rows, CancellationToken ct)
    {
        var personIds = rows.Select(r => r.PersonId).Distinct().ToList();
        var persons = await db.Persons.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var banks = await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Guarantee.DocType, ids, ct);
        var returns = await posting.FindAsync(TreasuryModule.Name, Guarantee.ReturnDocType, ids, ct);
        return rows.Select(g => new GuaranteeDto(g.Id, g.DocNo, g.Direction, g.Kind, PersianDate.ToPersian(g.DocDate), g.PersonId,
            persons.GetValueOrDefault(g.PersonId, "?"), g.BankAccountId, g.BankAccountId is int b ? banks.GetValueOrDefault(b) : null,
            g.Number, g.Amount, g.DueDate is { } d ? PersianDate.ToPersian(d) : null, g.Subject, g.Description, g.IsReturned,
            g.ReturnDate is { } rd ? PersianDate.ToPersian(rd) : null, TreasuryText.Voucher(vouchers.GetValueOrDefault(g.Id)),
            TreasuryText.Voucher(returns.GetValueOrDefault(g.Id)), g.LeafId, g.BankGuaranteeId)).ToList();
    }
}
