namespace Erp.Accounting.Domain.Vouchers;

/// <summary>
/// ردیف سند (fin.VoucherLines). شش ستون تفصیلی جدا هستند تا تراز تفصیلی با یک GROUP BY ساده
/// ساخته شود. ارز، نرخ، مقدار و واحد از ابتدا در ردیف وجود دارند.
/// </summary>
public class VoucherLine
{
    private VoucherLine() { } // EF

    internal VoucherLine(short rowNo, int accountId, int?[] detailIds, decimal debit, decimal credit, LineExtras extras)
    {
        RowNo = rowNo;
        AccountId = accountId;
        Detail1Id = detailIds[0];
        Detail2Id = detailIds[1];
        Detail3Id = detailIds[2];
        Detail4Id = detailIds[3];
        Detail5Id = detailIds[4];
        Detail6Id = detailIds[5];
        Debit = debit;
        Credit = credit;
        Description = extras.Description;
        CurrencyId = extras.Currency?.CurrencyId;
        ExchangeRate = extras.Currency?.ExchangeRate;
        CurrencyAmount = extras.Currency?.Amount;
        Quantity = extras.Quantity?.Value;
        UnitId = extras.Quantity?.UnitId;
        FollowUpNo = extras.FollowUpNo;
        FollowUpDate = extras.FollowUpDate;
    }

    public long Id { get; private set; }
    public long VoucherId { get; private set; }
    public short RowNo { get; internal set; }
    public int AccountId { get; private set; }

    public int? Detail1Id { get; private set; }
    public int? Detail2Id { get; private set; }
    public int? Detail3Id { get; private set; }
    public int? Detail4Id { get; private set; }
    public int? Detail5Id { get; private set; }
    public int? Detail6Id { get; private set; }

    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string? Description { get; private set; }

    public short? CurrencyId { get; private set; }
    public decimal? ExchangeRate { get; private set; }
    public decimal? CurrencyAmount { get; private set; }
    public decimal? Quantity { get; private set; }
    public short? UnitId { get; private set; }

    /// <summary>شماره و تاریخ پیگیری (مثلاً شماره چک یا فاکتور).</summary>
    public string? FollowUpNo { get; private set; }
    public DateOnly? FollowUpDate { get; private set; }

    public int?[] DetailIds => [Detail1Id, Detail2Id, Detail3Id, Detail4Id, Detail5Id, Detail6Id];
}

public sealed record CurrencyAmount(short CurrencyId, decimal Amount, decimal ExchangeRate);

public sealed record LineQuantity(decimal Value, short UnitId);

/// <summary>اطلاعات اختیاری یک ردیف.</summary>
public sealed record LineExtras(
    string? Description = null,
    CurrencyAmount? Currency = null,
    LineQuantity? Quantity = null,
    string? FollowUpNo = null,
    DateOnly? FollowUpDate = null)
{
    public static readonly LineExtras None = new();
}
