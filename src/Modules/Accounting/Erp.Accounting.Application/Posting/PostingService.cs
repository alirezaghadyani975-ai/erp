using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Contracts;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Posting;
using Erp.Accounting.Domain.Vouchers;
using Erp.Costing.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Posting;

/// <summary>
/// موتور صدور سند خودکار. برای هر سند منبع یک سند موقت می‌سازد (یا سند قبلی‌اش را بازنویسی می‌کند):
/// معین و طرف هر ردیف از الگوی همان نوع سند، تفصیلی‌ها از داده‌های سند منبع به ترتیب قاعده تفصیلی معین.
/// همه کنترل‌های سند دستی (قاعده تفصیلی، ماهیت عامل، وظیفه مرکز، تراز، سال و دوره باز) این‌جا هم برقرار است.
/// </summary>
public class PostingService(IAccountingDbContext db, ICurrentUser user, TimeProvider clock, IUnitOfWork uow) : IVoucherPosting
{
    public async Task<PostedVoucher> PostAsync(PostingRequest request, CancellationToken ct)
    {
        var template = await db.PostingTemplates.Include(t => t.Lines).AsNoTracking()
                           .FirstOrDefaultAsync(t => t.SourceDocType == request.DocType, ct)
                       ?? throw new DomainException($"الگوی صدور سند «{request.DocType}» تعریف نشده است.");
        if (!template.IsActive)
            throw new DomainException($"الگوی صدور سند «{template.Title}» غیرفعال است.");

        var lines = request.Lines.Where(l => l.Amount != 0).ToList();
        if (lines.Count == 0)
            throw new DomainException("سند منبع مبلغی برای ثبت ندارد.");

        var fiscalYear = await FiscalYearOfAsync(request.Date, ct);
        var resolved = await ResolveLinesAsync(template, lines, ct);

        await using var tx = await uow.BeginAsync(ct);
        var voucher = await LiveVoucherAsync(request.SourceModule, request.DocType, request.DocumentId, tracking: true, ct);
        if (voucher is not null)
        {
            EnsureNotPermanent(voucher);
            if (voucher.FiscalYearId != fiscalYear.Id)
            {
                // سال مالی عوض شده: سند قبلی حذف و در سال جدید سند تازه
                db.Vouchers.Remove(voucher);
                await db.SaveChangesAsync(ct);
                voucher = null;
            }
        }

        if (voucher is null)
        {
            var tempNo = (await db.Vouchers.Where(v => v.FiscalYearId == fiscalYear.Id).MaxAsync(v => (int?)v.TempNo, ct) ?? 0) + 1;
            voucher = Voucher.Create(fiscalYear, tempNo, request.Date, request.Description, user.UserId,
                clock.GetUtcNow().UtcDateTime, VoucherType.Normal,
                new SourceDocument(request.SourceModule, request.DocType, request.DocumentId));
            db.Vouchers.Add(voucher);
        }
        else
        {
            if (voucher.Status == VoucherStatus.Temporary) voucher.ReturnToDraft();
            voucher.UpdateHeader(fiscalYear, request.Date, request.Description);
            voucher.ClearLines();
            await db.SaveChangesAsync(ct); // ردیف‌های قبلی پیش از درج ردیف‌های جدید حذف شوند
        }

        var row = 0;
        foreach (var line in resolved)
        {
            row++;
            try
            {
                voucher.AddLine(line.Account, line.Details, line.Debit, line.Credit,
                    new LineExtras(line.Description, FollowUpNo: line.FollowUpNo, FollowUpDate: line.FollowUpDate));
            }
            catch (DomainException ex)
            {
                throw new DomainException($"ردیف {row} سند ({line.Title}): {ex.Message}", ex.Code);
            }
        }
        await db.SaveChangesAsync(ct);   // پیش‌نویس با ردیف‌ها
        voucher.MarkTemporary();          // تراز و حداقل دو ردیف
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToPosted(voucher);
    }

