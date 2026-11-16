using Erp.SharedKernel.Security;
using Erp.Treasury.Domain;

namespace Erp.Treasury.Application;

/// <summary>کمک‌های کنترل دسترسی خزانه‌داری: «فقط فرم‌های خودش» و محدوده صندوق/بانک/تنخواه.</summary>
internal static class TreasuryAccess
{
    /// <summary>شناسه‌های مجاز به‌صورت List (برای ترجمه Contains در EF)؛ null = همه.</summary>
    public static List<int>? Ids(this IAccessControl access, ScopeKind kind) => access.Allowed(kind)?.ToList();

    public static IQueryable<T> Own<T>(this IQueryable<T> query, IAccessControl access) where T : TreasuryDocument
    {
        if (!access.OwnDocsOnly) return query;
        var userId = access.UserId;
        return query.Where(d => d.CreatedBy == userId);
    }

    /// <summary>فرم را خود کاربر ثبت کرده (اگر «فقط فرم‌های خودش» است).</summary>
    public static void DemandDoc(this IAccessControl access, TreasuryDocument doc) => access.DemandOwner(doc.CreatedBy);

    /// <summary>همه صندوق‌ها و حساب‌های بانکی ردیف‌ها مجازند.</summary>
    public static void DemandFunds(this IAccessControl access, IEnumerable<int?> cashBoxIds, IEnumerable<int?> bankAccountIds)
    {
        foreach (var id in cashBoxIds.OfType<int>().Distinct()) access.DemandScope(ScopeKind.CashBox, id);
        foreach (var id in bankAccountIds.OfType<int>().Distinct()) access.DemandScope(ScopeKind.BankAccount, id);
    }
}
