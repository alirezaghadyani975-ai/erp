using Erp.Accounting.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Abstractions;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Inventory.Application;

/// <summary>
/// محاسبه بهای تمام‌شده ماهانه با میانگین موزون متحرک به تفکیک کالا × انبار و صدور سند ماه.
/// <para>ترتیب در هر روز: رسید خرید و اول دوره، بعد حواله‌ها، بعد رسید برگشت از فروش (که بهایش از حواله مبدأ می‌آید).</para>
/// <para>بها: اول دوره = فی واردشده؛ رسید خرید = مبلغ ردیف فاکتور خرید (بدون فاکتور: صفر و در گزارش اطلاعات ناقص)؛
/// حواله فروش و مصرف = میانگین لحظه؛ برگشت‌ها = بهای ردیف مبدأ. حواله‌ای که آخرین مقدار را می‌برد باقی مبلغ را می‌گیرد.</para>
/// <para>سند ماه: فروش (بدهکار بهای تمام‌شده گروه، بستانکار موجودی)، مصرف (بدهکار هزینه عامل/مرکز)، برگشت از فروش (برعکس فروش).
/// رسید خرید و حواله برگشت از خرید را فاکتورها ثبت کرده‌اند و اول دوره سند افتتاحیه دارد.</para>
/// </summary>
public class StockCostingService(IInventoryDbContext db, IVoucherPosting posting, IUnitOfWork uow, ICurrentUser user, TimeProvider clock,
    IAccessControl access)
{
    /// <summary>دوره‌های یک سال مالی با وضعیت محاسبه و سند هر ماه.</summary>
    public async Task<List<CostPeriodDto>> PeriodsAsync(short fiscalYearId, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Costing);
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYearId == fiscalYearId).OrderBy(p => p.PeriodNo).ToListAsync(ct);
        if (periods.Count == 0) return [];
        var periodIds = periods.Select(p => p.Id).ToList();
        var runs = await db.CostRuns.AsNoTracking().Where(r => periodIds.Contains(r.FiscalPeriodId)).ToDictionaryAsync(r => r.FiscalPeriodId, ct);
        var vouchers = await posting.FindAsync(CostRun.Module, CostRun.DocType, runs.Values.Select(r => (long)r.Id).ToList(), ct);

        var (from, to) = (periods[0].StartDate, periods[^1].EndDate);
        var lines = await db.StockDocs.AsNoTracking().Where(d => d.DocDate >= from && d.DocDate <= to)
            .SelectMany(d => d.Lines.Select(l => new { d.DocDate, In = (byte)d.DocType < 10, Amount = l.Amount ?? 0 }))
            .ToListAsync(ct);

        return periods.Select(p =>
        {
            var inPeriod = lines.Where(l => l.DocDate >= p.StartDate && l.DocDate <= p.EndDate).ToList();
            var run = runs.GetValueOrDefault(p.Id);
            var voucher = run is null ? null : vouchers.GetValueOrDefault(run.Id);
            return new CostPeriodDto(p.Id, p.PeriodNo, p.Title, PersianDate.ToPersian(p.StartDate), PersianDate.ToPersian(p.EndDate),
                inPeriod.Count > 0, run?.Status, run is null ? null : PersianDate.ToPersian(DateOnly.FromDateTime(run.RunAt)),
                voucher?.VoucherId, voucher?.TempNo, voucher?.Status,
                inPeriod.Where(l => !l.In).Sum(l => l.Amount), inPeriod.Where(l => l.In).Sum(l => l.Amount));
        }).ToList();
    }

    /// <summary>
    /// محاسبه همه ماه‌های لازم تا این دوره (از اولین ماه دارای سند انبار): ماهی که محاسبه نشده یا «نیازمند محاسبه دوباره» است،
    /// و خود این دوره همیشه. هر ماه در تراکنش خودش.
    /// </summary>
    public async Task<List<CostRunResultDto>> RunUpToAsync(int fiscalPeriodId, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Costing);
        var target = await db.FiscalPeriods.FirstOrDefaultAsync(p => p.Id == fiscalPeriodId, ct) ?? throw new NotFoundException("دوره مالی پیدا نشد.");
        var first = await db.StockDocs.MinAsync(d => (DateOnly?)d.DocDate, ct);
        if (first is null || first > target.EndDate) return [await RunAsync(target, ct)];

        var periods = await db.FiscalPeriods.Where(p => p.EndDate >= first && p.EndDate <= target.EndDate).OrderBy(p => p.StartDate).ToListAsync(ct);
        var periodIds = periods.Select(p => p.Id).ToList();
        var runs = await db.CostRuns.AsNoTracking().Where(r => periodIds.Contains(r.FiscalPeriodId))
            .ToDictionaryAsync(r => r.FiscalPeriodId, r => r.Status, ct);

        var results = new List<CostRunResultDto>();
        var rerun = false; // بعد از محاسبه دوباره یک ماه، ماه‌های بعد هم دوباره
        foreach (var p in periods)
        {
            if (!rerun && p.Id != target.Id && runs.GetValueOrDefault(p.Id) == CostRunStatus.Computed) continue;
            results.Add(await RunAsync(p, ct));
            rerun = true;
        }
        return results;
    }

    private sealed record Line(long Id, StockDocType Type, DateOnly Date, long DocId, int DocNo, short RowNo, int ItemId, int WarehouseId, decimal Quantity,
        long? SourceLineId, int? FactorId, int? CenterId, decimal? EnteredPrice);

    private async Task<CostRunResultDto> RunAsync(FiscalPeriodRef period, CancellationToken ct)
    {
        var existing = await db.CostRuns.FirstOrDefaultAsync(r => r.FiscalPeriodId == period.Id, ct);
        if (existing is not null) await posting.EnsureChangeableAsync(CostRun.Module, CostRun.DocType, existing.Id, ct);

        // مانده اول ماه از اجرای ماه قبل
        var opening = new Dictionary<(int ItemId, int WarehouseId), (decimal Qty, decimal Amount)>();
        if (await db.StockDocs.AnyAsync(d => d.DocDate < period.StartDate, ct))
        {
            var previous = await db.FiscalPeriods.Where(p => p.EndDate < period.StartDate).OrderByDescending(p => p.EndDate).FirstOrDefaultAsync(ct)
                           ?? throw new DomainException("دوره مالی پیش از این ماه تعریف نشده است.");
            var prevRun = await db.CostRuns.AsNoTracking().FirstOrDefaultAsync(r => r.FiscalPeriodId == previous.Id, ct);
            if (prevRun is not { Status: CostRunStatus.Computed })
                throw new DomainException($"اول بهای تمام‌شده دوره «{previous.Title}» ({PersianDate.ToPersian(previous.StartDate)} تا " +
                                          $"{PersianDate.ToPersian(previous.EndDate)}) را محاسبه کنید.");
            opening = await db.CostBalances.AsNoTracking().Where(b => b.RunId == prevRun.Id)
                .ToDictionaryAsync(b => (b.ItemId, b.WarehouseId), b => (b.Quantity, b.Amount), ct);
        }

        var lines = (await db.StockDocs.AsNoTracking().Where(d => d.DocDate >= period.StartDate && d.DocDate <= period.EndDate)
                .SelectMany(d => d.Lines.Select(l => new
                {
                    l.Id, d.DocType, d.DocDate, DocId = d.Id, d.DocNo, l.RowNo, l.ItemId, d.WarehouseId, l.Quantity, l.SourceLineId,
                    l.CostFactorId, l.CostCenterId, l.EnteredPrice
                }))
                .ToListAsync(ct))
            .Select(l => new Line(l.Id, l.DocType, l.DocDate, l.DocId, l.DocNo, l.RowNo, l.ItemId, l.WarehouseId, l.Quantity, l.SourceLineId,
                l.CostFactorId, l.CostCenterId, l.EnteredPrice))
            .OrderBy(l => l.Date).ThenBy(l => Phase(l.Type)).ThenBy(l => l.DocId).ThenBy(l => l.RowNo)
            .ToList();

        // بهای رسیدهای خرید (فقط از فاکتور خرید) و ردیف‌های مبدأ برگشت‌ها (اگر ماه قبل محاسبه شده‌اند).
        // هیچ بهایی از فی قبلی یا تقریبی حساب نمی‌شود: اطلاعات ناقص = صفر، و در گزارش بعد از محاسبه می‌آید.
        var receiptIds = lines.Where(l => l.Type == StockDocType.PurchaseReceipt).Select(l => l.Id).ToList();
        var invoiced = await db.ReceiptInvoiceAmountsAsync(receiptIds, ct);
        var lineIds = lines.Select(l => l.Id).ToHashSet();
        var outsideSources = lines.Where(l => l.SourceLineId is long s && !lineIds.Contains(s)).Select(l => l.SourceLineId!.Value).Distinct().ToList();
        var sourceCosts = await db.StockDocs.AsNoTracking().SelectMany(d => d.Lines)
            .Where(l => outsideSources.Contains(l.Id))
            .Select(l => new { l.Id, l.UnitCost }).ToDictionaryAsync(l => l.Id, l => l.UnitCost, ct);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var itemTitles = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => $"{i.Code} - {i.Title}", ct);

        var state = new Dictionary<(int, int), (decimal Qty, decimal Amount)>(opening);
        var costs = new Dictionary<long, (decimal Unit, decimal Amount)>();
        var warnings = new List<CostWarningDto>();
        void Warn(Line l, string problem) => warnings.Add(new CostWarningDto(PersianDate.ToPersian(l.Date), l.Type, l.DocId, l.DocNo, l.RowNo,
            itemTitles.GetValueOrDefault(l.ItemId, "?"), l.Quantity, problem));

        foreach (var l in lines)
        {
            var key = (l.ItemId, l.WarehouseId);
            var (qty, amount) = state.GetValueOrDefault(key);
            decimal value;
            if (l.Type.IsReceipt())
            {
                value = l.Type switch
                {
                    StockDocType.Opening => TradeMath.Rial((l.EnteredPrice ?? 0) * l.Quantity),
                    StockDocType.PurchaseReceipt when invoiced.TryGetValue(l.Id, out var inv) => inv,
                    StockDocType.PurchaseReceipt => Missing(l, "فاکتور خرید ندارد؛ بها صفر حساب شد."),
                    _ => TradeMath.Rial(SourceUnit(l) * l.Quantity) // برگشت از فروش
                };
                if (l.Type == StockDocType.Opening && value == 0) Warn(l, "فی موجودی اول دوره صفر است.");
                state[key] = (qty + l.Quantity, amount + value);
            }
            else
            {
                if (l.Quantity > qty)
                    throw new DomainException($"موجودی کالای ردیف {l.RowNo} سند {l.Type.Title()} در {PersianDate.ToPersian(l.Date)} کافی نیست " +
                                              "(داده ناسازگار؛ سندهای این کالا را بررسی کنید).");
                value = l.Quantity == qty ? amount
                    : l.Type == StockDocType.PurchaseReturnIssue ? TradeMath.Rial(SourceUnit(l) * l.Quantity)
                    : TradeMath.Rial(amount * l.Quantity / qty);
                state[key] = (qty - l.Quantity, amount - value);
            }
            costs[l.Id] = (Math.Round(value / l.Quantity, 4), value);
        }

        await using var tx = await uow.BeginAsync(ct);
        var run = existing ?? new CostRun(period.Id, period.StartDate, period.EndDate);
        run.Done(user.UserId, clock.GetUtcNow().UtcDateTime);
        if (existing is null) db.Add(run);
        await db.SaveChangesAsync(ct);

        await db.CostBalances.Where(b => b.RunId == run.Id).ExecuteDeleteAsync(ct);
        foreach (var ((itemId, warehouseId), (qty, amount)) in state.Where(s => s.Value.Qty != 0 || s.Value.Amount != 0))
            db.Add(new CostBalance(run.Id, itemId, warehouseId, qty, amount));
        await db.SaveChangesAsync(ct);
        await db.WriteCostsAsync(costs.Select(c => (c.Key, c.Value.Unit, c.Value.Amount)).ToList(), ct);
        await db.MarkStaleAfterAsync(period.EndDate, ct);

        var request = await PostingRequestAsync(run, period, lines, costs, ct);
        PostedVoucher? voucher = null;
        if (request.Lines.Count > 0) voucher = await posting.PostAsync(request, ct);
        else await posting.UnpostAsync(CostRun.Module, CostRun.DocType, run.Id, ct);
        await tx.CommitAsync(ct);

        return new CostRunResultDto(run.Id, lines.Count, lines.Where(l => !l.Type.IsReceipt()).Sum(l => costs[l.Id].Amount),
            lines.Where(l => l.Type.IsReceipt()).Sum(l => costs[l.Id].Amount), voucher?.VoucherId, voucher?.TempNo, warnings, period.Title);

        decimal Missing(Line l, string problem)
        {
            Warn(l, problem);
            return 0;
        }

        decimal SourceUnit(Line l)
        {
            if (l.SourceLineId is not long s) return 0;
            if (costs.TryGetValue(s, out var c)) return c.Unit;
            if (sourceCosts.GetValueOrDefault(s) is { } unit) return unit;
            Missing(l, "بهای ردیف مبدأ برگشت محاسبه نشده است؛ بها صفر حساب شد.");
            return 0;
        }
    }

    /// <summary>ترتیب داخل یک روز: رسیدهای مستقل، حواله‌ها، برگشت از فروش.</summary>
    private static int Phase(StockDocType type) => type switch
    {
        StockDocType.Opening or StockDocType.PurchaseReceipt => 0,
        StockDocType.SalesReturnReceipt => 2,
        _ => 1
    };

    private async Task<PostingRequest> PostingRequestAsync(CostRun run, FiscalPeriodRef period, List<Line> lines,
        Dictionary<long, (decimal Unit, decimal Amount)> costs, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var accounts = await db.Items.Where(i => itemIds.Contains(i.Id))
            .Join(db.ItemGroups, i => i.GroupId, g => g.Id, (i, g) => new { i.Id, i.Title, g.PurchaseAccountId, g.CostAccountId })
            .ToDictionaryAsync(x => x.Id, ct);

        var sums = new Dictionary<(string Key, int? Account, int? Factor, int? Center), decimal>();
        void Add(string key, int? account, int? factor, int? center, decimal amount)
        {
            if (amount == 0) return;
            sums[(key, account, factor, center)] = sums.GetValueOrDefault((key, account, factor, center)) + amount;
        }

        foreach (var l in lines)
        {
            var amount = costs[l.Id].Amount;
            var a = accounts[l.ItemId];
            var inventory = a.PurchaseAccountId ?? throw new DomainException($"گروه کالای «{a.Title}» معین موجودی ندارد.");
            switch (l.Type)
            {
                case StockDocType.SalesIssue:
                    Add("cogs", a.CostAccountId ?? throw new DomainException($"گروه کالای «{a.Title}» معین بهای تمام‌شده ندارد."), null, null, amount);
                    Add("inventory_out", inventory, null, null, amount);
                    break;
                case StockDocType.Consumption:
                    Add("consumption", null, l.FactorId, l.CenterId, amount);
                    Add("inventory_out", inventory, null, null, amount);
                    break;
                case StockDocType.SalesReturnReceipt:
                    Add("inventory_in", inventory, null, null, amount);
                    Add("cogs_return", a.CostAccountId ?? throw new DomainException($"گروه کالای «{a.Title}» معین بهای تمام‌شده ندارد."), null, null, amount);
                    break;
            }
        }

        var postingLines = sums.Select(s => new PostingLine(s.Key.Key, s.Value,
                s.Key.Key == "consumption"
                    ? s.Key.Center is int c
                        ? [new PostingDetail(PostingDetailKind.CostFactor, s.Key.Factor!.Value), new PostingDetail(PostingDetailKind.CostCenter, c)]
                        : [new PostingDetail(PostingDetailKind.CostFactor, s.Key.Factor!.Value)]
                    : [],
                s.Key.Key switch
                {
                    "cogs" => "بهای تمام‌شده کالای فروش‌رفته",
                    "consumption" => "مصرف کالا",
                    "inventory_in" => "برگشت کالا از فروش",
                    "cogs_return" => "برگشت بهای تمام‌شده فروش",
                    _ => "خروج کالا از انبار"
                },
                AccountId: s.Key.Account))
            .ToList();

        return new PostingRequest(CostRun.Module, CostRun.DocType, run.Id, period.EndDate,
            $"بهای تمام‌شده انبار {period.Title} ({PersianDate.ToPersian(period.StartDate)} تا {PersianDate.ToPersian(period.EndDate)})",
            postingLines);
    }
}
