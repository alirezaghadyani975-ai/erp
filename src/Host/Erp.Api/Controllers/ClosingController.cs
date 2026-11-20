using Erp.Accounting.Application.Closing;
using Erp.Accounting.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>بستن سال مالی و صورت‌های مالی</summary>
[ApiController]
[Route("api/accounting")]
public class ClosingController(ClosingService closing, StatementService statements) : ControllerBase
{
    /// <summary>وضعیت بستن سال و موانع</summary>
    [HttpGet("fiscal-years/{id}/closing")]
    public Task<ClosingStatusDto> Status(short id, CancellationToken ct) => closing.StatusAsync(id, ct);

    /// <summary>صدور اسناد بستن (موقت) و قفل سال</summary>
    [HttpPost("fiscal-years/{id}/closing")]
    public Task<ClosingStatusDto> Close(short id, CancellationToken ct) => closing.CloseAsync(id, ct);

    /// <summary>برگشت از بستن (تا وقتی اسناد دائم نشده‌اند)</summary>
    [HttpDelete("fiscal-years/{id}/closing")]
    public Task<ClosingStatusDto> Undo(short id, CancellationToken ct) => closing.UndoAsync(id, ct);

    /// <summary>قطعی کردن بستن سال</summary>
    [HttpPost("fiscal-years/{id}/closing/finalize")]
    public Task<ClosingStatusDto> Finalize(short id, CancellationToken ct) => closing.FinalizeAsync(id, ct);

    /// <summary>ترازنامه</summary>
    [HttpPost("reports/balance-sheet")]
    public Task<StatementDto> BalanceSheet(ReportFilter filter, CancellationToken ct) => statements.BalanceSheetAsync(filter, ct);

    /// <summary>صورت سود و زیان</summary>
    [HttpPost("reports/income-statement")]
    public Task<StatementDto> IncomeStatement(ReportFilter filter, CancellationToken ct) => statements.IncomeStatementAsync(filter, ct);
}
