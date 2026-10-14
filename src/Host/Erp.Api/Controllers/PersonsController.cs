using Erp.Accounting.Application.Persons;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>اشخاص، نقش‌ها و اطلاعات تکمیلی (نشانی، حساب بانکی، اطلاعات پرسنلی)</summary>
[ApiController]
[Route("api/base/persons")]
public class PersonsController(PersonService service) : ControllerBase
{
    /// <summary>جستجو با نام، کد ملی، موبایل یا کد تفصیلی؛ با فیلتر نقش</summary>
    [HttpGet]
    public Task<List<PersonListItemDto>> Search([FromQuery] string? q, [FromQuery] short? roleId,
        [FromQuery] bool includeInactive, CancellationToken ct) => service.SearchAsync(q, roleId, includeInactive, ct);

    [HttpGet("{id:int}")]
    public Task<PersonDetailsDto> Get(int id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>تعریف شخص؛ تفصیلی مشترک (و برای کارمند، تفصیلی کارکنان) خودکار ساخته می‌شود</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Create(SavePersonRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SavePersonRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("roles")]
    public Task<List<PersonRoleTypeDto>> Roles([FromQuery] bool includeInactive, CancellationToken ct) =>
        service.GetRoleTypesAsync(includeInactive, ct);

    /// <summary>تعریف نقش جدید (مثلاً «نماینده فروش»)</summary>
    [HttpPost("roles")]
    public async Task<ActionResult<short>> CreateRole([FromBody] string title, CancellationToken ct) =>
        Ok(await service.CreateRoleTypeAsync(title, ct));
}
