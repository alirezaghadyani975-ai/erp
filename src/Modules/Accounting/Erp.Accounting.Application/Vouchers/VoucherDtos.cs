using Erp.Accounting.Domain.Vouchers;

namespace Erp.Accounting.Application.Vouchers;

/// <summary>
/// ردیف سند با کد حساب و کد تفصیلی‌ها (همان کدهایی که حسابدار می‌شناسد).
/// DetailCodes به ترتیب سطح ۱ تا ۶ است؛ سطح خالی را null بگذارید.
/// </summary>
public sealed record VoucherLineRequest(
    string AccountCode,
    List<string?>? DetailCodes,
    decimal Debit,
    decimal Credit,
    string? Description = null,
    string? CurrencyCode = null,
    decimal? CurrencyAmount = null,
    decimal? ExchangeRate = null,
    decimal? Quantity = null,
    short? UnitId = null,
    string? FollowUpNo = null,
    string? FollowUpDate = null);

/// <summary>تاریخ‌ها شمسی، مثل 1405/07/21.</summary>
public sealed record SaveVoucherRequest(
    short FiscalYearId,
    string Date,
    string? Description,
    List<VoucherLineRequest> Lines,
    VoucherType Type = VoucherType.Normal);

public sealed record DetailRefDto(int Id, string Code, string Title);

public sealed record VoucherLineDto(
    short RowNo, int AccountId, string AccountCode, string AccountTitle,
    List<DetailRefDto?> Details, string? Description, decimal Debit, decimal Credit,
    string? CurrencyCode, decimal? CurrencyAmount, decimal? ExchangeRate,
    decimal? Quantity, short? UnitId, string? FollowUpNo, string? FollowUpDate);

public sealed record VoucherDto(
    long Id, short FiscalYearId, int TempNo, int? VoucherNo, string Date, string? Description,
    VoucherType Type, VoucherStatus Status, string? SourceModule, string? SourceDocType, long? SourceDocumentId,
    decimal TotalDebit, decimal TotalCredit, bool IsBalanced, List<VoucherLineDto> Lines);

public sealed record VoucherSummaryDto(
    long Id, int TempNo, int? VoucherNo, string Date, string? Description, VoucherType Type, VoucherStatus Status,
    decimal TotalDebit, string? SourceDocType = null);
