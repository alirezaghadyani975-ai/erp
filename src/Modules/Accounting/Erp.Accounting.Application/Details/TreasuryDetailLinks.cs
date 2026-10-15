using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Contracts;
using Erp.Accounting.Domain.Details;
using Erp.Accounting.Domain.Vouchers;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Details;

/// <summary>
/// تفصیلی صندوق‌ها، حساب‌های بانکی و تنخواه‌گردان‌ها برای ماژول خزانه‌داری (SourceModule = treasury).
/// گروه هر نوع با کلید سیستمی (cash، bank، imprest) پیدا می‌شود.
/// </summary>
public class TreasuryDetailLinks(IAccountingDbContext db) : ITreasuryDetailLinks
{
    public const string SourceModule = "treasury";

    public static string KeyOf(TreasuryDetailKind kind) => kind switch
    {
        TreasuryDetailKind.CashBox => DetailSystemKeys.Cash,
        TreasuryDetailKind.BankAccount => DetailSystemKeys.Bank,
        _ => DetailSystemKeys.Imprest
    };

    private static string TitleOf(TreasuryDetailKind kind) => kind switch
    {
        TreasuryDetailKind.CashBox => "صندوق‌ها",
        TreasuryDetailKind.BankAccount => "حساب‌های بانکی",
        _ => "تنخواه‌گردان‌ها"
    };

    public async Task<string> SyncAsync(TreasuryDetailKind kind, int id, string title, bool isActive, CancellationToken ct)
    {
        var detail = await FindAsync(kind, id, ct);
        if (detail is null)
        {
            var key = KeyOf(kind);
            var type = await db.DetailTypes.Where(t => t.SystemKey == key).OrderByDescending(t => t.IsActive).FirstOrDefaultAsync(ct)
                       ?? throw new DomainException($"گروه تفصیلی «{TitleOf(kind)}» تعریف نشده است.");
            if (!type.IsActive)
                throw new DomainException($"گروه تفصیلی «{type.Title}» غیرفعال است.");
            var code = await DetailService.NextCodeAsync(db, type, ct);
            detail = new DetailAccount(type, code, title, null, SourceModule, id);
            db.DetailAccounts.Add(detail);
        }
        if (detail.Title != title) detail.Rename(title);
        if (isActive) detail.Activate(); else detail.Deactivate();
        await db.SaveChangesAsync(ct);
        return detail.Code;
    }

    public async Task<bool> IsUsedAsync(TreasuryDetailKind kind, int id, CancellationToken ct)
    {
        var detail = await FindAsync(kind, id, ct);
        return detail is not null && await UsedAsync(detail.Id, ct);
    }

    public async Task DeleteAsync(TreasuryDetailKind kind, int id, CancellationToken ct)
    {
        var detail = await FindAsync(kind, id, ct);
        if (detail is null) return;
        if (await UsedAsync(detail.Id, ct))
            throw new DomainException($"تفصیلی {detail.Code} در سند استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.DetailAccounts.Remove(detail);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, string>> GetCodesAsync(TreasuryDetailKind kind, CancellationToken ct)
    {
        var key = KeyOf(kind);
        return await (from d in db.DetailAccounts.AsNoTracking()
                      join t in db.DetailTypes on d.DetailTypeId equals t.Id
                      where t.SystemKey == key && d.SourceModule == SourceModule && d.SourceEntityId != null
                      select new { Id = (int)d.SourceEntityId!.Value, d.Code })
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsync(TreasuryDetailKind kind, DateOnly? asOf, CancellationToken ct)
    {
        // معین وجه نقد هر نوع همان معین الگوی دریافت (صندوق، بانک) یا انتقال وجه (تنخواه) است
        var (docType, lineKey) = kind switch
        {
            TreasuryDetailKind.CashBox => ("trs_receipt", "cash"),
            TreasuryDetailKind.BankAccount => ("trs_receipt", "bank"),
            _ => ("trs_transfer", "imprest")
        };
        var accountId = await db.PostingTemplates.Where(t => t.SourceDocType == docType)
            .SelectMany(t => t.Lines).Where(l => l.LineKey == lineKey).Select(l => l.AccountId).FirstOrDefaultAsync(ct);
        if (accountId is null) return new Dictionary<int, decimal>();

        var key = KeyOf(kind);
        var details = await (from d in db.DetailAccounts.AsNoTracking()
                             join t in db.DetailTypes on d.DetailTypeId equals t.Id
                             where t.SystemKey == key && d.SourceModule == SourceModule && d.SourceEntityId != null
                             select new { d.Id, Entity = (int)d.SourceEntityId!.Value }).ToListAsync(ct);
        var ids = details.Select(d => d.Id).ToList();

        var lines = from l in db.VoucherLines.AsNoTracking()
                    join v in db.Vouchers on l.VoucherId equals v.Id
                    where l.AccountId == accountId && l.Detail1Id != null && ids.Contains(l.Detail1Id.Value)
                          && (v.Status == VoucherStatus.Temporary || v.Status == VoucherStatus.Permanent)
                          && v.Type != VoucherType.Closing
                    select new { l.Detail1Id, v.VoucherDate, l.Debit, l.Credit };
        if (asOf is DateOnly date) lines = lines.Where(l => l.VoucherDate <= date);

        var sums = await lines.GroupBy(l => l.Detail1Id!.Value)
            .Select(g => new { DetailId = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
        var byDetail = sums.ToDictionary(s => s.DetailId, s => s.Balance);
        return details.ToDictionary(d => d.Entity, d => byDetail.GetValueOrDefault(d.Id));
    }

    private Task<DetailAccount?> FindAsync(TreasuryDetailKind kind, int id, CancellationToken ct)
    {
        var key = KeyOf(kind);
        var typeIds = db.DetailTypes.Where(t => t.SystemKey == key).Select(t => t.Id);
        return db.DetailAccounts.FirstOrDefaultAsync(d => d.SourceModule == SourceModule && d.SourceEntityId == id
                                                          && typeIds.Contains(d.DetailTypeId), ct);
    }

    private Task<bool> UsedAsync(int detailId, CancellationToken ct) =>
        db.VoucherLines.AnyAsync(l => l.Detail1Id == detailId || l.Detail2Id == detailId || l.Detail3Id == detailId
                                      || l.Detail4Id == detailId || l.Detail5Id == detailId || l.Detail6Id == detailId, ct);
}