    public async Task UnpostAsync(string sourceModule, string docType, long documentId, CancellationToken ct)
    {
        var voucher = await LiveVoucherAsync(sourceModule, docType, documentId, tracking: true, ct);
        if (voucher is null) return;
        EnsureNotPermanent(voucher);
        db.Vouchers.Remove(voucher);
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureChangeableAsync(string sourceModule, string docType, long documentId, CancellationToken ct)
    {
        var voucher = await LiveVoucherAsync(sourceModule, docType, documentId, tracking: false, ct);
        if (voucher is not null) EnsureNotPermanent(voucher);
    }

    public async Task<IReadOnlyDictionary<long, PostedVoucher>> FindAsync(string sourceModule, string docType,
        IReadOnlyCollection<long> documentIds, CancellationToken ct)
    {
        var ids = documentIds.ToList();
        var rows = await db.Vouchers.AsNoTracking()
            .Where(v => v.SourceModule == sourceModule && v.SourceDocType == docType && v.Status != VoucherStatus.Void
                        && v.SourceDocumentId != null && ids.Contains(v.SourceDocumentId.Value))
            .Select(v => new { DocId = v.SourceDocumentId!.Value, v.Id, v.FiscalYearId, v.TempNo, v.VoucherNo, v.Status })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.DocId, r => new PostedVoucher(r.Id, r.FiscalYearId, r.TempNo, r.VoucherNo, (byte)r.Status));
    }

    // ------------------------------------------------------------------

    private sealed record ResolvedLine(string Title, Account Account, IReadOnlyList<DetailAccount?> Details,
        decimal Debit, decimal Credit, string? Description, string? FollowUpNo, DateOnly? FollowUpDate);

    private async Task<List<ResolvedLine>> ResolveLinesAsync(PostingTemplate template, List<PostingLine> lines, CancellationToken ct)
    {
        var candidates = await LoadDetailsAsync(lines.SelectMany(l => l.Details).Distinct().ToList(), ct);

        var fixedIds = template.Lines.Where(l => l.AccountId is not null).Select(l => l.AccountId!.Value)
            .Concat(lines.Select(l => l.AccountId).OfType<int>()).Distinct().ToList();
        var accounts = await db.Accounts.Include(a => a.DetailLevels).ThenInclude(l => l.AllowedTypes).AsNoTracking()
            .Where(a => fixedIds.Contains(a.Id) || (a.Level == AccountLevel.Subsidiary && a.CostNature != null))
            .ToListAsync(ct);

        var result = new List<ResolvedLine>();
        foreach (var line in lines)
        {
            var tl = template.Line(line.LineKey);
            var details = line.Details.SelectMany(d => candidates.GetValueOrDefault(d) ?? []).ToList();

            var account = tl.AccountRule switch
            {
                null => accounts.FirstOrDefault(a => a.Id == tl.AccountId)
                        ?? throw new DomainException($"معین ردیف «{tl.Title}» در الگوی «{template.Title}» پیدا نشد."),
                AccountRules.Cost => CostAccount(accounts, details, tl),
                AccountRules.Source => line.AccountId is int sid
                    ? accounts.FirstOrDefault(a => a.Id == sid) ?? throw new DomainException($"معین ردیف «{tl.Title}» پیدا نشد.")
                    : throw new DomainException($"فرم برای ردیف «{tl.Title}» معین نداده است."),
                _ => throw new DomainException($"قاعده معین «{tl.AccountRule}» شناخته نشده است.")
            };

            var levels = FillLevels(account, details, tl);
            var amount = Math.Abs(line.Amount);
            var debitSide = (tl.Side == PostingSide.Debit) == (line.Amount > 0);
            result.Add(new ResolvedLine(tl.Title, account, levels, debitSide ? amount : 0, debitSide ? 0 : amount, line.Description,
                line.FollowUpNo, line.FollowUpDate));
        }
        return result;
    }

