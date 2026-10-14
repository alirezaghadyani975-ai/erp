using Erp.BaseData.Application.Persons;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>اشخاص، نقش‌ها، رابطین و پیوست‌ها (ماژول اطلاعات پایه)</summary>
[ApiController]
[Route("api/base/persons")]
public class PersonsController(PersonService service) : ControllerBase
{
    /// <summary>جستجو با نام، کد ملی، موبایل یا کد تفصیلی؛ با فیلتر نقش و گروه</summary>
    [HttpGet]
    public Task<List<PersonListItemDto>> Search([FromQuery] string? q, [FromQuery] short? roleId, [FromQuery] int? groupId,
        [FromQuery] bool includeInactive, CancellationToken ct) => service.SearchAsync(q, roleId, groupId, includeInactive, ct);

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

    /// <summary>حذف شخص بدون گردش (تفصیلی‌های بدون گردشش هم حذف می‌شوند)</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>اشخاص مشابه (نام، موبایل یا کد ملی یکسان) برای هشدار پیش از ذخیره</summary>
    [HttpPost("duplicates")]
    public Task<List<DuplicateDto>> Duplicates(SavePersonRequest request, [FromQuery] int? excludeId, CancellationToken ct) =>
        service.FindDuplicatesAsync(request, excludeId, ct);

    [HttpGet("roles")]
    public Task<List<PersonRoleTypeDto>> Roles([FromQuery] bool includeInactive, CancellationToken ct) =>
        service.GetRoleTypesAsync(includeInactive, ct);

    /// <summary>تعریف نقش جدید (مثلاً «نماینده فروش»)</summary>
    [HttpPost("roles")]
    public async Task<ActionResult<short>> CreateRole([FromBody] string title, CancellationToken ct) =>
        Ok(await service.CreateRoleTypeAsync(title, ct));

    [HttpDelete("roles/{id}")]
    public async Task<IActionResult> DeleteRole(short id, CancellationToken ct)
    {
        await service.DeleteRoleTypeAsync(id, ct);
        return NoContent();
    }

    /// <summary>بارگذاری پیوست (حداکثر ۱۰ مگابایت)</summary>
    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<int>> Upload(int id, IFormFile file, [FromForm] string? title, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Ok(await service.AddAttachmentAsync(id, title, file.FileName, file.ContentType, ms.ToArray(), ct));
    }

    /// <summary>دریافت فایل پیوست</summary>
    [HttpGet("attachments/{attachmentId:int}")]
    public async Task<IActionResult> Download(int attachmentId, CancellationToken ct)
    {
        var file = await service.GetAttachmentAsync(attachmentId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("attachments/{attachmentId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attachmentId, CancellationToken ct)
    {
        await service.DeleteAttachmentAsync(attachmentId, ct);
        return NoContent();
    }
}
