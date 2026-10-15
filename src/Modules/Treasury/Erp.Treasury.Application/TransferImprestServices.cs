using Erp.Accounting.Contracts;
using Erp.Costing.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Text;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

public sealed record SaveTransferRequest(string Date, FundKind FromKind, int FromId, FundKind ToKind, int ToId, decimal Amount,
    decimal Fee, int? FeeFactorId, int? FeeCenterId, string? RefNo, string? Description);

public sealed record TransferDto(long Id, int DocNo, string Date, FundKind FromKind, int FromId, string FromTitle, FundKind ToKind,
    int ToId, string ToTitle, decimal Amount, decimal Fee, int? FeeFactorId, int? FeeCenterId, string? RefNo, string? Description,
    TreasuryVoucherDto? Voucher);

/// <summary>انتقال وجه بین صندوق، حساب بانکی و تنخواه‌گردان (مثلاً شارژ تنخواه یا واریز صندوق به بانک).</summary>
public class TransferService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock)
{
    public async Task<List<TransferDto>> ListAsync(string? fromDate, string? toDate, CancellationToken ct)
    {
        var query = db.Transfers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }
        var rows = await query.OrderByDescending(t => t.DocDate).ThenByDescending(t => t.DocNo).ToListAsync(ct);
        var titles = await FundTitlesAsync(ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Transfer.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(t => ToDto(t, titles, vouchers.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TransferDto> GetAsync(long id, CancellationToken ct)
    {
        var t = await db.Transfers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("انتقال وجه پیدا نشد.");
        var vouchers = await posting.FindAsync(TreasuryModule.Name, Transfer.DocType, [id], ct);
        return ToDto(t, await FundTitlesAsync(ct), vouchers.GetValueOrDefault(id));
    }

    public async Task<long> CreateAsync(SaveTransferRequest r, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.Transfers.MaxAsync(t => (int?)t.DocNo, ct) ?? 0) + 1;
        var transfer = new Transfer(no, user.UserId, clock.GetUtcNow().UtcDateTime);
        await ApplyAsync(transfer, r, ct);
        db.Add(transfer);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(await PostingRequestAsync(transfer, ct), ct);
        await tx.CommitAsync(ct);
        return transfer.Id;
    }

    public async Task UpdateAsync(long id, SaveTransferRequest r, CancellationToken ct)
    {
        await posting.EnsureChangeableAsync(TreasuryModule.Name, Transfer.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var transfer = await db.Transfers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("انتقال وجه پیدا نشد.");
        await ApplyAsync(transfer, r, ct);
        transfer.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(await PostingRequestAsync(transfer, ct), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var transfer = await db.Transfers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("انتقال وجه پیدا نشد.");
        await posting.UnpostAsync(TreasuryModule.Name, Transfer.DocType, id, ct);
        db.Remove(transfer);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ApplyAsync(Transfer t, SaveTransferRequest r, CancellationToken ct)
    {
        var titles = await FundTitlesAsync(ct, activeOnly: true);
        if (!titles.ContainsKey((r.FromKind, r.FromId))) throw new DomainException("مبدأ انتخاب نشده یا غیرفعال است.");
        if (!titles.ContainsKey((r.ToKind, r.ToId))) throw new DomainException("مقصد انتخاب نشده یا غیرفعال است.");
        int? center = r.FeeCenterId;
        if (r.Fee > 0 && r.FeeFactorId is int f)
        {
            var factor = await db.Factors.FirstOrDefaultAsync(x => x.Id == f, ct) ?? throw new DomainException("عامل هزینه کارمزد پیدا نشد.");
            if (factor.Nature == CostNature.Finance) center = null;
            else if (center is null) throw new DomainException($"برای عامل «{factor.Title}» مرکز هزینه کارمزد را انتخاب کنید.");
        }
        t.Set(PersianDate.Parse(r.Date), r.FromKind, r.FromId, r.ToKind, r.ToId, r.Amount, r.Fee, r.FeeFactorId, center, r.RefNo, r.Description);
    }

    private async Task<PostingRequest> PostingRequestAsync(Transfer t, CancellationToken ct)
    {
        var titles = await FundTitlesAsync(ct);
        var from = titles[(t.FromKind, t.FromId)];
        var to = titles[(t.ToKind, t.ToId)];
        var lines = new List<PostingLine>
        {
            new(Key(t.ToKind), t.Amount, [Detail(t.ToKind, t.ToId)], $"انتقال از {from}", FollowUpNo: t.RefNo),
            new(Key(t.FromKind), -(t.Amount + t.Fee), [Detail(t.FromKind, t.FromId)], $"انتقال به {to}", FollowUpNo: t.RefNo)
        };
        if (t.Fee > 0)
            lines.Add(new PostingLine("fee", t.Fee, t.FeeCenterId is int fc
                ? [new PostingDetail(PostingDetailKind.CostFactor, t.FeeFactorId!.Value), new PostingDetail(PostingDetailKind.CostCenter, fc)]
                : [new PostingDetail(PostingDetailKind.CostFactor, t.FeeFactorId!.Value)], "کارمزد انتقال"));
        var description = TreasuryText.Clip($"انتقال وجه {TreasuryText.No(t.DocNo)} از {from} به {to}" + (t.Description is { } d ? $" - {d}" : ""));
        return new PostingRequest(TreasuryModule.Name, Transfer.DocType, t.Id, t.DocDate, description, lines);
    }

    internal static string Key(FundKind kind) => kind switch
    {
        FundKind.CashBox => "cash",
        FundKind.BankAccount => "bank",
        _ => "imprest"
    };

    internal static PostingDetail Detail(FundKind kind, int id) => new(kind switch
    {
        FundKind.CashBox => PostingDetailKind.CashBox,
        FundKind.BankAccount => PostingDetailKind.BankAccount,
        _ => PostingDetailKind.Imprest
    }, id);

    private async Task<Dictionary<(FundKind, int), string>> FundTitlesAsync(CancellationToken ct, bool activeOnly = false)
    {
        var result = new Dictionary<(FundKind, int), string>();
        foreach (var c in await db.CashBoxes.AsNoTracking().Where(x => !activeOnly || x.IsActive).ToListAsync(ct))
            result[(FundKind.CashBox, c.Id)] = c.Title;
        foreach (var b in await db.BankAccounts.AsNoTracking().Where(x => !activeOnly || x.IsActive).ToListAsync(ct))
            result[(FundKind.BankAccount, b.Id)] = b.Title;
        foreach (var f in await db.ImprestFunds.AsNoTracking().Where(x => !activeOnly || x.IsActive).ToListAsync(ct))
            result[(FundKind.Imprest, f.Id)] = f.Title;
        return result;
    }

    private static TransferDto ToDto(Transfer t, Dictionary<(FundKind, int), string> titles, PostedVoucher? v) =>
        new(t.Id, t.DocNo, PersianDate.ToPersian(t.DocDate), t.FromKind, t.FromId, titles.GetValueOrDefault((t.FromKind, t.FromId), "?"),
            t.ToKind, t.ToId, titles.GetValueOrDefault((t.ToKind, t.ToId), "?"), t.Amount, t.Fee, t.FeeFactorId, t.FeeCenterId, t.RefNo, t.Description,
            TreasuryText.Voucher(v));
}

/// <summary>ردیف صورت‌هزینه: هزینه (عامل + مرکز + ارزش افزوده) یا پرداخت به طرف حساب (نوع پرداخت + شخص).</summary>
public sealed record ImprestLineRequest(int? CostFactorId, int? CostCenterId, decimal Amount, string? Description = null,
    decimal VatAmount = 0, ImprestLineKind Kind = ImprestLineKind.Expense, short? TypeId = null, int? PersonId = null);

public sealed record SaveImprestSettlementRequest(string Date, int ImprestFundId, string? Description, List<ImprestLineRequest> Lines);

public sealed record ImprestLineDto(short RowNo, ImprestLineKind Kind, int? CostFactorId, string? FactorTitle, int? CostCenterId,
    string? CenterTitle, short? TypeId, string? TypeTitle, int? PersonId, string? PersonName, decimal Amount, decimal VatAmount,
    string? Description);

public sealed record ImprestSettlementDto(long Id, int DocNo, string Date, int ImprestFundId, string FundTitle, decimal VatAmount,
    string? Description, decimal LinesTotal, decimal Total, List<ImprestLineDto> Lines, TreasuryVoucherDto? Voucher);

public sealed record ImprestSettlementListItemDto(long Id, int DocNo, string Date, string FundTitle, decimal Total, string? Description,
    TreasuryVoucherDto? Voucher);

/// <summary>
/// صورت‌هزینه تنخواه: پرداخت‌های تنخواه‌دار — هزینه (عامل + مرکز) یا پرداخت به طرف حساب (مثلاً بدهی یا مساعده)؛
/// بستانکار تنخواه‌گردان.
/// </summary>
public class ImprestSettlementService(ITreasuryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock)
{
    public async Task<List<ImprestSettlementListItemDto>> ListAsync(string? fromDate, string? toDate, CancellationToken ct)
    {
        var query = db.ImprestSettlements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fromDate)) { var f = PersianDate.Parse(fromDate); query = query.Where(r => r.DocDate >= f); }
        if (!string.IsNullOrWhiteSpace(toDate)) { var t = PersianDate.Parse(toDate); query = query.Where(r => r.DocDate <= t); }
        var rows = await query.OrderByDescending(s => s.DocDate).ThenByDescending(s => s.DocNo)
            .Select(s => new
            {
                s.Id, s.DocNo, s.DocDate, s.Description, s.VatAmount,
                Fund = db.ImprestFunds.Where(f => f.Id == s.ImprestFundId).Select(f => f.Title).FirstOrDefault(),
                Lines = s.Lines.Sum(l => (decimal?)l.Amount) ?? 0
            }).ToListAsync(ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, ImprestSettlement.DocType, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new ImprestSettlementListItemDto(r.Id, r.DocNo, PersianDate.ToPersian(r.DocDate), r.Fund ?? "?",
            r.Lines + r.VatAmount, r.Description, TreasuryText.Voucher(vouchers.GetValueOrDefault(r.Id)))).ToList();
    }

    public async Task<ImprestSettlementDto> GetAsync(long id, CancellationToken ct)
    {
        var s = await LoadAsync(id, false, ct);
        var factorIds = s.Lines.Select(l => l.CostFactorId).OfType<int>().ToList();
        var centerIds = s.Lines.Select(l => l.CostCenterId).OfType<int>().ToList();
        var typeIds = s.Lines.Select(l => l.TypeId).OfType<short>().ToList();
        var personIds = s.Lines.Select(l => l.PersonId).OfType<int>().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var types = await db.TransactionTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var persons = await db.Persons.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var fund = await db.ImprestFunds.Where(f => f.Id == s.ImprestFundId).Select(f => f.Title).FirstOrDefaultAsync(ct);
        var vouchers = await posting.FindAsync(TreasuryModule.Name, ImprestSettlement.DocType, [id], ct);
        return new ImprestSettlementDto(s.Id, s.DocNo, PersianDate.ToPersian(s.DocDate), s.ImprestFundId, fund ?? "?", s.VatAmount,
            s.Description, s.LinesTotal, s.Total,
            s.Lines.OrderBy(l => l.RowNo).Select(l => new ImprestLineDto(l.RowNo, l.LineKind,
                l.CostFactorId, l.CostFactorId is int f ? factors.GetValueOrDefault(f, "?") : null,
                l.CostCenterId, l.CostCenterId is int c ? centers.GetValueOrDefault(c) : null,
                l.TypeId, l.TypeId is short t ? types.GetValueOrDefault(t) : null,
                l.PersonId, l.PersonId is int p ? persons.GetValueOrDefault(p) : null,
                l.Amount, l.VatAmount, l.Description)).ToList(),
            TreasuryText.Voucher(vouchers.GetValueOrDefault(id)));
    }

    public async Task<long> CreateAsync(SaveImprestSettlementRequest r, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var no = (await db.ImprestSettlements.MaxAsync(s => (int?)s.DocNo, ct) ?? 0) + 1;
        var s = new ImprestSettlement(no, user.UserId, clock.GetUtcNow().UtcDateTime);
        var fund = await ApplyAsync(s, r, ct);
        db.Add(s);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(await PostingRequestAsync(s, fund, ct), ct);
        await tx.CommitAsync(ct);
        return s.Id;
    }

    public async Task UpdateAsync(long id, SaveImprestSettlementRequest r, CancellationToken ct)
    {
        await posting.EnsureChangeableAsync(TreasuryModule.Name, ImprestSettlement.DocType, id, ct);
        await using var tx = await uow.BeginAsync(ct);
        var s = await LoadAsync(id, true, ct);
        s.ClearLines();
        await db.SaveChangesAsync(ct);
        var fund = await ApplyAsync(s, r, ct);
        s.Touch(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await posting.PostAsync(await PostingRequestAsync(s, fund, ct), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        var s = await LoadAsync(id, true, ct);
        await posting.UnpostAsync(TreasuryModule.Name, ImprestSettlement.DocType, id, ct);
        db.Remove(s);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<ImprestFund> ApplyAsync(ImprestSettlement s, SaveImprestSettlementRequest r, CancellationToken ct)
    {
        var fund = await db.ImprestFunds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == r.ImprestFundId, ct)
                   ?? throw new DomainException("تنخواه‌گردان را انتخاب کنید.");
        if (!fund.IsActive && fund.Id != s.ImprestFundId) throw new DomainException($"تنخواه‌گردان «{fund.Title}» غیرفعال است.");
        var factorIds = r.Lines.Select(l => l.CostFactorId).OfType<int>().Distinct().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var typeIds = r.Lines.Select(l => l.TypeId).OfType<short>().Distinct().ToList();
        var types = await db.TransactionTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var personIds = r.Lines.Select(l => l.PersonId).OfType<int>().Distinct().ToList();
        var persons = await db.Persons.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var inputs = new List<ImprestLineInput>();
        var row = 0;
        foreach (var l in r.Lines)
        {
            row++;
            if (l.Kind == ImprestLineKind.Payment)
            {
                var type = l.TypeId is short tid ? types.GetValueOrDefault(tid) : null;
                if (type is null) throw new DomainException($"ردیف {row}: نوع پرداخت را انتخاب کنید.");
                if (type.Kind != TransactionKind.Payment) throw new DomainException($"ردیف {row}: «{type.Title}» نوع دریافت است، نه پرداخت.");
                if (l.PersonId is not int pid || !persons.ContainsKey(pid)) throw new DomainException($"ردیف {row}: طرف حساب را انتخاب کنید.");
                inputs.Add(new ImprestLineInput(l.Kind, null, null, type.Id, pid, type.AccountId, l.Amount, 0, l.Description));
            }
            else
            {
                var f = l.CostFactorId is int fid ? factors.GetValueOrDefault(fid) : null;
                if (f is null) throw new DomainException($"ردیف {row}: عامل هزینه را انتخاب کنید.");
                if (f.Nature != CostNature.Finance && l.CostCenterId is null)
                    throw new DomainException($"ردیف {row}: مرکز هزینه برای «{f.Title}» الزامی است.");
                inputs.Add(new ImprestLineInput(ImprestLineKind.Expense, f.Id, f.Nature == CostNature.Finance ? null : l.CostCenterId,
                    null, null, null, l.Amount, l.VatAmount, l.Description));
            }
        }
        s.SetHeader(PersianDate.Parse(r.Date), fund.Id, r.Description);
        s.SetLines(inputs);
        return fund;
    }

    private async Task<PostingRequest> PostingRequestAsync(ImprestSettlement s, ImprestFund fund, CancellationToken ct)
    {
        var factorIds = s.Lines.Select(l => l.CostFactorId).OfType<int>().ToList();
        var factors = await db.Factors.Where(f => factorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Title, ct);
        var typeIds = s.Lines.Select(l => l.TypeId).OfType<short>().ToList();
        var types = await db.TransactionTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var personIds = s.Lines.Select(l => l.PersonId).OfType<int>().ToList();
        var persons = await db.Persons.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        var lines = s.Lines.OrderBy(l => l.RowNo).Select(l => l.LineKind == ImprestLineKind.Payment
            ? new PostingLine("counter", l.Amount, [new PostingDetail(PostingDetailKind.Person, l.PersonId!.Value)],
                l.Description ?? $"{types.GetValueOrDefault(l.TypeId!.Value)} - {persons.GetValueOrDefault(l.PersonId.Value)}",
                AccountId: l.AccountId)
            : new PostingLine("expense", l.Amount,
                l.CostCenterId is int c
                    ? [new PostingDetail(PostingDetailKind.CostFactor, l.CostFactorId!.Value), new PostingDetail(PostingDetailKind.CostCenter, c)]
                    : [new PostingDetail(PostingDetailKind.CostFactor, l.CostFactorId!.Value)],
                l.Description ?? factors.GetValueOrDefault(l.CostFactorId.Value))).ToList();
        if (s.VatAmount > 0) lines.Add(new PostingLine("vat", s.VatAmount, [], $"ارزش افزوده صورت‌هزینه {TreasuryText.No(s.DocNo)}"));
        lines.Add(new PostingLine("imprest", s.Total, [new PostingDetail(PostingDetailKind.Imprest, fund.Id)], $"صورت‌هزینه {fund.Title}"));
        var description = TreasuryText.Clip($"صورت‌هزینه تنخواه {TreasuryText.No(s.DocNo)} - {fund.Title}" + (s.Description is { } d ? $" - {d}" : ""));
        return new PostingRequest(TreasuryModule.Name, ImprestSettlement.DocType, s.Id, s.DocDate, description, lines);
    }

    private async Task<ImprestSettlement> LoadAsync(long id, bool tracking, CancellationToken ct)
    {
        var query = db.ImprestSettlements.Include(s => s.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("صورت‌هزینه تنخواه پیدا نشد.");
    }
}
