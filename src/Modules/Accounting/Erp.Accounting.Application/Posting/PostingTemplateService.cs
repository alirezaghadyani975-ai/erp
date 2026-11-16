using Erp.SharedKernel.Security;
using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.Posting;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Posting;

public sealed record PostingTemplateLineDto(
    int Id, string LineKey, string Title, PostingSide Side,
    int? AccountId, string? AccountCode, string? AccountTitle, string? AccountRule, string? RuleTitle,
    string? DetailHint, string? AccountDetails);

public sealed record PostingTemplateDto(int Id, string SourceDocType, string? SourceModule, string Title, bool IsActive,
    List<PostingTemplateLineDto> Lines);

/// <summary>نمایش و تنظیم الگوهای صدور سند (فقط معین ردیف‌های ثابت قابل تغییر است).</summary>
public class PostingTemplateService(IAccountingDbContext db, IAccessControl access)
{
    public async Task<List<PostingTemplateDto>> GetAllAsync(CancellationToken ct)
    {
        access.Demand(AccountingPermissions.TemplateView);
        var templates = await db.PostingTemplates.Include(t => t.Lines).AsNoTracking().OrderBy(t => t.Title).ToListAsync(ct);
        var accountIds = templates.SelectMany(t => t.Lines).Select(l => l.AccountId).OfType<int>().Distinct().ToList();
        var accounts = await db.Accounts.Include(a => a.DetailLevels).ThenInclude(l => l.AllowedTypes).AsNoTracking()
            .Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var typeTitles = await db.DetailTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Title, ct);

        string? Rule(int? accountId)
        {
            if (accountId is not int id || !accounts.TryGetValue(id, out var a)) return null;
            if (a.DetailLevels.Count == 0) return "بدون تفصیلی";
            return string.Join("، ", a.DetailLevels.OrderBy(l => l.LevelNo).Select(l =>
                $"سطح {l.LevelNo}: {string.Join(" یا ", l.AllowedTypes.Select(t => typeTitles.GetValueOrDefault(t.DetailTypeId, "?")))}"));
        }

        return templates.Select(t => new PostingTemplateDto(t.Id, t.SourceDocType, t.SourceModule, t.Title, t.IsActive,
            t.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).Select(l =>
            {
                var a = l.AccountId is int id ? accounts.GetValueOrDefault(id) : null;
                return new PostingTemplateLineDto(l.Id, l.LineKey, l.Title, l.Side, l.AccountId, a?.Code, a?.Title,
                    l.AccountRule, l.AccountRule is { } r ? AccountRules.Title(r) : null, l.DetailHint, Rule(l.AccountId));
            }).ToList())).ToList();
    }

    public async Task ChangeAccountAsync(int templateId, int lineId, string accountCode, CancellationToken ct)
    {
        access.Demand(AccountingPermissions.TemplateEdit);
        var template = await db.PostingTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == templateId, ct)
                       ?? throw new NotFoundException("الگوی سند پیدا نشد.");
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Code == accountCode.Trim(), ct)
                      ?? throw new DomainException($"حساب با کد {accountCode} وجود ندارد.");
        template.ChangeAccount(lineId, account);
        await db.SaveChangesAsync(ct);
    }
}
