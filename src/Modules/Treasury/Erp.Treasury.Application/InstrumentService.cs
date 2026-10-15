using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

public sealed record InstrumentBookDto(int Id, InstrumentKind Kind, int? BankAccountId, string? BankTitle, string Title, string? Series,
    long FromNo, long ToNo, byte Digits, string ReceivedDate, decimal? MaxAmount, bool IsActive, string? Notes,
    int Total, int Blank, int Used, int Void, string? NextSerial, string Range);

public sealed record CreateInstrumentBookRequest(InstrumentKind Kind, int? BankAccountId, string Title, string? Series, long FromNo, long ToNo,
    byte Digits, string ReceivedDate, decimal? MaxAmount, string? Notes);

public sealed record UpdateInstrumentBookRequest(string Title, string ReceivedDate, decimal? MaxAmount, bool IsActive, string? Notes);

/// <param name="UsedIn">فرمی که برگ را مصرف کرده (مثلاً «پرداخت 000012»).</param>
public sealed record InstrumentLeafDto(long Id, long No, string SerialNo, LeafStatus Status, string StatusTitle, string? VoidDate,
    string? VoidReason, string? UsedIn, string? UsedUrl);

/// <summary>برگ سفید برای انتخاب در فرم.</summary>
public sealed record LeafOption(long Id, int BookId, string BookTitle, string SerialNo, int? BankAccountId, decimal? MaxAmount)
{
    public string Display => $"{SerialNo} — {BookTitle}";
}

public sealed record BankGuaranteeDto(int Id, int BankAccountId, string BankTitle, string Number, decimal Amount, string IssueDate,
    string? ExpiryDate, string? Subject, BankGuaranteeStatus Status, string StatusTitle, string? CancelDate, string? Notes,
    string? HolderName, long? GuaranteeId, bool IsExpired)
{
    public string Display => $"{Number} — {BankTitle} — {Amount:#,0}";
}

public sealed record SaveBankGuaranteeRequest(int BankAccountId, string Number, decimal Amount, string IssueDate, string? ExpiryDate,
    string? Subject, string? Notes);

/// <summary>
/// اسناد پرداختنی تعریف‌شده: دسته‌چک، دفتر سفته (با برگ‌ها) و ضمانت‌نامه‌های بانکی شرکت.
/// چک صادره و اسناد تضمینی پرداختی فقط از این‌ها انتخاب می‌شوند.
/// </summary>
public class InstrumentService(ITreasuryDbContext db, TimeProvider clock)
{
    public static string Title(LeafStatus s) => s switch
    {
        LeafStatus.Blank => "سفید",
        LeafStatus.Used => "استفاده‌شده",
        _ => "باطل"
    };

    public static string Title(BankGuaranteeStatus s) => s switch
    {
        BankGuaranteeStatus.Available => "نزد ما",
        BankGuaranteeStatus.Delivered => "نزد ذی‌نفع",
        _ => "باطل / عودت به بانک"
    };

    // -------------------------------------------------------------- دفترچه‌ها