    /// <summary>تفصیلی‌های سند منبع (شخص، عامل، مرکز، تفصیلی مستقیم) ← حساب‌های تفصیلی ممکن.</summary>
    private async Task<Dictionary<PostingDetail, List<DetailAccount>>> LoadDetailsAsync(List<PostingDetail> refs, CancellationToken ct)
    {
        var result = new Dictionary<PostingDetail, List<DetailAccount>>();
        if (refs.Count == 0) return result;

        var personIds = refs.Where(r => r.Kind == PostingDetailKind.Person).Select(r => (int)r.Id).ToList();
        var factorIds = refs.Where(r => r.Kind == PostingDetailKind.CostFactor).Select(r => r.Id).ToList();
        var centerIds = refs.Where(r => r.Kind == PostingDetailKind.CostCenter).Select(r => r.Id).ToList();
        var directIds = refs.Where(r => r.Kind == PostingDetailKind.DetailAccount).Select(r => (int)r.Id).ToList();
        var treasuryIds = refs.Where(r => r.Kind is PostingDetailKind.CashBox or PostingDetailKind.BankAccount or PostingDetailKind.Imprest)
            .Select(r => r.Id).ToList();
        var dueTypes = db.DetailTypes.Where(t => t.SystemKey == DetailSystemKeys.DueMonth).Select(t => t.Id);
        var hasDue = refs.Any(r => r.Kind == PostingDetailKind.DueMonth);

        var factorTypes = db.DetailTypes.Where(t => t.OwnerModule == OwnerModules.CostFactors).Select(t => t.Id);
        var centerTypes = db.DetailTypes.Where(t => t.OwnerModule == OwnerModules.CostCenters).Select(t => t.Id);

        var rows = await db.DetailAccounts.AsNoTracking()
            .Where(d => (d.PersonId != null && personIds.Contains(d.PersonId.Value))
                        || directIds.Contains(d.Id)
                        || (hasDue && dueTypes.Contains(d.DetailTypeId))
                        || (d.SourceModule == TreasuryDetailLinks.SourceModule && d.SourceEntityId != null && treasuryIds.Contains(d.SourceEntityId.Value))
                        || (d.SourceModule == CostDetailLinks.SourceModule && d.SourceEntityId != null
                            && ((factorIds.Contains(d.SourceEntityId.Value) && factorTypes.Contains(d.DetailTypeId))
                                || (centerIds.Contains(d.SourceEntityId.Value) && centerTypes.Contains(d.DetailTypeId)))))
            .ToListAsync(ct);
        var types = await db.DetailTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => new { t.OwnerModule, t.SystemKey }, ct);
        var owners = types.ToDictionary(t => t.Key, t => t.Value.OwnerModule);
        bool Treasury(DetailAccount d, long id, string key) =>
            d.SourceModule == TreasuryDetailLinks.SourceModule && d.SourceEntityId == id && types[d.DetailTypeId].SystemKey == key;

