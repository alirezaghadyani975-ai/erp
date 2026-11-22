using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Inventory.Application;

/// <summary>فیلتر گزارش‌های انبار: بازه تاریخ (شمسی) و در صورت نیاز یک انبار.</summary>
public sealed record InventoryReportFilter(string? From, string? To, int? WarehouseId = null);

/// <summary>
/// گزارش‌های انبار مرحله ۳ (قالب مشترک `ReportTable` با سطح دوم): گردش کالا، رسید و حواله فاکتورنشده، مصرف به تفکیک مرکز و عامل،
/// کنترل موجودی ریالی با دفتر معین. مبالغ فقط از محاسبه بهای تمام‌شده است؛ ردیف محاسبه‌نشده ریال ندارد و جدا شمرده می‌شود.
/// </summary>
public class InventoryReportService(IInventoryDbContext db, IAccessControl access, TimeProvider clock)
{
    public static readonly (string Key, string Title, string Help)[] Types =
    [
        ("movement", "گردش کالا", "هر کالا: مانده ابتدای بازه، ورود، خروج و مانده پایان (تعدادی) و ریال ورود و خروج محاسبه‌شده؛ سطح دوم به تفکیک انبار."),
        ("uninvoiced", "رسید و حواله فاکتورنشده", "سندهای انباری که هنوز فاکتور یا برگشتشان صادر نشده، با سن (روز)؛ سطح دوم ردیف‌های فاکتورنشده."),
        ("consumption", "مصرف به تفکیک مرکز", "حواله‌های مصرف هر مرکز هزینه (ریال پس از محاسبه بهای تمام‌شده)؛ سطح دوم به تفکیک عامل هزینه."),
        ("ledger", "کنترل موجودی با دفتر", "موجودی ریالی آخرین ماه محاسبه‌شده تا «تا تاریخ» در برابر مانده معین موجودی همان تاریخ؛ سطح دوم به تفکیک نوع کالا."),
    ];

    /// <summary>انواع سندی که باید فاکتور یا برگشت بخورند.</summary>
    private static readonly StockDocType[] Invoiceable =
        [StockDocType.PurchaseReceipt, StockDocType.SalesIssue, StockDocType.SalesReturnReceipt, StockDocType.PurchaseReturnIssue];

    public async Task<ReportTable> RunAsync(string type, InventoryReportFilter f, CancellationToken ct)
    {
        access.Demand(InventoryPermissions.Reports);
        return type switch
        {
            "movement" => await MovementAsync(f, ct),
            "uninvoiced" => await UninvoicedAsync(f, ct),
            "consumption" => await ConsumptionAsync(f, ct),
            "ledger" => await LedgerAsync(f, ct),
            _ => throw new DomainException("نوع گزارش انبار نامعتبر است.")
        };
    }

    /// <summary>هشدارهای صفحه نخست: سندهای فاکتورنشده قدیمی‌تر از یک هفته و ماه‌های نیازمند محاسبه دوباره.</summary>
    public async Task<List<InventoryAlertDto>> AlertsAsync(CancellationToken ct)
    {
        var alerts = new List<InventoryAlertDto>();
        if (!access.Has(InventoryPermissions.Reports)) return alerts;
        var weekAgo = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(-7);
        var allowed = access.WarehouseIds();
        var old = await db.StockDocs.Where(d => Invoiceable.Contains(d.DocType) && d.DocDate <= weekAgo && (allowed == null || allowed.Contains(d.WarehouseId)))
            .Select(d => new { d.Id, LineIds = d.Lines.Select(l => l.Id).ToList() }).ToListAsync(ct);
        if (old.Count > 0)
        {
            var links = await db.InvoiceLinksAsync(old.SelectMany(d => d.LineIds).ToList(), ct);
            var count = old.Count(d => d.LineIds.Any(id => !links.ContainsKey(id)));
            if (count > 0)
                alerts.Add(new InventoryAlertDto($"{count} رسید یا حواله انبار بیش از یک هفته است فاکتور نشده است.",
                    "/inventory/reports?type=uninvoiced"));
        }
        var stale = await db.CostRuns.CountAsync(r => r.Status == CostRunStatus.Stale, ct);
        if (stale > 0)
            alerts.Add(new InventoryAlertDto($"بهای تمام‌شده {stale} ماه بعد از تغییر سندها نیازمند محاسبه دوباره است.", "/inventory/costing"));
        return alerts;
    }

