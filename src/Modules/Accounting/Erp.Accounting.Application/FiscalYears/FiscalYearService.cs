using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Domain.FiscalYears;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.FiscalYears;

public sealed record FiscalPeriodDto(byte PeriodNo, string Title, string StartDate, string EndDate, PeriodStatus Status);

public sealed record FiscalYearDto(short Id, string Title, string StartDate, string EndDate, PeriodStatus Status,
    List<FiscalPeriodDto> Periods);

public class FiscalYearService(IAccountingDbContext db)
{
    public async Task<List<FiscalYearDto>> GetAllAsync(CancellationToken ct)
    {
        var years = await db.FiscalYears.AsNoTracking().Include(f => f.Periods)
            .OrderByDescending(f => f.StartDate).ToListAsync(ct);
        return years.Select(ToDto).ToList();
    }

    /// <summary>
    /// ساخت سال مالی شمسی با ۱۲ دوره ماهانه. <paramref name="startMonth"/> برای سال مالی
    /// غیرتقویمی است (مثلاً ۴ یعنی تیر تا خرداد سال بعد).
    /// </summary>
    public async Task<short> CreateAsync(int persianYear, byte startMonth, CancellationToken ct)
    {
        if (persianYear is < 1395 or > 1420)
            throw new DomainException("تقویم شمسی فعلاً برای سال‌های ۱۳۹۵ تا ۱۴۲۰ بارگذاری شده است.");
        return await db.CreateFiscalYearAsync(persianYear, startMonth, ct);
    }

    public async Task SetPeriodStatusAsync(short yearId, byte periodNo, PeriodStatus status, CancellationToken ct)
    {
        var year = await db.FiscalYears.Include(f => f.Periods).FirstOrDefaultAsync(f => f.Id == yearId, ct)
                   ?? throw new NotFoundException($"سال مالی {yearId} پیدا نشد.");
        year.SetPeriodStatus(periodNo, status);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف سال مالی؛ فقط وقتی هیچ سندی (حتی پیش‌نویس یا باطل) ندارد.</summary>
    public async Task DeleteAsync(short id, CancellationToken ct)
    {
        var year = await db.FiscalYears.Include(f => f.Periods).FirstOrDefaultAsync(f => f.Id == id, ct)
                   ?? throw new NotFoundException($"سال مالی {id} پیدا نشد.");
        var count = await db.Vouchers.CountAsync(v => v.FiscalYearId == id, ct);
        if (count > 0)
            throw new DomainException($"سال مالی {year.Title} {count} سند دارد و حذف نمی‌شود.");
        db.FiscalYears.Remove(year);
        await db.SaveChangesAsync(ct);
    }

    private static FiscalYearDto ToDto(FiscalYear f) => new(
        f.Id, f.Title, PersianDate.ToPersian(f.StartDate), PersianDate.ToPersian(f.EndDate), f.Status,
        f.Periods.OrderBy(p => p.PeriodNo)
            .Select(p => new FiscalPeriodDto(p.PeriodNo, p.Title, PersianDate.ToPersian(p.StartDate),
                PersianDate.ToPersian(p.EndDate), p.Status))
            .ToList());
}
