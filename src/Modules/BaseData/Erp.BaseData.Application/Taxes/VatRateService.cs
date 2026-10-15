using Erp.BaseData.Application.Abstractions;
using Erp.BaseData.Domain.Taxes;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.BaseData.Application.Taxes;

public sealed record VatRateDto(short Id, int PersianYear, decimal TaxPercent, decimal TollPercent)
{
    public decimal TotalPercent => TaxPercent + TollPercent;
}

/// <summary>نتیجه محاسبه ارزش افزوده یک مبلغ در یک تاریخ.</summary>
public sealed record VatCalculation(int PersianYear, decimal TaxPercent, decimal TollPercent, decimal Tax, decimal Toll)
{
    public decimal Total => Tax + Toll;
}

/// <summary>نرخ‌های سالانه مالیات و عوارض ارزش افزوده.</summary>
public class VatRateService(IBaseDataDbContext db)
{
    public Task<List<VatRateDto>> GetAllAsync(CancellationToken ct) =>
        db.VatRates.AsNoTracking().OrderByDescending(r => r.PersianYear)
            .Select(r => new VatRateDto(r.Id, r.PersianYear, r.TaxPercent, r.TollPercent)).ToListAsync(ct);

    /// <summary>ثبت یا اصلاح نرخ یک سال.</summary>
    public async Task SaveAsync(int persianYear, decimal taxPercent, decimal tollPercent, CancellationToken ct)
    {
        var rate = await db.VatRates.FirstOrDefaultAsync(r => r.PersianYear == persianYear, ct);
        if (rate is null) db.VatRates.Add(new VatRate(persianYear, taxPercent, tollPercent));
        else rate.Update(taxPercent, tollPercent);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(short id, CancellationToken ct)
    {
        var rate = await db.VatRates.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("نرخ پیدا نشد.");
        db.VatRates.Remove(rate);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>ارزش افزوده یک مبلغ به نرخ سال تاریخ داده‌شده (شمسی)؛ اگر نرخ آن سال تعریف نشده باشد null.</summary>
    public async Task<VatCalculation?> CalculateAsync(string date, decimal amount, CancellationToken ct)
    {
        var year = PersianDate.PersianYear(PersianDate.Parse(date));
        var rate = await db.VatRates.AsNoTracking().FirstOrDefaultAsync(r => r.PersianYear == year, ct);
        if (rate is null) return null;
        var (tax, toll) = rate.Calculate(amount);
        return new VatCalculation(year, rate.TaxPercent, rate.TollPercent, tax, toll);
    }
}
