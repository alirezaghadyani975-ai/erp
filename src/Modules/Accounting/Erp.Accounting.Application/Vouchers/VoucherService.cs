using Erp.SharedKernel.Security;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Vouchers;

public class VoucherService(IAccountingDbContext db, ICurrentUser user, TimeProvider clock, IUnitOfWork uow, IAccessControl access)
{
    private const int NumberingRetries = 3;

    public async Task<List<VoucherSummaryDto>> ListAsync(short fiscalYearId, VoucherStatus? status, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherView);
        access.DemandScope(ScopeKind.FiscalYear, fiscalYearId);
        var query = db.Vouchers.AsNoTracking().Where(v => v.FiscalYearId == fiscalYearId);
        if (access.OwnDocsOnly) query = query.Where(v => v.CreatedBy == access.UserId);
        if (status is not null)
            query = query.Where(v => v.Status == status);

        var rows = await query.OrderBy(v => v.TempNo)
            .Select(v => new { v.Id, v.TempNo, v.VoucherNo, v.VoucherDate, v.Description, v.Type, v.Status,
                Total = v.Lines.Sum(l => l.Debit), v.SourceDocType })
            .ToListAsync(ct);

        return rows.Select(r => new VoucherSummaryDto(r.Id, r.TempNo, r.VoucherNo, PersianDate.ToPersian(r.VoucherDate),
            r.Description, r.Type, r.Status, r.Total, r.SourceDocType)).ToList();
    }

    public async Task<VoucherDto> GetAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherView);
        var v = await LoadAsync(id, ct, tracking: false);

        var accountIds = v.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.Code, a.Title), ct);

        var detailIds = v.Lines.SelectMany(l => l.DetailIds).OfType<int>().Distinct().ToList();
        var details = await db.DetailAccounts.AsNoTracking().Where(d => detailIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new DetailRefDto(d.Id, d.Code, d.Title), ct);

        var currencyIds = v.Lines.Select(l => l.CurrencyId).OfType<short>().Distinct().ToList();
        var currencies = await db.Currencies.AsNoTracking().Where(c => currencyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        return new VoucherDto(
            v.Id, v.FiscalYearId, v.TempNo, v.VoucherNo, PersianDate.ToPersian(v.VoucherDate), v.Description,
            v.Type, v.Status, v.SourceModule, v.SourceDocType, v.SourceDocumentId,
            v.TotalDebit, v.TotalCredit, v.IsBalanced,
            v.Lines.OrderBy(l => l.RowNo).Select(l => new VoucherLineDto(
                l.RowNo, l.AccountId, accounts[l.AccountId].Code, accounts[l.AccountId].Title,
                l.DetailIds.TakeWhile(d => d is not null).Select(d => details.GetValueOrDefault(d!.Value)).ToList(),
                l.Description, l.Debit, l.Credit,
                l.CurrencyId is short c ? currencies[c] : null, l.CurrencyAmount, l.ExchangeRate,
                l.Quantity, l.UnitId, l.FollowUpNo,
                l.FollowUpDate is DateOnly f ? PersianDate.ToPersian(f) : null)).ToList());
    }

    /// <summary>ثبت سند جدید (پیش‌نویس) با شماره موقت بعدی.</summary>
    public async Task<long> CreateAsync(SaveVoucherRequest request, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherCreate);
        var fiscalYear = await GetFiscalYearAsync(request.FiscalYearId, ct);
        var date = PersianDate.Parse(request.Date);

        for (var attempt = 1; ; attempt++)
        {
            var tempNo = await db.Vouchers.Where(v => v.FiscalYearId == fiscalYear.Id)
                .MaxAsync(v => (int?)v.TempNo, ct) ?? 0;

            var voucher = Voucher.Create(fiscalYear, tempNo + 1, date, request.Description, user.UserId,
                clock.GetUtcNow().UtcDateTime, request.Type);
            await AddLinesAsync(voucher, request.Lines, ct);
            db.Vouchers.Add(voucher);

            try
            {
                await db.SaveChangesAsync(ct);
                return voucher.Id;
            }
            catch (DbUpdateException ex) when (attempt < NumberingRetries && IsNumberConflict(ex))
            {
                // کاربر دیگری همزمان همین شماره را گرفته؛ دوباره با شماره بعدی
                db.Vouchers.Entry(voucher).State = EntityState.Detached;
                foreach (var line in voucher.Lines) db.VoucherLines.Entry(line).State = EntityState.Detached;
            }
        }
    }

    /// <summary>ویرایش سند پیش‌نویس (سربرگ و جایگزینی کامل ردیف‌ها).</summary>
    public async Task UpdateAsync(long id, SaveVoucherRequest request, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherEdit);
        var voucher = await LoadAsync(id, ct);
        await EnsureManualAsync(voucher, ct);
        var fiscalYear = await GetFiscalYearAsync(voucher.FiscalYearId, ct);

        await using var tx = await uow.BeginAsync(ct);
        voucher.UpdateHeader(fiscalYear, PersianDate.Parse(request.Date), request.Description);
        voucher.ClearLines();
        await db.SaveChangesAsync(ct); // ردیف‌های قبلی پیش از درج ردیف‌های جدید حذف شوند (شماره ردیف یکتاست)

        await AddLinesAsync(voucher, request.Lines, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>حذف سند؛ فقط پیش‌نویس. سند موقت باطل می‌شود و سند دائم هیچ‌وقت حذف نمی‌شود.</summary>
    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherDelete);
        var voucher = await LoadAsync(id, ct);
        await EnsureManualAsync(voucher, ct);
        if (voucher.Status != VoucherStatus.Draft)
            throw new DomainException("فقط سند پیش‌نویس حذف می‌شود؛ سند موقت را برگردانید به پیش‌نویس یا باطل کنید، سند دائم حذف نمی‌شود.");
        db.Vouchers.Remove(voucher);
        await db.SaveChangesAsync(ct);
    }

    public Task MarkTemporaryAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherTemporary);
        return ChangeAsync(id, v => v.MarkTemporary(), ct);
    }

    public Task ReturnToDraftAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherDraft);
        return ChangeAsync(id, v => v.ReturnToDraft(), ct, manualOnly: true);
    }

    public Task VoidAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherVoid);
        return ChangeAsync(id, v => v.Void(), ct, manualOnly: true);
    }

    /// <summary>دائم کردن سند با شماره قطعی بعدی (پیوسته و بدون جای خالی در سال مالی).</summary>
    public async Task<int> MarkPermanentAsync(long id, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherPermanent);
        for (var attempt = 1; ; attempt++)
        {
            var voucher = await LoadAsync(id, ct);
            var fiscalYear = await GetFiscalYearAsync(voucher.FiscalYearId, ct);
            var lastNo = await db.Vouchers.Where(v => v.FiscalYearId == voucher.FiscalYearId && v.VoucherNo != null)
                .MaxAsync(v => v.VoucherNo, ct) ?? 0;

            voucher.MarkPermanent(fiscalYear, lastNo + 1, user.UserId, clock.GetUtcNow().UtcDateTime);
            try
            {
                await db.SaveChangesAsync(ct);
                return lastNo + 1;
            }
            catch (DbUpdateException ex) when (attempt < NumberingRetries && IsNumberConflict(ex))
            {
                db.Vouchers.Entry(voucher).State = EntityState.Detached;
            }
        }
    }

    private async Task ChangeAsync(long id, Action<Voucher> change, CancellationToken ct, bool manualOnly = false)
    {
        var voucher = await LoadAsync(id, ct);
        if (manualOnly) await EnsureManualAsync(voucher, ct);
        change(voucher);
        await db.SaveChangesAsync(ct);
    }

    private async Task AddLinesAsync(Voucher voucher, List<VoucherLineRequest> lines, CancellationToken ct)
    {
        var accountCodes = lines.Select(l => l.AccountCode.Trim()).Distinct().ToList();
        var accounts = await db.Accounts.Include(a => a.DetailLevels).ThenInclude(l => l.AllowedTypes)
            .Where(a => accountCodes.Contains(a.Code))
            .ToDictionaryAsync(a => a.Code, ct);

        var detailCodes = lines.SelectMany(l => l.DetailCodes ?? []).OfType<string>().Select(c => c.Trim()).Distinct().ToList();
        var details = await db.DetailAccounts.Where(d => detailCodes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, ct);

        var currencyCodes = lines.Select(l => l.CurrencyCode).OfType<string>().Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        var currencies = await db.Currencies.Where(c => currencyCodes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.Id, ct);

        var row = 0;
        foreach (var line in lines)
        {
            row++;
            try
            {
                if (!accounts.TryGetValue(line.AccountCode.Trim(), out Account? account))
                    throw new DomainException($"حساب با کد {line.AccountCode} وجود ندارد.");

                var lineDetails = (line.DetailCodes ?? []).Select(code =>
                    string.IsNullOrWhiteSpace(code)
                        ? null
                        : details.GetValueOrDefault(code.Trim()) ?? throw new DomainException($"تفصیلی با کد {code} وجود ندارد."))
                    .ToList();

                CurrencyAmount? currency = null;
                if (!string.IsNullOrWhiteSpace(line.CurrencyCode))
                {
                    if (!currencies.TryGetValue(line.CurrencyCode.Trim().ToUpperInvariant(), out var currencyId))
                        throw new DomainException($"ارز {line.CurrencyCode} تعریف نشده است.");
                    currency = new CurrencyAmount(currencyId, line.CurrencyAmount ?? 0, line.ExchangeRate ?? 0);
                }

                LineQuantity? quantity = null;
                if (line.Quantity is not null || line.UnitId is not null)
                {
                    if (line.Quantity is null || line.UnitId is null)
                        throw new DomainException("مقدار و واحد با هم لازم‌اند.");
                    quantity = new LineQuantity(line.Quantity.Value, line.UnitId.Value);
                }

                var extras = new LineExtras(line.Description, currency, quantity, line.FollowUpNo,
                    string.IsNullOrWhiteSpace(line.FollowUpDate) ? null : PersianDate.Parse(line.FollowUpDate));

                voucher.AddLine(account, lineDetails, line.Debit, line.Credit, extras);
            }
            catch (DomainException ex)
            {
                throw new DomainException($"ردیف {row}: {ex.Message}", ex.Code);
            }
        }
    }

    /// <summary>سندی که زیرسیستم صادر کرده فقط از همان فرم اصلاح یا حذف می‌شود.</summary>
    private async Task EnsureManualAsync(Voucher voucher, CancellationToken ct)
    {
        if (!voucher.IsSystemGenerated) return;
        var source = await db.PostingTemplates.Where(t => t.SourceDocType == voucher.SourceDocType)
            .Select(t => t.Title).FirstOrDefaultAsync(ct) ?? voucher.SourceDocType;
        throw new DomainException($"این سند خودکار از «{source}» صادر شده است؛ اصلاح و حذفش فقط از همان فرم ممکن است.");
    }

    private async Task<FiscalYear> GetFiscalYearAsync(short id, CancellationToken ct)
    {
        access.DemandScope(ScopeKind.FiscalYear, id);
        return await db.FiscalYears.Include(f => f.Periods).FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException($"سال مالی {id} پیدا نشد.");
    }

    private async Task<Voucher> LoadAsync(long id, CancellationToken ct, bool tracking = true)
    {
        var query = db.Vouchers.Include(v => v.Lines).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        var voucher = await query.FirstOrDefaultAsync(v => v.Id == id, ct)
               ?? throw new NotFoundException($"سند با شناسه {id} پیدا نشد.");
        access.DemandScope(ScopeKind.FiscalYear, voucher.FiscalYearId);
        access.DemandOwner(voucher.CreatedBy);
        return voucher;
    }

    private static bool IsNumberConflict(DbUpdateException ex) =>
        ex.InnerException?.Message is { } m && (m.Contains("UQ_Vouchers_YearTempNo") || m.Contains("UX_Vouchers_No"));
}
