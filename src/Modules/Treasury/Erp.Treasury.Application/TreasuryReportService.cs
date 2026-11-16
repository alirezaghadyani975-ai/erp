using Erp.SharedKernel.Security;
using System.Globalization;
using Erp.Accounting.Contracts;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

/// <summary>فیلتر گزارش چک‌ها؛ تاریخ‌ها شمسی.</summary>
public sealed record ChequeFilter(List<byte>? Statuses = null, string? FromDue = null, string? ToDue = null, int? PersonId = null,
    int? BankAccountId = null, int? CashBoxId = null, string? Term = null, List<long>? Ids = null);

public sealed record ChequeEventDto(string Date, string Title, string? DocNo, string? Place, string? Url);

public sealed record FundBalanceDto(FundKind Kind, int Id, string Title, string? Code, bool IsActive, decimal Balance,
    decimal ChequesInBox, int ChequesInBoxCount);

public sealed record DueMonthDto(int Year, int Month, string Title, decimal Receivable, int ReceivableCount, decimal Payable,
    int PayableCount)
{
    public decimal Net => Receivable - Payable;
}

/// <summary>گزارش‌های خزانه‌داری: چک‌های دریافتنی و پرداختنی، کاردکس چک، موجودی صندوق و بانک، سررسید ماهانه.</summary>
public class TreasuryReportService(ITreasuryDbContext db, ITreasuryDetailLinks links, IAccessControl access)
{
    private static readonly string[] Months =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public Task<List<ChequeRowDto>> ReceivedChequesAsync(ChequeFilter f, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Reports);
        return ReceivedCoreAsync(f, ct);
    }

    public Task<List<ChequeRowDto>> IssuedChequesAsync(ChequeFilter f, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Reports);
        return IssuedCoreAsync(f, ct);
    }

    /// <summary>چک‌های دریافتنی (بدون کنترل مجوز گزارش؛ برای فرم عملیات چک هم). فقط چک‌های صندوق/حساب‌های مجاز.</summary>
    internal async Task<List<ChequeRowDto>> ReceivedCoreAsync(ChequeFilter f, CancellationToken ct)
    {
        var q = db.ReceivedCheques.AsNoTracking();
        if (access.Ids(ScopeKind.CashBox) is { } boxes) q = q.Where(c => c.CashBoxId == null || boxes.Contains(c.CashBoxId.Value));
        if (access.Ids(ScopeKind.BankAccount) is { } bankIds) q = q.Where(c => c.BankAccountId == null || bankIds.Contains(c.BankAccountId.Value));
        if (f.Ids is { } ids) q = q.Where(c => ids.Contains(c.Id));
        if (f.Statuses is { Count: > 0 } st) q = q.Where(c => st.Contains((byte)c.Status));
        if (!string.IsNullOrWhiteSpace(f.FromDue)) { var d = PersianDate.Parse(f.FromDue); q = q.Where(c => c.DueDate >= d); }
        if (!string.IsNullOrWhiteSpace(f.ToDue)) { var d = PersianDate.Parse(f.ToDue); q = q.Where(c => c.DueDate <= d); }
        if (f.PersonId is int p) q = q.Where(c => c.PersonId == p);
        if (f.BankAccountId is int b) q = q.Where(c => c.BankAccountId == b);
        if (f.CashBoxId is int cb) q = q.Where(c => c.CashBoxId == cb);

        var rows = await q.OrderBy(c => c.DueDate).ThenBy(c => c.SerialNo)
            .Select(c => new
            {
                Cheque = c,
                Receipt = db.ReceiptLines.Where(l => l.Id == c.ReceiptLineId)
                    .Select(l => new { l.ReceiptId, No = db.Receipts.Where(r => r.Id == l.ReceiptId).Select(r => r.DocNo).FirstOrDefault() })
                    .FirstOrDefault()
            }).ToListAsync(ct);
        var lookups = await LookupsAsync(ct);
        var result = rows.Select(r => new ChequeRowDto(r.Cheque.Id, ChequeSide.Receivable, r.Cheque.SerialNo, r.Cheque.SayadId,
            PersianDate.ToPersian(r.Cheque.DueDate), r.Cheque.Amount, r.Cheque.PersonId, lookups.Persons.GetValueOrDefault(r.Cheque.PersonId, "?"),
            (byte)r.Cheque.Status, ChequeTitles.Of(r.Cheque.Status),
            r.Cheque.BankId is short bank ? lookups.Banks.GetValueOrDefault(bank) : null,
            r.Cheque.BankAccountId is int ba ? lookups.BankAccounts.GetValueOrDefault(ba) : r.Cheque.CashBoxId is int box ? lookups.Boxes.GetValueOrDefault(box) : null,
            r.Receipt is null ? null : TreasuryText.No(r.Receipt.No), r.Receipt?.ReceiptId)).ToList();
        return Filter(result, f.Term);
    }

    internal async Task<List<ChequeRowDto>> IssuedCoreAsync(ChequeFilter f, CancellationToken ct)
    {
        var q = db.IssuedCheques.AsNoTracking();
        if (access.Ids(ScopeKind.BankAccount) is { } bankIds) q = q.Where(c => bankIds.Contains(c.BankAccountId));
        if (f.Ids is { } ids) q = q.Where(c => ids.Contains(c.Id));
        if (f.Statuses is { Count: > 0 } st) q = q.Where(c => st.Contains((byte)c.Status));
        if (!string.IsNullOrWhiteSpace(f.FromDue)) { var d = PersianDate.Parse(f.FromDue); q = q.Where(c => c.DueDate >= d); }
        if (!string.IsNullOrWhiteSpace(f.ToDue)) { var d = PersianDate.Parse(f.ToDue); q = q.Where(c => c.DueDate <= d); }
        if (f.PersonId is int p) q = q.Where(c => c.PersonId == p);
        if (f.BankAccountId is int b) q = q.Where(c => c.BankAccountId == b);

        var rows = await q.OrderBy(c => c.DueDate).ThenBy(c => c.SerialNo)
            .Select(c => new
            {
                Cheque = c,
                Payment = db.PaymentLines.Where(l => l.Id == c.PaymentLineId)
                    .Select(l => new { l.PaymentId, No = db.Payments.Where(x => x.Id == l.PaymentId).Select(x => x.DocNo).FirstOrDefault() })
                    .FirstOrDefault()
            }).ToListAsync(ct);
        var lookups = await LookupsAsync(ct);
        var result = rows.Select(r => new ChequeRowDto(r.Cheque.Id, ChequeSide.Payable, r.Cheque.SerialNo, r.Cheque.SayadId,
            PersianDate.ToPersian(r.Cheque.DueDate), r.Cheque.Amount, r.Cheque.PersonId, lookups.Persons.GetValueOrDefault(r.Cheque.PersonId, "?"),
            (byte)r.Cheque.Status, ChequeTitles.Of(r.Cheque.Status), null, lookups.BankAccounts.GetValueOrDefault(r.Cheque.BankAccountId),
            r.Payment is null ? null : TreasuryText.No(r.Payment.No), r.Payment?.PaymentId)).ToList();
        return Filter(result, f.Term);
    }

    /// <summary>کاردکس چک: ثبت، عملیات‌ها و خرج.</summary>
    public async Task<List<ChequeEventDto>> HistoryAsync(ChequeSide side, long id, CancellationToken ct)
    {
        access.DemandAny(TreasuryPermissions.Reports, TreasuryPermissions.View(side));
        var events = new List<(DateOnly Date, long Order, ChequeEventDto Event)>();
        var lookups = await LookupsAsync(ct);
        if (side == ChequeSide.Receivable)
        {
            var c = await db.ReceivedCheques.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("چک پیدا نشد.");
            var receipt = await (from l in db.ReceiptLines join r in db.Receipts on l.ReceiptId equals r.Id
                                 where l.Id == c.ReceiptLineId select new { r.Id, r.DocNo, r.DocDate, l.CashBoxId }).FirstAsync(ct);
            events.Add((receipt.DocDate, 0, new ChequeEventDto(PersianDate.ToPersian(receipt.DocDate), "دریافت (ثبت چک)",
                TreasuryText.No(receipt.DocNo), receipt.CashBoxId is int b ? lookups.Boxes.GetValueOrDefault(b) : null,
                $"/treasury/receipts/{receipt.Id}")));
            var ops = await (from l in db.ChequeOperationLines join o in db.ChequeOperations on l.OperationId equals o.Id
                             where l.ReceivedChequeId == id select o).ToListAsync(ct);
            events.AddRange(ops.Select(o => (o.DocDate, o.Id, new ChequeEventDto(PersianDate.ToPersian(o.DocDate), ChequeTitles.Of(o.Operation),
                TreasuryText.No(o.DocNo), o.BankAccountId is int ba ? lookups.BankAccounts.GetValueOrDefault(ba) : o.CashBoxId is int cb ? lookups.Boxes.GetValueOrDefault(cb) : null,
                $"/treasury/cheque-ops/{o.Id}"))));
            var spend = await (from l in db.PaymentLines join p in db.Payments on l.PaymentId equals p.Id
                               where l.ReceivedChequeId == id select p).FirstOrDefaultAsync(ct);
            if (spend is not null)
                events.Add((spend.DocDate, long.MaxValue, new ChequeEventDto(PersianDate.ToPersian(spend.DocDate), "خرج در پرداخت",
                    TreasuryText.No(spend.DocNo), lookups.Persons.GetValueOrDefault(spend.PersonId), $"/treasury/payments/{spend.Id}")));
        }
        else
        {
            var c = await db.IssuedCheques.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("چک پیدا نشد.");
            var payment = await (from l in db.PaymentLines join p in db.Payments on l.PaymentId equals p.Id
                                 where l.Id == c.PaymentLineId select p).FirstAsync(ct);
            events.Add((payment.DocDate, 0, new ChequeEventDto(PersianDate.ToPersian(payment.DocDate), "پرداخت (صدور چک)",
                TreasuryText.No(payment.DocNo), lookups.BankAccounts.GetValueOrDefault(c.BankAccountId), $"/treasury/payments/{payment.Id}")));
            var ops = await (from l in db.ChequeOperationLines join o in db.ChequeOperations on l.OperationId equals o.Id
                             where l.IssuedChequeId == id select o).ToListAsync(ct);
            events.AddRange(ops.Select(o => (o.DocDate, o.Id, new ChequeEventDto(PersianDate.ToPersian(o.DocDate), ChequeTitles.Of(o.Operation),
                TreasuryText.No(o.DocNo), null, $"/treasury/cheque-ops/{o.Id}"))));
        }
        return events.OrderBy(e => e.Date).ThenBy(e => e.Order).Select(e => e.Event).ToList();
    }

    /// <summary>مانده صندوق‌ها، حساب‌های بانکی و تنخواه‌ها تا تاریخ (از سندهای موقت و دائم) و چک‌های در صندوق.</summary>
    public async Task<List<FundBalanceDto>> BalancesAsync(string? asOf, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Reports);
        DateOnly? date = string.IsNullOrWhiteSpace(asOf) ? null : PersianDate.Parse(asOf);
        var result = new List<FundBalanceDto>();
        var cheques = await db.ReceivedCheques.AsNoTracking().Where(c => c.Status == ReceivedChequeStatus.InBox && c.CashBoxId != null)
            .GroupBy(c => c.CashBoxId!.Value).Select(g => new { Box = g.Key, Sum = g.Sum(x => x.Amount), Count = g.Count() })
            .ToDictionaryAsync(x => x.Box, ct);

        async Task Add<T>(FundKind kind, TreasuryDetailKind dk, IQueryable<T> source, Func<T, (int Id, string Title, bool Active)> map)
        {
            var codes = await links.GetCodesAsync(dk, ct);
            var balances = await links.GetBalancesAsync(dk, date, ct);
            foreach (var x in (await source.ToListAsync(ct)).Select(map))
            {
                var ch = kind == FundKind.CashBox ? cheques.GetValueOrDefault(x.Id) : null;
                result.Add(new FundBalanceDto(kind, x.Id, x.Title, codes.GetValueOrDefault(x.Id), x.Active, balances.GetValueOrDefault(x.Id),
                    ch?.Sum ?? 0, ch?.Count ?? 0));
            }
        }
        await Add(FundKind.CashBox, TreasuryDetailKind.CashBox, db.CashBoxes.AsNoTracking(), c => (c.Id, c.Title, c.IsActive));
        await Add(FundKind.BankAccount, TreasuryDetailKind.BankAccount, db.BankAccounts.AsNoTracking(), b => (b.Id, b.Title, b.IsActive));
        await Add(FundKind.Imprest, TreasuryDetailKind.Imprest, db.ImprestFunds.AsNoTracking(), f => (f.Id, f.Title, f.IsActive));
        return result.Where(r => access.CanUse(TreasuryScopes.Of(r.Kind), r.Id)).OrderBy(r => r.Kind).ThenBy(r => r.Code).ToList();
    }

    /// <summary>سررسید ماهانه چک‌های باز: دریافتنی (در صندوق و در جریان وصول) در برابر پرداختنی (پاس‌نشده).</summary>
    public async Task<List<DueMonthDto>> DueScheduleAsync(CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Reports);
        var received = await db.ReceivedCheques.AsNoTracking()
            .Where(c => c.Status == ReceivedChequeStatus.InBox || c.Status == ReceivedChequeStatus.InCollection)
            .Select(c => new { c.DueDate, c.Amount }).ToListAsync(ct);
        var issued = await db.IssuedCheques.AsNoTracking().Where(c => c.Status == IssuedChequeStatus.Issued)
            .Select(c => new { c.DueDate, c.Amount }).ToListAsync(ct);
        var cal = new PersianCalendar();
        (int, int) Ym(DateOnly d) { var dt = d.ToDateTime(TimeOnly.MinValue); return (cal.GetYear(dt), cal.GetMonth(dt)); }

        var keys = received.Select(r => Ym(r.DueDate)).Concat(issued.Select(i => Ym(i.DueDate))).Distinct().OrderBy(k => k).ToList();
        return keys.Select(k =>
        {
            var rec = received.Where(r => Ym(r.DueDate) == k).ToList();
            var pay = issued.Where(i => Ym(i.DueDate) == k).ToList();
            return new DueMonthDto(k.Item1, k.Item2, $"{Months[k.Item2 - 1]} {k.Item1}", rec.Sum(r => r.Amount), rec.Count,
                pay.Sum(p => p.Amount), pay.Count);
        }).ToList();
    }

    private static List<ChequeRowDto> Filter(List<ChequeRowDto> rows, string? term)
    {
        term = PersianText.NormalizeOrNull(term);
        return term is null ? rows : rows.Where(r => r.SerialNo.Contains(term) || r.PersonName.Contains(term)
                                                     || (r.SayadId ?? "").Contains(term) || (r.PlaceTitle ?? "").Contains(term)).ToList();
    }

    private async Task<(Dictionary<int, string> Persons, Dictionary<short, string> Banks, Dictionary<int, string> BankAccounts,
        Dictionary<int, string> Boxes)> LookupsAsync(CancellationToken ct) =>
        (await db.Persons.ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct),
         await db.Banks.ToDictionaryAsync(b => b.Id, b => b.Title, ct),
         await db.BankAccounts.ToDictionaryAsync(b => b.Id, b => b.Title, ct),
         await db.CashBoxes.ToDictionaryAsync(c => c.Id, c => c.Title, ct));
}
