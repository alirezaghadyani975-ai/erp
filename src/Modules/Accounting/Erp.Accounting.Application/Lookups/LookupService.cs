using Erp.Accounting.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Lookups;

public sealed record CurrencyDto(short Id, string Code, string Title, byte DecimalPlaces, bool IsBase);

public sealed record UnitDto(short Id, string Title);

/// <summary>ارزها و واحدهای سنجش برای ردیف سند.</summary>
public class FinanceLookupService(IAccountingDbContext db)
{
    public Task<List<CurrencyDto>> CurrenciesAsync(CancellationToken ct) =>
        db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase).ThenBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Id, c.Code, c.Title, c.DecimalPlaces, c.IsBase)).ToListAsync(ct);

    public Task<List<UnitDto>> UnitsAsync(CancellationToken ct) =>
        db.Units.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Id)
            .Select(u => new UnitDto(u.Id, u.Title)).ToListAsync(ct);
}
