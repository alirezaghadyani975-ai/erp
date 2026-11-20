using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Vouchers;

/// <summary>
/// چاپ سند: یک سند (Ids) یا گروهی از اسناد یک سال با بازه تاریخ و شماره. اسناد باطل هرگز چاپ نمی‌شوند؛
/// پیش‌نویس فقط وقتی خواسته شود (با علامت «پیش‌نویس» در چاپ). بازه شماره روی شماره قطعی و در نبودش شماره موقت است.
/// </summary>
public sealed record VoucherPrintRequest(
    short FiscalYearId,
    List<long>? Ids = null,
    string? FromDate = null,
    string? ToDate = null,
    int? FromNo = null,
    int? ToNo = null,
    bool IncludeTemporary = true,
    bool IncludeDraft = false);

public sealed record VoucherPrintLine(
    short RowNo, string AccountCode, string AccountTitle, string GeneralCode, string GeneralTitle,
    string? Details, string? Description, decimal Debit, decimal Credit);

public sealed record VoucherPrintDto(
    long Id, int TempNo, int? VoucherNo, string Date, string? Description, VoucherType Type, VoucherStatus Status,
    string? SourceTitle, string FiscalYearTitle, decimal TotalDebit, decimal TotalCredit, List<VoucherPrintLine> Lines);

public class VoucherPrintService(IAccountingDbContext db, IAccessControl access)
{
    public const int MaxVouchers = 500;

    public async Task<List<VoucherPrintDto>> GetAsync(VoucherPrintRequest r, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.VoucherPrint);
        access.DemandScope(ScopeKind.FiscalYear, r.FiscalYearId);
        var year = await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == r.FiscalYearId, ct)
                   ?? throw new NotFoundException("سال مالی پیدا نشد.");

        var query = db.Vouchers.AsNoTracking().Where(v => v.FiscalYearId == r.FiscalYearId && v.Status != VoucherStatus.Void);
        if (access.OwnDocsOnly) query = query.Where(v => v.CreatedBy == access.UserId);
        if (r.Ids is { Count: > 0 } ids)
            query = query.Where(v => ids.Contains(v.Id));
        else
        {
            if (!r.IncludeDraft) query = query.Where(v => v.Status != VoucherStatus.Draft);
            if (!r.IncludeTemporary) query = query.Where(v => v.Status != VoucherStatus.Temporary);
            if (Date(r.FromDate, "از تاریخ") is DateOnly from) query = query.Where(v => v.VoucherDate >= from);
            if (Date(r.ToDate, "تا تاریخ") is DateOnly to) query = query.Where(v => v.VoucherDate <= to);
            if (r.FromNo is int fromNo) query = query.Where(v => (v.VoucherNo ?? v.TempNo) >= fromNo);
            if (r.ToNo is int toNo) query = query.Where(v => (v.VoucherNo ?? v.TempNo) <= toNo);
        }

        var count = await query.CountAsync(ct);
        if (count > MaxVouchers)
            throw new DomainException($"{count:#,0} سند در این بازه است؛ هر بار حداکثر {MaxVouchers} سند چاپ می‌شود. بازه را کوچک‌تر کنید.");

        var vouchers = await query.Include(v => v.Lines)
            .OrderBy(v => v.VoucherNo == null).ThenBy(v => v.VoucherNo).ThenBy(v => v.VoucherDate).ThenBy(v => v.TempNo)
            .ToListAsync(ct);
        if (r.Ids is { Count: > 0 } && vouchers.Count == 0) throw new NotFoundException("سند پیدا نشد یا قابل چاپ نیست (باطل).");

        var chart = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.ParentId, a.Code, a.Title, a.Level }).ToDictionaryAsync(a => a.Id, ct);
        var detailIds = vouchers.SelectMany(v => v.Lines).SelectMany(l => l.DetailIds).OfType<int>().Distinct().ToList();
        var details = await db.DetailAccounts.AsNoTracking().Where(d => detailIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => $"{d.Code} {d.Title}", ct);
        var sources = await db.PostingTemplates.AsNoTracking()
            .Select(t => new { t.SourceDocType, t.Title }).ToListAsync(ct);

        return vouchers.Select(v => new VoucherPrintDto(
            v.Id, v.TempNo, v.VoucherNo, PersianDate.ToPersian(v.VoucherDate), v.Description, v.Type, v.Status,
            v.SourceDocType is null ? null : sources.FirstOrDefault(s => s.SourceDocType == v.SourceDocType)?.Title ?? v.SourceDocType,
            year.Title, v.TotalDebit, v.TotalCredit,
            v.Lines.OrderBy(l => l.RowNo).Select(l =>
            {
                var account = chart[l.AccountId];
                var general = account.ParentId is int p && chart.TryGetValue(p, out var parent) ? parent : account;
                var lineDetails = l.DetailIds.OfType<int>().Select(id => details.GetValueOrDefault(id, "?")).ToList();
                return new VoucherPrintLine(l.RowNo, account.Code, account.Title, general.Code, general.Title,
                    lineDetails.Count == 0 ? null : string.Join(" / ", lineDetails), l.Description, l.Debit, l.Credit);
            }).ToList())).ToList();
    }

    private static DateOnly? Date(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return PersianDate.TryParse(PersianText.Normalize(value), out var d) ? d : throw new DomainException($"{label} نامعتبر است: {value}");
    }
}
