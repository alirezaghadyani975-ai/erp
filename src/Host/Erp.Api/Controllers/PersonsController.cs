using Erp.Accounting.Application.Persons;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>اشخاص (نسخه حداقلی) و تفصیلی نقش‌هایشان</summary>
[ApiController]
[Route("api/base/persons")]
public class PersonsController(PersonService service) : ControllerBase
{
    [HttpGet]
    public Task<List<PersonDto>> Search([FromQuery] string? q, CancellationToken ct) => service.SearchAsync(q, ct);

    [HttpGet("{id:int}")]
    public Task<PersonDto> Get(int id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>تعریف شخص و ساخت خودکار تفصیلی برای هر نقش</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Create(CreatePersonRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));
}
