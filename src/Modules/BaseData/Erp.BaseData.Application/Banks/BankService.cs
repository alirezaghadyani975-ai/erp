using Erp.SharedKernel.Security;
using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Domain.Banks;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Application.Banks;

public sealed record BankDto(short Id, string Title, string? IbanCode, string? CardPrefixes, bool IsActive);

public sealed record SaveBankRequest(string Title, string? IbanCode, string? CardPrefixes, bool IsActive);

/// <summary>بانک‌ها؛ تشخیص بانک حساب‌ها از روی کد شبا و پیش‌شماره کارت همین جدول است.</summary>
public class BankService(IBaseDataDbContext db, IAccessControl access)
{
    public Task<List<BankDto>> GetAllAsync(bool includeInactive, CancellationToken ct) =>
        db.Banks.AsNoTracking().Where(b => includeInactive || b.IsActive)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Title)
            .Select(b => new BankDto(b.Id, b.Title, b.IbanCode, b.CardPrefixes, b.IsActive))
            .ToListAsync(ct);

    public async Task<short> CreateAsync(SaveBankRequest request, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.Banks);
        var bank = new Bank(request.Title, request.IbanCode, request.CardPrefixes);
        if (!request.IsActive) bank.Deactivate();
        await EnsureUniqueAsync(bank, ct);
        db.Banks.Add(bank);
        await db.SaveChangesAsync(ct);
        return bank.Id;
    }

    public async Task UpdateAsync(short id, SaveBankRequest request, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.Banks);
        var bank = await db.Banks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("بانک پیدا نشد.");
        bank.Update(request.Title, request.IbanCode, request.CardPrefixes);
        if (request.IsActive) bank.Activate(); else bank.Deactivate();
        await EnsureUniqueAsync(bank, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(short id, CancellationToken ct)
    {
        access.Demand(BaseDataPermissions.Banks);
        var bank = await db.Banks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("بانک پیدا نشد.");
        if (await db.Persons.AnyAsync(p => p.BankAccounts.Any(a => a.BankId == id), ct))
            throw new DomainException($"بانک «{bank.Title}» در حساب‌های اشخاص استفاده شده و حذف نمی‌شود؛ غیرفعالش کنید.");
        db.Banks.Remove(bank);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(Bank bank, CancellationToken ct)
    {
        if (await db.Banks.AnyAsync(b => b.Id != bank.Id && b.Title == bank.Title, ct))
            throw new DomainException($"بانک «{bank.Title}» قبلاً تعریف شده است.");
        if (bank.IbanCode is not null && await db.Banks.AnyAsync(b => b.Id != bank.Id && b.IbanCode == bank.IbanCode, ct))
            throw new DomainException($"کد شبای {bank.IbanCode} برای بانک دیگری ثبت شده است.");
    }
}