    // ---------------- ابزار ----------------

    private sealed record ItemInfo(string Code, string Title, string Unit, byte? CategoryId)
    {
        public string Text => $"{Code} {Title}";
    }

    private async Task<Dictionary<int, ItemInfo>> ItemsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await (from i in db.Items
                      where list.Contains(i.Id)
                      join u in db.Units on i.UnitId equals u.Id
                      select new { i.Id, i.Code, i.Title, Unit = u.Title, i.CategoryId })
            .ToDictionaryAsync(x => x.Id, x => new ItemInfo(x.Code, x.Title, x.Unit, x.CategoryId), ct);
    }

    private async Task<Dictionary<int, string>> WarehousesAsync(CancellationToken ct) =>
        await db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Title, ct);

    private async Task<List<string>> SubtitleAsync(InventoryReportFilter f, bool range, CancellationToken ct)
    {
        var s = new List<string> { range ? $"از {Or(f.From, "ابتدا")} تا {Or(f.To, "امروز")}" : $"تا تاریخ {Or(f.To, "امروز")}" };
        if (f.WarehouseId is int w) s.Add($"انبار: {await db.Warehouses.Where(x => x.Id == w).Select(x => x.Title).FirstOrDefaultAsync(ct)}");
        return s;
    }

    private sealed record MoveLine(bool In, DateOnly Date, int WarehouseId, int ItemId, decimal Qty, decimal? Amount);

    private sealed record ConsLine(long DocId, int? CostCenterId, int? CostFactorId, decimal? Amount);

    private static string Or(string? v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v;

    private static ReportRow Row(List<ReportRow>? children, params (string Key, object? Value)[] cells) =>
        new(cells.ToDictionary(c => c.Key, c => c.Value), children);

    private static ReportColumn Text(string key, string title) => new(key, title);
    private static ReportColumn Code(string key, string title) => new(key, title, ReportColumnKind.Code);
    private static ReportColumn Num(string key, string title, string? group = null, bool sum = true) => new(key, title, ReportColumnKind.Number, group, sum);
    private static ReportColumn Amt(string key, string title, string? group = null) => new(key, title, ReportColumnKind.Amount, group);

    // ---------------- گزارش‌ها ----------------

    private async Task<ReportTable> MovementAsync(InventoryReportFilter f, CancellationToken ct)
    {
        DateOnly? from = PersianDate.TryParse(f.From, out var a) ? a : null;
        var to = PersianDate.TryParse(f.To, out var b) ? b : DateOnly.MaxValue;
        var docs = db.StockDocs.Where(d => d.DocDate <= to);
        if (f.WarehouseId is int w) { access.DemandScope(ScopeKind.Warehouse, w); docs = docs.Where(d => d.WarehouseId == w); }
        if (access.WarehouseIds() is { } allowed) docs = docs.Where(d => allowed.Contains(d.WarehouseId));
        var lines = (await docs.SelectMany(d => d.Lines.Select(l => new { d.DocType, d.DocDate, d.WarehouseId, l.ItemId, l.Quantity, l.Amount }))
            .ToListAsync(ct)).Select(l => new MoveLine(l.DocType.IsReceipt(), l.DocDate, l.WarehouseId, l.ItemId, l.Quantity, l.Amount)).ToList();
        var items = await ItemsAsync(lines.Select(l => l.ItemId), ct);
        var warehouses = await WarehousesAsync(ct);

        (string, object?)[] Cells(string title, string unit, IEnumerable<MoveLine> rows)
        {
            var list = rows.ToList();
            bool InRange(MoveLine l) => from is null || l.Date >= from.Value;
            var before = list.Where(l => !InRange(l)).ToList();
            var opening = before.Where(l => l.In).Sum(l => l.Qty) - before.Where(l => !l.In).Sum(l => l.Qty);
            var range = list.Where(InRange).ToList();
            var inQty = range.Where(l => l.In).Sum(l => l.Qty);
            var outQty = range.Where(l => !l.In).Sum(l => l.Qty);
            return [("title", title), ("unit", unit), ("opening", opening), ("in", inQty), ("out", outQty), ("closing", opening + inQty - outQty),
                    ("inAmount", range.Where(l => l.In).Sum(l => l.Amount ?? 0)), ("outAmount", range.Where(l => !l.In).Sum(l => l.Amount ?? 0)),
                    ("uncosted", (decimal)range.Count(l => l.Amount == null))];
        }

        var rows = lines.GroupBy(l => l.ItemId).OrderBy(g => items.GetValueOrDefault(g.Key)?.Code).Select(g =>
        {
            var item = items.GetValueOrDefault(g.Key) ?? new ItemInfo("?", "?", "", null);
            return Row(g.GroupBy(l => l.WarehouseId).OrderBy(x => warehouses.GetValueOrDefault(x.Key))
                    .Select(x => Row(null, Cells(warehouses.GetValueOrDefault(x.Key, "?"), item.Unit, x))).ToList(),
                Cells(item.Text, item.Unit, g));
        }).ToList();
        return new ReportTable("گردش کالا", await SubtitleAsync(f, true, ct),
            [Text("title", "کالا / انبار"), Text("unit", "واحد"), Num("opening", "مانده ابتدا", "مقدار", false), Num("in", "ورود", "مقدار", false),
             Num("out", "خروج", "مقدار", false), Num("closing", "مانده پایان", "مقدار", false),
             Amt("inAmount", "ورود", "ریال (محاسبه‌شده)"), Amt("outAmount", "خروج", "ریال (محاسبه‌شده)"), Num("uncosted", "ردیف بی‌بها")],
            rows, true, "به تفکیک انبار");
    }

    private async Task<ReportTable> UninvoicedAsync(InventoryReportFilter f, CancellationToken ct)
    {
        var docs = db.StockDocs.Where(d => Invoiceable.Contains(d.DocType));
        if (PersianDate.TryParse(f.From, out var from)) docs = docs.Where(d => d.DocDate >= from);
        if (PersianDate.TryParse(f.To, out var to)) docs = docs.Where(d => d.DocDate <= to);
        if (f.WarehouseId is int w) { access.DemandScope(ScopeKind.Warehouse, w); docs = docs.Where(d => d.WarehouseId == w); }
        if (access.WarehouseIds() is { } allowed) docs = docs.Where(d => allowed.Contains(d.WarehouseId));
        var list = await docs.OrderBy(d => d.DocDate).ThenBy(d => d.DocNo)
            .Select(d => new { d.Id, d.DocType, d.DocNo, d.DocDate, d.WarehouseId, d.PartyId, Lines = d.Lines.Select(l => new { l.Id, l.ItemId, l.Quantity }).ToList() })
            .ToListAsync(ct);
        var links = await db.InvoiceLinksAsync(list.SelectMany(d => d.Lines.Select(l => l.Id)).ToList(), ct);
        var open = list.Select(d => new { Doc = d, Lines = d.Lines.Where(l => !links.ContainsKey(l.Id)).ToList() }).Where(x => x.Lines.Count > 0).ToList();
        var items = await ItemsAsync(open.SelectMany(x => x.Lines.Select(l => l.ItemId)), ct);
        var warehouses = await WarehousesAsync(ct);
        var partyIds = open.Select(x => x.Doc.PartyId).OfType<int>().Distinct().ToList();
        var parties = await db.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var rows = open.Select(x => Row(
            x.Lines.Select(l => Row(null, ("title", items.GetValueOrDefault(l.ItemId)?.Text), ("no", null), ("date", null), ("party", null),
                ("warehouse", null), ("days", null), ("lines", 1m), ("qty", l.Quantity), ("unit", items.GetValueOrDefault(l.ItemId)?.Unit))).ToList(),
            ("title", x.Doc.DocType.Title()), ("no", x.Doc.DocNo.ToString("000000")), ("date", PersianDate.ToPersian(x.Doc.DocDate)),
            ("party", x.Doc.PartyId is int p ? parties.GetValueOrDefault(p) : null), ("warehouse", warehouses.GetValueOrDefault(x.Doc.WarehouseId)),
            ("days", (decimal)(today.DayNumber - x.Doc.DocDate.DayNumber)), ("lines", (decimal)x.Lines.Count), ("qty", null), ("unit", null))).ToList();
        return new ReportTable("رسید و حواله فاکتورنشده", await SubtitleAsync(f, true, ct),
            [Text("title", "نوع سند / کالا"), Code("no", "شماره"), Code("date", "تاریخ"), Text("party", "طرف حساب"), Text("warehouse", "انبار"),
             Num("days", "سن (روز)", sum: false), Num("lines", "ردیف فاکتورنشده"), Num("qty", "مقدار", sum: false), Text("unit", "واحد")],
            rows, true, "ردیف‌های فاکتورنشده");
    }

    private async Task<ReportTable> ConsumptionAsync(InventoryReportFilter f, CancellationToken ct)
    {
        var docs = db.StockDocs.Where(d => d.DocType == StockDocType.Consumption);
        if (PersianDate.TryParse(f.From, out var from)) docs = docs.Where(d => d.DocDate >= from);
        if (PersianDate.TryParse(f.To, out var to)) docs = docs.Where(d => d.DocDate <= to);
        if (f.WarehouseId is int w) { access.DemandScope(ScopeKind.Warehouse, w); docs = docs.Where(d => d.WarehouseId == w); }
        if (access.WarehouseIds() is { } allowed) docs = docs.Where(d => allowed.Contains(d.WarehouseId));
        var lines = (await docs.SelectMany(d => d.Lines.Select(l => new { l.CostCenterId, l.CostFactorId, l.Amount, d.Id })).ToListAsync(ct))
            .Select(l => new ConsLine(l.Id, l.CostCenterId, l.CostFactorId, l.Amount)).ToList();
        var centers = await db.Centers.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var factors = await db.Factors.ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        (string, object?)[] Cells(string title, IEnumerable<ConsLine> ls)
        {
            var list = ls.ToList();
            return [("title", title), ("docs", (decimal)list.Select(l => l.DocId).Distinct().Count()), ("lines", (decimal)list.Count),
                    ("amount", list.Sum(l => l.Amount ?? 0)), ("uncosted", (decimal)list.Count(l => l.Amount == null))];
        }
        var rows = lines.GroupBy(l => l.CostCenterId).OrderBy(g => g.Key is int c ? centers.GetValueOrDefault(c) : "").Select(g => Row(
            g.GroupBy(l => l.CostFactorId).OrderBy(x => x.Key is int fa ? factors.GetValueOrDefault(fa) : "")
                .Select(x => Row(null, Cells(x.Key is int fa ? factors.GetValueOrDefault(fa, "?") : "بدون عامل", x))).ToList(),
            Cells(g.Key is int c ? centers.GetValueOrDefault(c, "?") : "بدون مرکز (هزینه مالی)", g))).ToList();
        return new ReportTable("مصرف به تفکیک مرکز هزینه", await SubtitleAsync(f, true, ct),
            [Text("title", "مرکز / عامل هزینه"), Num("docs", "تعداد حواله", sum: false), Num("lines", "تعداد ردیف"), Amt("amount", "مبلغ (محاسبه‌شده)"),
             Num("uncosted", "ردیف بی‌بها")],
            rows, false, "به تفکیک عامل");
    }

    private async Task<ReportTable> LedgerAsync(InventoryReportFilter f, CancellationToken ct)
    {
        var to = PersianDate.TryParse(f.To, out var b) ? b : DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var run = await db.CostRuns.Where(r => r.EndDate <= to).OrderByDescending(r => r.EndDate).FirstOrDefaultAsync(ct);
        var subtitle = new List<string>();
        if (run is null)
        {
            subtitle.Add($"تا {PersianDate.ToPersian(to)} هیچ ماهی محاسبه نشده است.");
            return new ReportTable("کنترل موجودی با دفتر معین", subtitle, [Text("title", "معین")], []);
        }
        subtitle.Add($"موجودی پایان {PersianDate.ToPersian(run.EndDate)} (آخرین ماه محاسبه‌شده){(run.Status == CostRunStatus.Stale ? " — نیازمند محاسبه دوباره" : "")}");
        subtitle.Add("اختلاف: رسید فاکتورنشده (بی‌بها)، سند دستی روی معین موجودی یا ماه محاسبه‌نشده.");

        if (f.WarehouseId is int wh) access.DemandScope(ScopeKind.Warehouse, wh);
        var allowed = access.WarehouseIds();
        // کاربر محدود به چند انبار، مانده دفتر کل شرکت را با موجودی بخشی از انبارها مقایسه نمی‌کند
        var partial = f.WarehouseId is not null || allowed is not null;
        var balances = await db.CostBalances.Where(x => x.RunId == run.Id && (f.WarehouseId == null || x.WarehouseId == f.WarehouseId))
            .Where(x => allowed == null || allowed.Contains(x.WarehouseId))
            .Select(x => new { x.ItemId, x.Amount }).ToListAsync(ct);
        var items = await ItemsAsync(balances.Select(x => x.ItemId), ct);
        var categories = await db.ItemCategories.ToDictionaryAsync(c => c.Id, c => new { c.Title, c.InventoryAccountId }, ct);
        var accountIds = categories.Values.Select(c => c.InventoryAccountId).OfType<int>().Distinct().ToList();
        var accounts = await db.Accounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Title}", ct);
        var ledger = await db.LedgerBalancesAsync(accountIds, run.EndDate, ct);

        var byCategory = balances.GroupBy(x => items.GetValueOrDefault(x.ItemId)?.CategoryId)
            .ToDictionary(g => g.Key ?? 0, g => g.Sum(x => x.Amount));
        var rows = accountIds.OrderBy(id => accounts.GetValueOrDefault(id)).Select(id =>
        {
            var cats = categories.Where(c => c.Value.InventoryAccountId == id).ToList();
            var stock = cats.Sum(c => byCategory.GetValueOrDefault(c.Key));
            var book = ledger.GetValueOrDefault(id);
            return Row(cats.Select(c => Row(null, ("title", c.Value.Title), ("stock", byCategory.GetValueOrDefault(c.Key)), ("ledger", null), ("diff", null))).ToList(),
                ("title", accounts.GetValueOrDefault(id, "?")), ("stock", stock), ("ledger", partial ? null : book),
                ("diff", partial ? null : book - stock));
        }).ToList();
        if (partial) subtitle.Add("با فیلتر یا محدوده انبار، مانده دفتر (که انبار ندارد) نمایش داده نمی‌شود.");
        return new ReportTable("کنترل موجودی با دفتر معین", subtitle,
            [Text("title", "معین / نوع کالا"), Amt("stock", "موجودی ریالی انبار"), Amt("ledger", "مانده دفتر معین"), Amt("diff", "اختلاف (دفتر − انبار)")],
            rows, false, "به تفکیک نوع کالا");
    }
}

public sealed record InventoryAlertDto(string Text, string Url);
