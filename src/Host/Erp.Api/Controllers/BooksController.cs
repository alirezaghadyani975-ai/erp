using Erp.Accounting.Application.Reports;
using Erp.Accounting.Application.Vouchers;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>دفاتر قانونی و ترتیب شماره اسناد</summary>
[ApiController]
[Route("api/accounting")]
public class BooksController(LegalBookService books, VoucherNumberingService numbering) : ControllerBase
{
    /// <summary>دفتر روزنامه (اسناد دائم به تفکیک حساب کل)</summary>
    [HttpPost("reports/journal")]
    public Task<JournalDto> Journal(ReportFilter filter, CancellationToken ct) => books.JournalAsync(filter, ct);

    /// <summary>دفتر کل؛ fromCode و toCode بازه کد حساب کل</summary>
    [HttpPost("reports/general-ledger")]
    public Task<GeneralLedgerDto> GeneralLedger(ReportFilter filter, [FromQuery] string? fromCode, [FromQuery] string? toCode, CancellationToken ct) =>
        books.GeneralLedgerAsync(filter, fromCode, toCode, ct);

    /// <summary>وضعیت ترتیب شماره‌ها و اسناد موقت تا تاریخ</summary>
    [HttpGet("fiscal-years/{id}/numbering")]
    public Task<NumberingStatusDto> Status(short id, [FromQuery] string? toDate, CancellationToken ct) => numbering.StatusAsync(id, toDate, ct);

    /// <summary>دائم کردن گروهی اسناد موقت تا تاریخ، به ترتیب تاریخ</summary>
    [HttpPost("fiscal-years/{id}/numbering/permanent")]
    public Task<int> MakePermanent(short id, [FromQuery] string? toDate, CancellationToken ct) => numbering.MakePermanentAsync(id, toDate, ct);

    /// <summary>شماره‌گذاری مجدد اسناد دائم به ترتیب تاریخ</summary>
    [HttpPost("fiscal-years/{id}/numbering/renumber")]
    public Task<int> Renumber(short id, CancellationToken ct) => numbering.RenumberAsync(id, ct);
}
