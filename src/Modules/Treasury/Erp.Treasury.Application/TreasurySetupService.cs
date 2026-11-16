using Erp.SharedKernel.Security;
using Erp.Accounting.Contracts;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Domain;
using Erp.Treasury.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Treasury.Application;

public sealed record CashBoxDto(int Id, string? Code, string Title, bool IsActive, string? Notes, decimal Balance);
public sealed record SaveCashBoxRequest(string Title, bool IsActive, string? Notes);

public sealed record TreasuryBankAccountDto(int Id, string? Code, string Title, short? BankId, string? BankTitle, string? Branch,
    string? AccountNo, string? Iban, BankAccountKind AccountKind, bool IsActive, string? Notes, decimal Balance);
public sealed record SaveTreasuryBankAccountRequest(string Title, short? BankId, string? Branch, string? AccountNo, string? Iban,
    BankAccountKind AccountKind, bool IsActive, string? Notes);

public sealed record ImprestFundDto(int Id, string? Code, string Title, int? HolderPersonId, string? HolderName, bool IsActive,
    string? Notes, decimal Balance);
public sealed record SaveImprestFundRequest(string Title, int? HolderPersonId, bool IsActive, string? Notes);

public sealed record TransactionTypeDto(short Id, TransactionKind Kind, string Title, int AccountId, string AccountCode,
    string AccountTitle, int SortOrder, bool IsActive);
public sealed record SaveTransactionTypeRequest(TransactionKind Kind, string Title, string AccountCode, int SortOrder, bool IsActive);

/// <summary>یک صندوق، حساب بانکی یا تنخواه برای انتخاب در فرم‌ها.</summary>
public sealed record FundOption(FundKind Kind, int Id, string Title, string? Code, bool IsActive)
{
    public string Display => $"{Title}{(Code is null ? "" : $" ({Code})")}";
}

/// <summary>نوع محدوده داده هر نوع وجه نقد.</summary>
public static class TreasuryScopes
{
    public static ScopeKind Of(FundKind kind) => kind switch
    {
        FundKind.CashBox => ScopeKind.CashBox,
        FundKind.BankAccount => ScopeKind.BankAccount,
        _ => ScopeKind.ImprestFund,
    };

    /// <summary>کنترل دسترسی به صندوق/حساب/تنخواه (null = بدون وجه نقد).</summary>
    public static void Demand(IAccessControl access, FundKind kind, int? id)
    {
        if (id is int value) access.DemandScope(Of(kind), value);
    }
}

