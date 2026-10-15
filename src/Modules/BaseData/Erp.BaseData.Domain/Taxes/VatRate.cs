using Erp.SharedKernel.Domain;

namespace Erp.BaseData.Domain.Taxes;

/// <summary>
/// نرخ مالیات و عوارض ارزش افزوده یک سال (base.VatRates). فرم‌های خرید و فروش مبلغ ارزش افزوده را
/// از روی سال تاریخ فرم و همین نرخ‌ها خودکار حساب می‌کنند.
/// </summary>
public class VatRate : Entity<short>
{
    private VatRate() { } // EF

    public VatRate(int persianYear, decimal taxPercent, decimal tollPercent)
    {
        if (persianYear is < 1390 or > 1500) throw new DomainException("سال نامعتبر است.");
        PersianYear = persianYear;
        Update(taxPercent, tollPercent);
    }

    public int PersianYear { get; private set; }
    public decimal TaxPercent { get; private set; }
    public decimal TollPercent { get; private set; }

    public decimal TotalPercent => TaxPercent + TollPercent;

    public void Update(decimal taxPercent, decimal tollPercent)
    {
        if (taxPercent is < 0 or > 100 || tollPercent is < 0 or > 100)
            throw new DomainException("درصد مالیات و عوارض باید بین ۰ تا ۱۰۰ باشد.");
        TaxPercent = Math.Round(taxPercent, 2);
        TollPercent = Math.Round(tollPercent, 2);
    }

    /// <summary>مبلغ مالیات و عوارض یک مبلغ (به ریال، گرد به عدد صحیح).</summary>
    public (decimal Tax, decimal Toll) Calculate(decimal amount) =>
        (Math.Round(amount * TaxPercent / 100, 0, MidpointRounding.AwayFromZero),
         Math.Round(amount * TollPercent / 100, 0, MidpointRounding.AwayFromZero));
}
