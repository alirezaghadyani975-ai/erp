using Erp.Accounting.Application.Details;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>انواع تفصیلی و تفصیلی‌ها</summary>
[ApiController]
[Route("api/accounting/details")]
public class DetailsController(DetailService service) : ControllerBase
{
    [HttpGet("types")]
    public Task<List<DetailTypeDto>> GetTypes(CancellationToken ct) => service.GetTypesAsync(ct);

    [HttpPost("types")]
    public async Task<ActionResult<short>> CreateType(CreateDetailTypeRequest request, CancellationToken ct) =>
        Ok(await service.CreateTypeAsync(request, ct));

    /// <summary>ویرایش گروه تفصیلی (بازه با حفظ کدهای موجود)</summary>
    [HttpPut("types/{id}")]
    public async Task<IActionResult> UpdateType(short id, UpdateDetailTypeRequest request, CancellationToken ct)
    {
        await service.UpdateTypeAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>جستجوی تفصیلی با کد یا عنوان</summary>
    [HttpGet]
    public Task<List<DetailAccountDto>> Search([FromQuery] short? typeId, [FromQuery] string? q, CancellationToken ct) =>
        service.SearchAsync(typeId, q, ct);

    /// <summary>تفصیلی غیرشخصی (صندوق، بانک، مرکز هزینه، پروژه، ...)</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Create(CreateDetailAccountRequest request, CancellationToken ct) =>
        Ok(await service.CreateAccountAsync(request, ct));
}
