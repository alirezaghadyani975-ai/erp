using Erp.BaseData.Application.Banks;
using Erp.BaseData.Application.Lookups;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>جداول پایه عمومی (کشور، استان، شهر، نوع نشانی، گروه اشخاص، سمت رابط) و بانک‌ها</summary>
[ApiController]
[Route("api/base")]
public class BaseLookupsController(LookupService lookups, BankService banks) : ControllerBase
{
    [HttpGet("lookups")]
    public Task<List<LookupCategoryDto>> Categories(CancellationToken ct) => lookups.GetCategoriesAsync(ct);

    /// <summary>اقلام یک فهرست با کد فهرست (مثلاً city)، اختیاری زیر یک والد (مثلاً شهرهای یک استان)</summary>
    [HttpGet("lookups/{categoryCode}")]
    public Task<List<LookupItemDto>> Items(string categoryCode, [FromQuery] int? parentId, [FromQuery] bool includeInactive,
        CancellationToken ct) => lookups.GetItemsAsync(categoryCode, parentId, includeInactive, ct);

    [HttpPost("lookups/items")]
    public async Task<ActionResult<int>> CreateItem(SaveLookupItemRequest request, CancellationToken ct) =>
        Ok(await lookups.CreateItemAsync(request, ct));

    [HttpPut("lookups/items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, SaveLookupItemRequest request, CancellationToken ct)
    {
        await lookups.UpdateItemAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("lookups/items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id, CancellationToken ct)
    {
        await lookups.DeleteItemAsync(id, ct);
        return NoContent();
    }

    [HttpGet("banks")]
    public Task<List<BankDto>> Banks([FromQuery] bool includeInactive, CancellationToken ct) => banks.GetAllAsync(includeInactive, ct);

    [HttpPost("banks")]
    public async Task<ActionResult<short>> CreateBank(SaveBankRequest request, CancellationToken ct) =>
        Ok(await banks.CreateAsync(request, ct));

    [HttpPut("banks/{id}")]
    public async Task<IActionResult> UpdateBank(short id, SaveBankRequest request, CancellationToken ct)
    {
        await banks.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("banks/{id}")]
    public async Task<IActionResult> DeleteBank(short id, CancellationToken ct)
    {
        await banks.DeleteAsync(id, ct);
        return NoContent();
    }
}