/// <summary>
/// تعاریف خزانه‌داری: صندوق‌ها، حساب‌های بانکی، تنخواه‌گردان‌ها و انواع دریافت و پرداخت.
/// تفصیلی هر صندوق/حساب/تنخواه در همان تراکنش در حسابداری ساخته و هماهنگ می‌شود.
/// </summary>
public class TreasurySetupService(ITreasuryDbContext db, ITreasuryDetailLinks links, IUnitOfWork uow, IAccessControl access)
{
    // ------------------------------------------------------------ صندوق
    public async Task<List<CashBoxDto>> GetCashBoxesAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = await db.CashBoxes.AsNoTracking().Where(c => includeInactive || c.IsActive).OrderBy(c => c.Title).ToListAsync(ct);
        var codes = await links.GetCodesAsync(TreasuryDetailKind.CashBox, ct);
        var balances = await links.GetBalancesAsync(TreasuryDetailKind.CashBox, null, ct);
        rows = rows.Where(c => access.CanUse(ScopeKind.CashBox, c.Id)).ToList();
        return rows.Select(c => new CashBoxDto(c.Id, codes.GetValueOrDefault(c.Id), c.Title, c.IsActive, c.Notes,
            balances.GetValueOrDefault(c.Id))).OrderBy(c => c.Code).ToList();
    }

    public async Task<int> SaveCashBoxAsync(int? id, SaveCashBoxRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        await using var tx = await uow.BeginAsync(ct);
        CashBox box;
        if (id is int existing)
        {
            box = await db.CashBoxes.FirstOrDefaultAsync(c => c.Id == existing, ct) ?? throw new NotFoundException("صندوق پیدا نشد.");
            box.Update(r.Title, r.Notes);
        }
        else
        {
            box = new CashBox(r.Title, r.Notes);
            db.Add(box);
        }
        box.SetActive(r.IsActive);
        if (await db.CashBoxes.AnyAsync(c => c.Id != box.Id && c.Title == box.Title, ct))
            throw new DomainException($"صندوق «{box.Title}» قبلاً تعریف شده است.");
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(TreasuryDetailKind.CashBox, box.Id, box.Title, box.IsActive, ct);
        await tx.CommitAsync(ct);
        return box.Id;
    }

    public async Task DeleteCashBoxAsync(int id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        var box = await db.CashBoxes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("صندوق پیدا نشد.");
        if (await db.ReceiptLines.AnyAsync(l => l.CashBoxId == id, ct) || await db.PaymentLines.AnyAsync(l => l.CashBoxId == id, ct)
            || await db.Transfers.AnyAsync(t => (t.FromKind == FundKind.CashBox && t.FromId == id) || (t.ToKind == FundKind.CashBox && t.ToId == id), ct))
            throw new DomainException($"صندوق «{box.Title}» در فرم‌ها استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        await using var tx = await uow.BeginAsync(ct);
        await links.DeleteAsync(TreasuryDetailKind.CashBox, id, ct);
        db.Remove(box);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------ حساب بانکی
    public async Task<List<TreasuryBankAccountDto>> GetBankAccountsAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = (await db.BankAccounts.AsNoTracking().Where(b => includeInactive || b.IsActive).ToListAsync(ct))
            .Where(b => access.CanUse(ScopeKind.BankAccount, b.Id)).ToList();
        var banks = await db.Banks.ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var codes = await links.GetCodesAsync(TreasuryDetailKind.BankAccount, ct);
        var balances = await links.GetBalancesAsync(TreasuryDetailKind.BankAccount, null, ct);
        return rows.Select(b => new TreasuryBankAccountDto(b.Id, codes.GetValueOrDefault(b.Id), b.Title, b.BankId,
                b.BankId is short bid ? banks.GetValueOrDefault(bid) : null, b.Branch, b.AccountNo, b.Iban, b.AccountKind,
                b.IsActive, b.Notes, balances.GetValueOrDefault(b.Id)))
            .OrderBy(b => b.Code).ToList();
    }

    public async Task<int> SaveBankAccountAsync(int? id, SaveTreasuryBankAccountRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        await using var tx = await uow.BeginAsync(ct);
        BankAccount account;
        if (id is int existing)
            account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == existing, ct) ?? throw new NotFoundException("حساب بانکی پیدا نشد.");
        else
        {
            account = new BankAccount(r.Title);
            db.Add(account);
        }
        account.Update(r.Title, r.BankId, r.Branch, r.AccountNo, r.Iban, r.AccountKind, r.Notes);
        account.SetActive(r.IsActive);
        if (await db.BankAccounts.AnyAsync(b => b.Id != account.Id && b.Title == account.Title, ct))
            throw new DomainException($"حساب بانکی «{account.Title}» قبلاً تعریف شده است.");
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(TreasuryDetailKind.BankAccount, account.Id, account.Title, account.IsActive, ct);
        await tx.CommitAsync(ct);
        return account.Id;
    }

    public async Task DeleteBankAccountAsync(int id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("حساب بانکی پیدا نشد.");
        if (await db.ReceiptLines.AnyAsync(l => l.BankAccountId == id, ct) || await db.PaymentLines.AnyAsync(l => l.BankAccountId == id, ct)
            || await db.ReceivedCheques.AnyAsync(c => c.BankAccountId == id, ct) || await db.Guarantees.AnyAsync(g => g.BankAccountId == id, ct)
            || await db.Transfers.AnyAsync(t => (t.FromKind == FundKind.BankAccount && t.FromId == id) || (t.ToKind == FundKind.BankAccount && t.ToId == id), ct))
            throw new DomainException($"حساب «{account.Title}» در فرم‌ها استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        await using var tx = await uow.BeginAsync(ct);
        await links.DeleteAsync(TreasuryDetailKind.BankAccount, id, ct);
        db.Remove(account);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------ تنخواه‌گردان
    public async Task<List<ImprestFundDto>> GetImprestFundsAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = (await db.ImprestFunds.AsNoTracking().Where(f => includeInactive || f.IsActive).ToListAsync(ct))
            .Where(f => access.CanUse(ScopeKind.ImprestFund, f.Id)).ToList();
        var holderIds = rows.Select(r => r.HolderPersonId).OfType<int>().Distinct().ToList();
        var holders = await db.Persons.Where(p => holderIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var codes = await links.GetCodesAsync(TreasuryDetailKind.Imprest, ct);
        var balances = await links.GetBalancesAsync(TreasuryDetailKind.Imprest, null, ct);
        return rows.Select(f => new ImprestFundDto(f.Id, codes.GetValueOrDefault(f.Id), f.Title, f.HolderPersonId,
                f.HolderPersonId is int h ? holders.GetValueOrDefault(h) : null, f.IsActive, f.Notes, balances.GetValueOrDefault(f.Id)))
            .OrderBy(f => f.Code).ToList();
    }

    public async Task<int> SaveImprestFundAsync(int? id, SaveImprestFundRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        await using var tx = await uow.BeginAsync(ct);
        ImprestFund fund;
        if (id is int existing)
        {
            fund = await db.ImprestFunds.FirstOrDefaultAsync(f => f.Id == existing, ct) ?? throw new NotFoundException("تنخواه‌گردان پیدا نشد.");
            fund.Update(r.Title, r.HolderPersonId, r.Notes);
        }
        else
        {
            fund = new ImprestFund(r.Title, r.HolderPersonId, r.Notes);
            db.Add(fund);
        }
        fund.SetActive(r.IsActive);
        if (await db.ImprestFunds.AnyAsync(f => f.Id != fund.Id && f.Title == fund.Title, ct))
            throw new DomainException($"تنخواه‌گردان «{fund.Title}» قبلاً تعریف شده است.");
        await db.SaveChangesAsync(ct);
        await links.SyncAsync(TreasuryDetailKind.Imprest, fund.Id, fund.Title, fund.IsActive, ct);
        await tx.CommitAsync(ct);
        return fund.Id;
    }

    public async Task DeleteImprestFundAsync(int id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        var fund = await db.ImprestFunds.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("تنخواه‌گردان پیدا نشد.");
        if (await db.ImprestSettlements.AnyAsync(s => s.ImprestFundId == id, ct)
            || await db.Transfers.AnyAsync(t => (t.FromKind == FundKind.Imprest && t.FromId == id) || (t.ToKind == FundKind.Imprest && t.ToId == id), ct))
            throw new DomainException($"تنخواه‌گردان «{fund.Title}» در فرم‌ها استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        await using var tx = await uow.BeginAsync(ct);
        await links.DeleteAsync(TreasuryDetailKind.Imprest, id, ct);
        db.Remove(fund);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>صندوق‌ها، حساب‌ها و تنخواه‌ها برای انتخاب در فرم‌ها.</summary>
    public async Task<List<FundOption>> GetFundsAsync(CancellationToken ct)
    {
        var boxCodes = await links.GetCodesAsync(TreasuryDetailKind.CashBox, ct);
        var bankCodes = await links.GetCodesAsync(TreasuryDetailKind.BankAccount, ct);
        var imprestCodes = await links.GetCodesAsync(TreasuryDetailKind.Imprest, ct);
        var result = new List<FundOption>();
        result.AddRange((await db.CashBoxes.AsNoTracking().ToListAsync(ct))
            .Select(c => new FundOption(FundKind.CashBox, c.Id, c.Title, boxCodes.GetValueOrDefault(c.Id), c.IsActive)));
        result.AddRange((await db.BankAccounts.AsNoTracking().ToListAsync(ct))
            .Select(b => new FundOption(FundKind.BankAccount, b.Id, b.Title, bankCodes.GetValueOrDefault(b.Id), b.IsActive)));
        result.AddRange((await db.ImprestFunds.AsNoTracking().ToListAsync(ct))
            .Select(f => new FundOption(FundKind.Imprest, f.Id, f.Title, imprestCodes.GetValueOrDefault(f.Id), f.IsActive)));
        // محدوده داده: فقط صندوق/حساب/تنخواه‌هایی که کاربر مجاز است
        return result.Where(f => access.CanUse(TreasuryScopes.Of(f.Kind), f.Id)).OrderBy(f => f.Kind).ThenBy(f => f.Code).ToList();
    }

    // ------------------------------------------------------------ انواع دریافت و پرداخت
    public async Task<List<TransactionTypeDto>> GetTypesAsync(TransactionKind? kind, bool includeInactive, CancellationToken ct)
    {
        var query = db.TransactionTypes.AsNoTracking().Where(t => includeInactive || t.IsActive);
        if (kind is not null) query = query.Where(t => t.Kind == kind);
        var rows = await query.OrderBy(t => t.Kind).ThenBy(t => t.SortOrder).ThenBy(t => t.Title).ToListAsync(ct);
        var accountIds = rows.Select(r => r.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return rows.Select(t => new TransactionTypeDto(t.Id, t.Kind, t.Title, t.AccountId,
            accounts.GetValueOrDefault(t.AccountId)?.Code ?? "?", accounts.GetValueOrDefault(t.AccountId)?.Title ?? "?",
            t.SortOrder, t.IsActive)).ToList();
    }

    public async Task<short> SaveTypeAsync(short? id, SaveTransactionTypeRequest r, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Code == r.AccountCode.Trim(), ct)
                      ?? throw new DomainException($"حساب با کد {r.AccountCode} وجود ندارد.");
        if (account.Level != 3) throw new DomainException($"«{account.Code} - {account.Title}» معین نیست.");
        if (!account.IsActive) throw new DomainException($"معین «{account.Title}» غیرفعال است.");

        TransactionType type;
        if (id is short existing)
        {
            type = await db.TransactionTypes.FirstOrDefaultAsync(t => t.Id == existing, ct) ?? throw new NotFoundException("نوع پیدا نشد.");
            type.Update(r.Title, account.Id, r.SortOrder);
        }
        else
        {
            type = new TransactionType(r.Kind, r.Title, account.Id, r.SortOrder);
            db.Add(type);
        }
        type.SetActive(r.IsActive);
        if (await db.TransactionTypes.AnyAsync(t => t.Id != type.Id && t.Kind == type.Kind && t.Title == type.Title, ct))
            throw new DomainException($"«{type.Title}» قبلاً تعریف شده است.");
        await db.SaveChangesAsync(ct);
        return type.Id;
    }

    public async Task DeleteTypeAsync(short id, CancellationToken ct)
    {
        access.Demand(TreasuryPermissions.Setup);
        var type = await db.TransactionTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نوع پیدا نشد.");
        if (await db.Receipts.AnyAsync(r => r.TypeId == id, ct) || await db.Payments.AnyAsync(p => p.TypeId == id, ct))
            throw new DomainException($"«{type.Title}» در فرم‌ها استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Remove(type);
        await db.SaveChangesAsync(ct);
    }
}