        foreach (var r in refs)
        {
            var matches = r.Kind switch
            {
                PostingDetailKind.Person => rows.Where(d => d.PersonId == r.Id).ToList(),
                PostingDetailKind.CostFactor => rows.Where(d => d.SourceEntityId == r.Id && owners[d.DetailTypeId] == OwnerModules.CostFactors).ToList(),
                PostingDetailKind.CostCenter => rows.Where(d => d.SourceEntityId == r.Id && owners[d.DetailTypeId] == OwnerModules.CostCenters).ToList(),
                PostingDetailKind.CashBox => rows.Where(d => Treasury(d, r.Id, DetailSystemKeys.Cash)).ToList(),
                PostingDetailKind.BankAccount => rows.Where(d => Treasury(d, r.Id, DetailSystemKeys.Bank)).ToList(),
                PostingDetailKind.Imprest => rows.Where(d => Treasury(d, r.Id, DetailSystemKeys.Imprest)).ToList(),
                PostingDetailKind.DueMonth => rows.Where(d => types[d.DetailTypeId].SystemKey == DetailSystemKeys.DueMonth)
                    .OrderBy(d => d.Code).Skip((int)r.Id - 1).Take(1).ToList(),
                _ => rows.Where(d => d.Id == r.Id).ToList()
            };
            if (matches.Count == 0)
                throw new DomainException(r.Kind switch
                {
                    PostingDetailKind.Person => "شخص انتخاب‌شده تفصیلی حسابداری ندارد؛ به او نقش طرف‌حساب (مثلاً تأمین‌کننده) بدهید.",
                    PostingDetailKind.CostFactor => "عامل هزینه انتخاب‌شده تفصیلی حسابداری ندارد.",
                    PostingDetailKind.CostCenter => "مرکز هزینه انتخاب‌شده تفصیلی حسابداری ندارد.",
                    PostingDetailKind.CashBox => "صندوق انتخاب‌شده تفصیلی حسابداری ندارد.",
                    PostingDetailKind.BankAccount => "حساب بانکی انتخاب‌شده تفصیلی حسابداری ندارد.",
                    PostingDetailKind.Imprest => "تنخواه‌گردان انتخاب‌شده تفصیلی حسابداری ندارد.",
                    PostingDetailKind.DueMonth => "تفصیلی ماه سررسید پیدا نشد (گروه «ماه سررسید»).",
                    _ => "حساب تفصیلی پیدا نشد."
                });
            result[r] = matches;
        }
        return result;
    }

    /// <summary>معین هزینه: کل از گروه عامل، معین از وظیفه مرکز (هزینه مالی وظیفه ندارد).</summary>
    private static Account CostAccount(List<Account> accounts, List<DetailAccount> details, PostingTemplateLine tl)
    {
        var factor = details.FirstOrDefault(d => d.CostNature is not null)
                     ?? throw new DomainException($"ردیف «{tl.Title}» عامل هزینه ندارد.");
        var center = details.FirstOrDefault(d => d.CostFunction is not null);
        var nature = factor.CostNature!.Value;

        var candidates = accounts.Where(a => a.Level == AccountLevel.Subsidiary && a.IsActive && a.CostNature == nature).ToList();
        var account = (center?.CostFunction is { } function
                          ? candidates.Where(a => a.CostFunction == function)
                          : Enumerable.Empty<Account>())
                      .Concat(candidates.Where(a => a.CostFunction is null))
                      .OrderByDescending(a => a.IsStandard).ThenBy(a => a.Code)
                      .FirstOrDefault();
        if (account is not null) return account;

        if (center is null && candidates.Any(a => a.CostFunction is not null))
            throw new DomainException($"عامل «{factor.Title}» از گروه «{CostTitles.Of(nature)}» است و مرکز هزینه لازم دارد.");
        throw new DomainException(center?.CostFunction is { } f
            ? $"برای عامل «{factor.Title}» ({CostTitles.Of(nature)}) و مرکز «{center.Title}» ({CostTitles.Of(f)}) معین هزینه‌ای تعریف نشده است."
            : $"برای گروه هزینه «{CostTitles.Of(nature)}» معین هزینه‌ای تعریف نشده است.");
    }

    /// <summary>هر سطح قاعده معین با اولین تفصیلی سند منبع که آن سطح می‌پذیرد پر می‌شود.</summary>
    private static List<DetailAccount?> FillLevels(Account account, List<DetailAccount> details, PostingTemplateLine tl)
    {
        var levels = new List<DetailAccount?>();
        var used = new HashSet<int>();
        foreach (var level in account.DetailLevels.OrderBy(l => l.LevelNo))
        {
            var match = details.FirstOrDefault(d => !used.Contains(d.Id) && account.AcceptsDetail(level.LevelNo, d.DetailTypeId));
            if (match is null)
            {
                if (level.IsRequired)
                    throw new DomainException(
                        $"معین «{account.Code} - {account.Title}» (ردیف «{tl.Title}» الگو) در سطح {level.LevelNo} تفصیلی‌ای می‌خواهد که این فرم ندارد" +
                        (tl.DetailHint is { } hint ? $" (فرم فقط «{hint}» می‌دهد)" : "") + "؛ معین الگو یا قاعده تفصیلی معین را اصلاح کنید.");
                break;
            }
            used.Add(match.Id);
            levels.Add(match);
        }
        return levels;
    }

    private async Task<FiscalYear> FiscalYearOfAsync(DateOnly date, CancellationToken ct) =>
        await db.FiscalYears.Include(f => f.Periods).FirstOrDefaultAsync(f => f.StartDate <= date && f.EndDate >= date, ct)
        ?? throw new DomainException($"برای تاریخ {PersianDate.ToPersian(date)} سال مالی تعریف نشده است.");

    private Task<Voucher?> LiveVoucherAsync(string module, string docType, long documentId, bool tracking, CancellationToken ct)
    {
        var query = db.Vouchers.Include(v => v.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(v => v.SourceModule == module && v.SourceDocType == docType
                                              && v.SourceDocumentId == documentId && v.Status != VoucherStatus.Void, ct);
    }

    private static void EnsureNotPermanent(Voucher voucher)
    {
        if (voucher.Status == VoucherStatus.Permanent)
            throw new DomainException($"سند حسابداری شماره {voucher.VoucherNo} این فرم دائم شده است؛ فرم دیگر اصلاح یا حذف نمی‌شود (اصلاح فقط با سند اصلاحی).");
    }

    private static PostedVoucher ToPosted(Voucher v) => new(v.Id, v.FiscalYearId, v.TempNo, v.VoucherNo, (byte)v.Status);
}