    public async Task<List<InstrumentBookDto>> GetBooksAsync(InstrumentKind? kind, CancellationToken ct)
    {
        var query = db.InstrumentBooks.AsNoTracking();
        if (kind is not null) query = query.Where(b => b.Kind == kind);
        var books = await query.OrderByDescending(b => b.IsActive).ThenBy(b => b.Kind).ThenByDescending(b => b.ReceivedDate).ToListAsync(ct);
        var ids = books.Select(b => b.Id).ToList();
        var counts = await db.InstrumentLeaves.Where(l => ids.Contains(l.BookId))
            .GroupBy(l => new { l.BookId, l.Status }).Select(g => new { g.Key.BookId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var next = await db.InstrumentLeaves.Where(l => ids.Contains(l.BookId) && l.Status == LeafStatus.Blank)
            .GroupBy(l => l.BookId).Select(g => new { BookId = g.Key, No = g.Min(l => l.No) }).ToListAsync(ct);
        var banks = await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        int Count(int id, LeafStatus s) => counts.Where(c => c.BookId == id && c.Status == s).Sum(c => c.Count);
        return books.Select(b => new InstrumentBookDto(b.Id, b.Kind, b.BankAccountId,
            b.BankAccountId is int ba ? banks.GetValueOrDefault(ba) : null, b.Title, b.Series, b.FromNo, b.ToNo, b.Digits,
            PersianDate.ToPersian(b.ReceivedDate), b.MaxAmount, b.IsActive, b.Notes, (int)(b.ToNo - b.FromNo + 1),
            Count(b.Id, LeafStatus.Blank), Count(b.Id, LeafStatus.Used), Count(b.Id, LeafStatus.Void),
            next.FirstOrDefault(n => n.BookId == b.Id) is { } n ? b.SerialOf(n.No) : null,
            $"{b.SerialOf(b.FromNo)} تا {b.SerialOf(b.ToNo)}")).ToList();
    }

    public async Task<int> CreateBookAsync(CreateInstrumentBookRequest r, CancellationToken ct)
    {
        if (r.Kind == InstrumentKind.ChequeBook)
        {
            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == r.BankAccountId, ct)
                       ?? throw new DomainException("حساب بانکی دسته‌چک را انتخاب کنید.");
            if (!bank.IsActive) throw new DomainException($"حساب بانکی «{bank.Title}» غیرفعال است.");
        }
        var book = new InstrumentBook(r.Kind, r.BankAccountId, r.Title, r.Series, r.FromNo, r.ToNo, r.Digits,
            PersianDate.Parse(r.ReceivedDate), r.MaxAmount, r.Notes);

        // هم‌پوشانی با دفترچه دیگر همان حساب (یا همان نوع سفته) و همان سری
        var overlap = await db.InstrumentBooks.AsNoTracking()
            .Where(b => b.Kind == book.Kind && b.BankAccountId == book.BankAccountId && b.Series == book.Series
                        && b.FromNo <= book.ToNo && book.FromNo <= b.ToNo)
            .Select(b => b.Title).FirstOrDefaultAsync(ct);
        if (overlap is not null) throw new DomainException($"بازه این برگ‌ها با «{overlap}» هم‌پوشانی دارد.");

        db.Add(book);
        await db.SaveChangesAsync(ct);
        return book.Id;
    }

    public async Task UpdateBookAsync(int id, UpdateInstrumentBookRequest r, CancellationToken ct)
    {
        var book = await db.InstrumentBooks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("دفترچه پیدا نشد.");
        book.Update(r.Title, PersianDate.Parse(r.ReceivedDate), r.MaxAmount, r.Notes);
        book.SetActive(r.IsActive);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteBookAsync(int id, CancellationToken ct)
    {
        var book = await db.InstrumentBooks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("دفترچه پیدا نشد.");
        if (await db.InstrumentLeaves.AnyAsync(l => l.BookId == id && l.Status == LeafStatus.Used, ct))
            throw new DomainException($"از «{book.Title}» برگ استفاده شده و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.");
        db.Remove(book);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<InstrumentLeafDto>> GetLeavesAsync(int bookId, CancellationToken ct)
    {
        var leaves = await db.InstrumentLeaves.AsNoTracking().Where(l => l.BookId == bookId).OrderBy(l => l.No).ToListAsync(ct);
        var ids = leaves.Select(l => l.Id).ToList();
        var cheques = await db.IssuedCheques.AsNoTracking().Where(c => c.LeafId != null && ids.Contains(c.LeafId.Value))
            .Join(db.PaymentLines, c => c.PaymentLineId, l => l.Id, (c, l) => new { LeafId = c.LeafId!.Value, l.PaymentId })
            .Join(db.Payments, x => x.PaymentId, p => p.Id, (x, p) => new { x.LeafId, p.Id, p.DocNo })
            .ToListAsync(ct);
        var guarantees = await db.Guarantees.AsNoTracking().Where(g => g.LeafId != null && ids.Contains(g.LeafId.Value))
            .Select(g => new { LeafId = g.LeafId!.Value, g.Id, g.DocNo }).ToListAsync(ct);

        return leaves.Select(l =>
        {
            string? usedIn = null, url = null;
            if (cheques.FirstOrDefault(c => c.LeafId == l.Id) is { } c) (usedIn, url) = ($"پرداخت {TreasuryText.No(c.DocNo)}", $"/treasury/payments/{c.Id}");
            else if (guarantees.FirstOrDefault(g => g.LeafId == l.Id) is { } g) (usedIn, url) = ($"سند تضمینی {TreasuryText.No(g.DocNo)}", $"/treasury/guarantees/{g.Id}");
            return new InstrumentLeafDto(l.Id, l.No, l.SerialNo, l.Status, Title(l.Status),
                l.VoidDate is { } d ? PersianDate.ToPersian(d) : null, l.VoidReason, usedIn, url);
        }).ToList();
    }

    public async Task VoidLeafAsync(long leafId, string date, string? reason, CancellationToken ct)
    {
        var leaf = await db.InstrumentLeaves.FirstOrDefaultAsync(l => l.Id == leafId, ct) ?? throw new NotFoundException("برگ پیدا نشد.");
        leaf.MarkVoid(PersianDate.Parse(date), reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task UndoVoidLeafAsync(long leafId, CancellationToken ct)
    {
        var leaf = await db.InstrumentLeaves.FirstOrDefaultAsync(l => l.Id == leafId, ct) ?? throw new NotFoundException("برگ پیدا نشد.");
        leaf.UndoVoid();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>برگ‌های سفید دفترچه‌های فعال برای انتخاب در فرم (به ترتیب دفترچه و شماره).</summary>
    public async Task<List<LeafOption>> GetBlankLeavesAsync(InstrumentKind kind, int? bankAccountId, CancellationToken ct)
    {
        var books = await db.InstrumentBooks.AsNoTracking()
            .Where(b => b.Kind == kind && b.IsActive && (bankAccountId == null || b.BankAccountId == bankAccountId))
            .OrderBy(b => b.ReceivedDate).ThenBy(b => b.Id).ToListAsync(ct);
        var ids = books.Select(b => b.Id).ToList();
        var leaves = await db.InstrumentLeaves.AsNoTracking().Where(l => ids.Contains(l.BookId) && l.Status == LeafStatus.Blank)
            .OrderBy(l => l.No).ToListAsync(ct);
        return books.SelectMany(b => leaves.Where(l => l.BookId == b.Id)
            .Select(l => new LeafOption(l.Id, b.Id, b.Title, l.SerialNo, b.BankAccountId, b.MaxAmount))).ToList();
    }

    /// <summary>یک برگ (حتی استفاده‌شده) برای نمایش در فرم موجود.</summary>
    public async Task<LeafOption?> GetLeafAsync(long leafId, CancellationToken ct)
    {
        var leaf = await db.InstrumentLeaves.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leafId, ct);
        if (leaf is null) return null;
        var b = await db.InstrumentBooks.AsNoTracking().FirstAsync(x => x.Id == leaf.BookId, ct);
        return new LeafOption(leaf.Id, b.Id, b.Title, leaf.SerialNo, b.BankAccountId, b.MaxAmount);
    }

    // -------------------------------------------------------------- ضمانت‌نامه بانکی

    public async Task<List<BankGuaranteeDto>> GetBankGuaranteesAsync(BankGuaranteeStatus? status, CancellationToken ct)
    {
        var query = db.BankGuarantees.AsNoTracking();
        if (status is not null) query = query.Where(g => g.Status == status);
        var rows = await query.OrderBy(g => g.Status).ThenByDescending(g => g.IssueDate).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var holders = await db.Guarantees.AsNoTracking()
            .Where(g => g.BankGuaranteeId != null && ids.Contains(g.BankGuaranteeId.Value) && !g.IsReturned)
            .Join(db.Persons, g => g.PersonId, p => p.Id, (g, p) => new { BgId = g.BankGuaranteeId!.Value, g.Id, p.DisplayName })
            .ToListAsync(ct);
        var banks = await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return rows.Select(g =>
        {
            var holder = holders.FirstOrDefault(h => h.BgId == g.Id);
            return new BankGuaranteeDto(g.Id, g.BankAccountId, banks.GetValueOrDefault(g.BankAccountId, "?"), g.Number, g.Amount,
                PersianDate.ToPersian(g.IssueDate), g.ExpiryDate is { } e ? PersianDate.ToPersian(e) : null, g.Subject, g.Status,
                Title(g.Status), g.CancelDate is { } c ? PersianDate.ToPersian(c) : null, g.Notes, holder?.DisplayName, holder?.Id,
                g.Status != BankGuaranteeStatus.Cancelled && g.ExpiryDate is { } x && x < today);
        }).ToList();
    }

    public async Task<int> SaveBankGuaranteeAsync(int? id, SaveBankGuaranteeRequest r, CancellationToken ct)
    {
        var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == r.BankAccountId, ct)
                   ?? throw new DomainException("حساب بانکی صادرکننده را انتخاب کنید.");
        var issue = PersianDate.Parse(r.IssueDate);
        DateOnly? expiry = string.IsNullOrWhiteSpace(r.ExpiryDate) ? null : PersianDate.Parse(r.ExpiryDate);
        BankGuarantee g;
        if (id is int existing)
        {
            g = await db.BankGuarantees.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("ضمانت‌نامه پیدا نشد.");
            g.Update(bank.Id, r.Number, r.Amount, issue, expiry, r.Subject, r.Notes);
        }
        else
        {
            g = new BankGuarantee(bank.Id, r.Number, r.Amount, issue, expiry, r.Subject, r.Notes);
            db.Add(g);
        }
        if (await db.BankGuarantees.AnyAsync(x => x.Id != g.Id && x.BankAccountId == g.BankAccountId && x.Number == g.Number, ct))
            throw new DomainException($"ضمانت‌نامه شماره {g.Number} از «{bank.Title}» قبلاً تعریف شده است.");
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task DeleteBankGuaranteeAsync(int id, CancellationToken ct)
    {
        var g = await db.BankGuarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ضمانت‌نامه پیدا نشد.");
        if (await db.Guarantees.AnyAsync(x => x.BankGuaranteeId == id, ct))
            throw new DomainException("این ضمانت‌نامه در سند تضمینی استفاده شده و حذف نمی‌شود؛ می‌توانید باطلش کنید.");
        db.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelBankGuaranteeAsync(int id, string date, CancellationToken ct)
    {
        var g = await db.BankGuarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ضمانت‌نامه پیدا نشد.");
        g.Cancel(PersianDate.Parse(date));
        await db.SaveChangesAsync(ct);
    }

    public async Task UndoCancelBankGuaranteeAsync(int id, CancellationToken ct)
    {
        var g = await db.BankGuarantees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ضمانت‌نامه پیدا نشد.");
        g.UndoCancel();
        await db.SaveChangesAsync(ct);
    }
}
