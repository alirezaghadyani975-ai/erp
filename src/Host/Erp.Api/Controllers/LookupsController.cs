using Erp.Accounting.Application.Lookups;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>داده‌های پایه: ارزها و واحدها</summary>
[ApiController]
[Route("api/base")]
public class LookupsController(FinanceLookupService service) : ControllerBase
{
    [HttpGet("currencies")]
    public Task<List<CurrencyDto>> Currencies(CancellationToken ct) => service.CurrenciesAsync(ct);

    [HttpGet("units")]
    public Task<List<UnitDto>> Units(CancellationToken ct) => service.UnitsAsync(ct);
}
