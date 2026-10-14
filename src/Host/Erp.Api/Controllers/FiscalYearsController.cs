using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Domain.FiscalYears;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>سال‌ها و دوره‌های مالی</summary>
[ApiController]
[Route("api/accounting/fiscal-years")]
public class FiscalYearsController(FiscalYearService service) : ControllerBase
{
    [HttpGet]
    public Task<List<FiscalYearDto>> GetAll(CancellationToken ct) => service.GetAllAsync(ct);

    /// <summary>ساخت سال مالی شمسی با ۱۲ دوره؛ startMonth برای سال مالی غیرتقویمی (مثلاً ۴ = تیر)</summary>
    [HttpPost("{persianYear:int}")]
    public async Task<ActionResult<short>> Create(int persianYear, [FromQuery] byte startMonth = 1, CancellationToken ct = default) =>
        Ok(await service.CreateAsync(persianYear, startMonth, ct));

    /// <summary>باز، قفل یا بستن یک دوره</summary>
    [HttpPut("{id}/periods/{periodNo}/status/{status}")]
    public async Task<IActionResult> SetPeriodStatus(short id, byte periodNo, PeriodStatus status, CancellationToken ct)
    {
        await service.SetPeriodStatusAsync(id, periodNo, status, ct);
        return NoContent();
    }
}
