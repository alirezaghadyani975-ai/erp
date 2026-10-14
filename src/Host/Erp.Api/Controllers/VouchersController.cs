using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>اسناد حسابداری</summary>
[ApiController]
[Route("api/accounting/vouchers")]
public class VouchersController(VoucherService service) : ControllerBase
{
    [HttpGet]
    public Task<List<VoucherSummaryDto>> List([FromQuery] short fiscalYearId, [FromQuery] VoucherStatus? status, CancellationToken ct) =>
        service.ListAsync(fiscalYearId, status, ct);

    [HttpGet("{id:long}")]
    public Task<VoucherDto> Get(long id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>ثبت سند جدید (پیش‌نویس)</summary>
    [HttpPost]
    public async Task<ActionResult<long>> Create(SaveVoucherRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    /// <summary>ویرایش سند پیش‌نویس</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, SaveVoucherRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:long}/temporary")]
    public async Task<IActionResult> MarkTemporary(long id, CancellationToken ct)
    {
        await service.MarkTemporaryAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:long}/draft")]
    public async Task<IActionResult> ReturnToDraft(long id, CancellationToken ct)
    {
        await service.ReturnToDraftAsync(id, ct);
        return NoContent();
    }

    /// <summary>دائم کردن؛ شماره قطعی سند را برمی‌گرداند</summary>
    [HttpPost("{id:long}/permanent")]
    public async Task<ActionResult<int>> MarkPermanent(long id, CancellationToken ct) =>
        Ok(await service.MarkPermanentAsync(id, ct));

    [HttpPost("{id:long}/void")]
    public async Task<IActionResult> Void(long id, CancellationToken ct)
    {
        await service.VoidAsync(id, ct);
        return NoContent();
    }

    /// <summary>حذف (فقط اگر قواعد حذف امن اجازه دهد)</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
