using Erp.Accounting.Application.ChartOfAccounts;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>کدینگ حساب‌ها (گروه، کل، معین)</summary>
[ApiController]
[Route("api/accounting/accounts")]
public class AccountsController(AccountService service) : ControllerBase
{
    /// <summary>درخت کامل کدینگ</summary>
    [HttpGet("tree")]
    public Task<List<AccountNodeDto>> GetTree(CancellationToken ct) => service.GetTreeAsync(ct);

    [HttpGet("{id:int}")]
    public Task<AccountDto> Get(int id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>دریافت حساب با کد، مثلاً 2301</summary>
    [HttpGet("by-code/{code}")]
    public Task<AccountDto> GetByCode(string code, CancellationToken ct) => service.GetByCodeAsync(code, ct);

    /// <summary>حساب اختصاصی شرکت (کل یا معین) زیر یک حساب موجود</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Create(CreateAccountRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    /// <summary>تغییر عنوان و فعال/غیرفعال کردن</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAccountRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>تعریف یا تغییر سطح تفصیلی یک معین (سطح ۱ تا ۶)</summary>
    [HttpPut("{id:int}/detail-levels/{levelNo:int}")]
    public async Task<IActionResult> SetDetailLevel(int id, int levelNo, SetDetailLevelRequest request, CancellationToken ct)
    {
        await service.SetDetailLevelAsync(id, levelNo, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/detail-levels/{levelNo:int}")]
    public async Task<IActionResult> RemoveDetailLevel(int id, int levelNo, CancellationToken ct)
    {
        await service.RemoveDetailLevelAsync(id, levelNo, ct);
        return NoContent();
    }
}
