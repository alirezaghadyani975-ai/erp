using Erp.BaseData.Application.Taxes;
using Erp.SharedKernel.Calendar;

namespace Erp.Api.Components.Shared.Inputs;

/// <summary>نرخ ارزش افزوده هر سال (در طول اتصال، با حافظه) برای محاسبه ردیف‌ها بدون رفت‌وبرگشت مکرر.</summary>
public sealed class VatRateCache(Backend backend)
{
    private readonly Dictionary<int, VatCalculation?> _cache = [];

    public async Task<VatCalculation?> ForDateAsync(string? date)
    {
        if (!PersianDate.TryParse(date, out var d)) return null;
        var year = PersianDate.PersianYear(d);
        if (_cache.TryGetValue(year, out var hit)) return hit;
        var r = await backend.Run<VatRateService, VatCalculation?>(s => s.CalculateAsync(date!, 0, default));
        return _cache[year] = r.Value;
    }

    public void Invalidate() => _cache.Clear();

    /// <summary>همان گرد کردن دامنه: مالیات و عوارض جدا، به ریال.</summary>
    public static decimal Calc(VatCalculation rate, decimal amount) =>
        Math.Round(amount * rate.TaxPercent / 100, 0, MidpointRounding.AwayFromZero)
        + Math.Round(amount * rate.TollPercent / 100, 0, MidpointRounding.AwayFromZero);
}
