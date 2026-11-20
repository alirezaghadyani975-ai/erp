using Erp.Accounting.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>گزارش‌های حسابداری: تراز آزمایشی و دفتر حساب</summary>
[ApiController]
[Route("api/accounting/reports")]
public class ReportsController(ReportService service) : ControllerBase
{
    /// <summary>تراز آزمایشی (گروه، کل، معین یا تفصیلی) با ستون‌های ۴ و ۶ ستونی</summary>
    [HttpPost("trial-balance")]
    public Task<TrialBalanceDto> TrialBalance(TrialBalanceRequest request, CancellationToken ct) =>
        service.TrialBalanceAsync(request, ct);

    /// <summary>دفتر حساب یا تفصیلی با مانده جاری</summary>
    [HttpPost("ledger")]
    public Task<LedgerDto> Ledger(LedgerRequest request, CancellationToken ct) => service.LedgerAsync(request, ct);

    /// <summary>مرور حساب‌ها: گردش و مانده به تفکیک سطح دلخواه با محدودیت حساب و تفصیلی‌ها</summary>
    [HttpPost("review")]
    public Task<ReviewDto> Review(ReviewRequest request, CancellationToken ct) => service.ReviewAsync(request, ct);
}
