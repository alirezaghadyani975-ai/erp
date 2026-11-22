using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Sales.Application;

/// <summary>فیلتر گزارش‌های فروش: بازه تاریخ (شمسی)، و در صورت نیاز یک مشتری یا یک کالا.</summary>
public sealed record SalesReportFilter(string? From, string? To, int? CustomerId = null, int? ItemId = null);

/// <summary>
/// گزارش‌های فروش با سطح دوم. «خالص» = مبلغ پس از تخفیف منهای برگشت‌ها (برگشت به تاریخ خودش در بازه می‌آید).
/// بهای فروش از ردیف حواله فروش (و رسید برگشت) بعد از محاسبه بهای تمام‌شده ماه؛ ردیف محاسبه‌نشده بها ندارد و شمرده می‌شود.
/// </summary>
public class SalesReportService(ISalesDbContext db, IAccessControl access)
{
    public static readonly (string Key, string Title, string Help)[] Types =
    [
        ("items", "فروش کالاها", "هر کالا: مقدار و مبلغ فروش، تخفیف، برگشت و فروش خالص؛ سطح دوم به تفکیک مشتری."),
        ("customers", "فروش مشتریان", "هر مشتری: تعداد فاکتور، فروش، برگشت و خالص؛ سطح دوم به تفکیک کالا."),
        ("monthly", "فروش ماهانه", "هر ماه: فروش، تخفیف، برگشت، خالص و ارزش افزوده؛ سطح دوم به تفکیک روز."),
        ("profit", "سود ناخالص کالاها", "فروش خالص در برابر بهای تمام‌شده هر کالا و حاشیه سود؛ سطح دوم به تفکیک مشتری. فقط ماه‌های محاسبه‌شده بها دارند."),
        ("vat", "ارزش افزوده فروش", "مالیات و عوارض ارزش افزوده فاکتورها و برگشت‌ها به تفکیک ماه؛ سطح دوم هر فاکتور و برگشت."),
    ];

    public async Task<ReportTable> RunAsync(string type, SalesReportFilter f, CancellationToken ct)
    {
        access.Demand(SalesPermissions.ReportView);
        var facts = await FactsAsync(f, ct);
        var items = await db.Items.ToDictionaryAsync(i => i.Id, i => (Text: i.Code + " " + i.Title, Unit: i.UnitTitle), ct);
        var customerIds = facts.Select(x => x.CustomerId).Distinct().ToList();
        var customers = await db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        string Item(int id) => items.TryGetValue(id, out var i) ? i.Text : "?";
        string Customer(int id) => customers.GetValueOrDefault(id, "?");
        var subtitle = await SubtitleAsync(f, Item, ct);

        List<Measure<Fact>> Amounts() =>
        [
            new("gross", r => r.Where(x => !x.IsReturn).Sum(x => x.Gross)),
            new("discount", r => r.Where(x => !x.IsReturn).Sum(x => x.Discount)),
            new("sales", r => r.Where(x => !x.IsReturn).Sum(x => x.Net)),
            new("returns", r => r.Where(x => x.IsReturn).Sum(x => x.Net)),
            new("net", r => r.Sum(x => x.Signed)),
        ];
        List<ReportColumn> AmountColumns() =>
        [
            new("gross", "ناخالص", ReportColumnKind.Amount), new("discount", "تخفیف", ReportColumnKind.Amount),
            new("sales", "فروش پس از تخفیف", ReportColumnKind.Amount), new("returns", "برگشت", ReportColumnKind.Amount),
            new("net", "فروش خالص", ReportColumnKind.Amount),
        ];

        switch (type)
        {
            case "items":
            {
                var measures = new List<Measure<Fact>>
                {
                    new("unit", r => items.TryGetValue(r[0].ItemId, out var i) ? i.Unit : ""),
                    new("qty", r => r.Where(x => !x.IsReturn).Sum(x => x.Qty)),
                    new("retQty", r => r.Where(x => x.IsReturn).Sum(x => x.Qty)),
                };
                measures.AddRange(Amounts());
                return new ReportTable("فروش کالاها", subtitle,
                    [new("title", "کالا / مشتری"), new("unit", "واحد"), new("qty", "مقدار فروش", ReportColumnKind.Number, Sum: false),
                     new("retQty", "مقدار برگشت", ReportColumnKind.Number, Sum: false), .. AmountColumns()],
                    ReportBuilder.TwoLevel(facts, x => x.ItemId, Item, x => x.CustomerId, Customer, measures), true, "به تفکیک مشتری");
            }
            case "customers":
            {
                var measures = new List<Measure<Fact>> { new("invoices", r => (decimal)r.Where(x => !x.IsReturn).Select(x => x.DocId).Distinct().Count()) };
                measures.AddRange(Amounts());
                measures.Add(new("vat", r => r.Sum(x => x.IsReturn ? -x.Vat : x.Vat)));
                return new ReportTable("فروش مشتریان", subtitle,
                    [new("title", "مشتری / کالا"), new("invoices", "تعداد فاکتور", ReportColumnKind.Number, Sum: false), .. AmountColumns(),
                     new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => x.CustomerId, Customer, x => x.ItemId, Item, measures), true, "به تفکیک کالا");
            }
            case "monthly":
            {
                var measures = new List<Measure<Fact>> { new("invoices", r => (decimal)r.Where(x => !x.IsReturn).Select(x => x.DocId).Distinct().Count()) };
                measures.AddRange(Amounts());
                measures.Add(new("vat", r => r.Sum(x => x.IsReturn ? -x.Vat : x.Vat)));
                return new ReportTable("فروش ماهانه", subtitle,
                    [new("title", "ماه / روز"), new("invoices", "تعداد فاکتور", ReportColumnKind.Number), .. AmountColumns(),
                     new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => Month(x.Date), m => m, x => PersianDate.ToPersian(x.Date), d => d, measures), true, "به تفکیک روز");
            }
            case "profit":
            {
                var measures = new List<Measure<Fact>>
                {
                    new("net", r => r.Sum(x => x.Signed)),
                    new("cost", r => r.Sum(x => (x.IsReturn ? -1 : 1) * (x.Cost ?? 0))),
                    new("profit", r => r.Sum(x => x.Signed) - r.Sum(x => (x.IsReturn ? -1 : 1) * (x.Cost ?? 0))),
                    new("margin", r => ReportBuilder.Percent(r.Sum(x => x.Signed) - r.Sum(x => (x.IsReturn ? -1 : 1) * (x.Cost ?? 0)), r.Sum(x => x.Signed))),
                    new("uncosted", r => (decimal)r.Count(x => x.Cost is null)),
                };
                return new ReportTable("سود ناخالص کالاها", [.. subtitle, "ردیف بی‌بها: حواله فروشی که ماهش هنوز محاسبه نشده؛ سود آن ردیف‌ها کامل نیست."],
                    [new("title", "کالا / مشتری"), new("net", "فروش خالص", ReportColumnKind.Amount), new("cost", "بهای تمام‌شده", ReportColumnKind.Amount),
                     new("profit", "سود ناخالص", ReportColumnKind.Amount), new("margin", "حاشیه سود", ReportColumnKind.Percent),
                     new("uncosted", "ردیف بی‌بها", ReportColumnKind.Number)],
                    ReportBuilder.TwoLevel(facts.Where(x => x.IsGoods), x => x.ItemId, Item, x => x.CustomerId, Customer, measures), false, "به تفکیک مشتری");
            }
            case "vat":
            {
                var measures = new List<Measure<Fact>>
                {
                    new("base", r => r.Where(x => x.Vat != 0).Sum(x => x.Signed)),
                    new("salesVat", r => r.Where(x => !x.IsReturn).Sum(x => x.Vat)),
                    new("returnVat", r => r.Where(x => x.IsReturn).Sum(x => x.Vat)),
                    new("vat", r => r.Sum(x => x.IsReturn ? -x.Vat : x.Vat)),
                    new("exempt", r => r.Where(x => x.Vat == 0).Sum(x => x.Signed)),
                };
                return new ReportTable("ارزش افزوده فروش", subtitle,
                    [new("title", "ماه / سند"), new("base", "مبلغ مشمول (خالص)", ReportColumnKind.Amount), new("salesVat", "ارزش افزوده فروش", ReportColumnKind.Amount),
                     new("returnVat", "ارزش افزوده برگشت", ReportColumnKind.Amount), new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount),
                     new("exempt", "فروش معاف یا بدون مالیات", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => Month(x.Date), m => m, x => x.DocTitle, d => d, measures), false, "به تفکیک سند");
            }
            default: throw new DomainException("نوع گزارش فروش نامعتبر است.");
        }
    }

    // ---------------- داده ----------------

    /// <param name="Signed">مبلغ پس از تخفیف با علامت (برگشت منفی).</param>
    private sealed record Fact(bool IsReturn, long DocId, string DocTitle, DateOnly Date, int CustomerId, int ItemId, bool IsGoods,
        decimal Qty, decimal Gross, decimal Discount, decimal Net, decimal Vat, decimal? Cost)
    {
        public decimal Signed => IsReturn ? -Net : Net;
    }

    private static string Month(DateOnly d) => PersianDate.ToPersian(d)[..7];

    private async Task<List<Fact>> FactsAsync(SalesReportFilter f, CancellationToken ct)
    {
        DateOnly? from = PersianDate.TryParse(f.From, out var a) ? a : null;
        DateOnly? to = PersianDate.TryParse(f.To, out var b) ? b : null;

        var invoices = db.SalesInvoices.AsNoTracking();
        if (from is not null) invoices = invoices.Where(i => i.DocDate >= from);
        if (to is not null) invoices = invoices.Where(i => i.DocDate <= to);
        if (f.CustomerId is int c) invoices = invoices.Where(i => i.CustomerId == c);
        var sales = await invoices.SelectMany(i => i.Lines.Select(l => new
            {
                i.Id, i.DocNo, i.DocDate, i.CustomerId, l.ItemId, l.Quantity, l.UnitPrice, l.Discount, l.Amount, l.VatAmount, l.IssueLineId
            }))
            .Where(l => f.ItemId == null || l.ItemId == f.ItemId).ToListAsync(ct);

        var returns = db.SalesReturns.AsNoTracking();
        if (from is not null) returns = returns.Where(r => r.DocDate >= from);
        if (to is not null) returns = returns.Where(r => r.DocDate <= to);
        var rets = await (from r in returns
                          from l in r.Lines
                          join il in db.SalesInvoices.SelectMany(i => i.Lines.Select(x => new { x.Id, x.ItemId, i.CustomerId })) on l.InvoiceLineId equals il.Id
                          where (f.CustomerId == null || il.CustomerId == f.CustomerId) && (f.ItemId == null || il.ItemId == f.ItemId)
                          select new { r.Id, r.DocNo, r.DocDate, il.CustomerId, il.ItemId, l.Quantity, l.GrossAmount, l.Discount, l.Amount, l.VatAmount, l.ReceiptLineId })
            .ToListAsync(ct);

        var stockIds = sales.Select(s => s.IssueLineId).Concat(rets.Select(r => r.ReceiptLineId)).OfType<long>().Distinct().ToList();
        var costs = await db.StockLines.Where(s => stockIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.CostAmount, ct);
        var goods = await db.Items.Where(i => i.Kind == Erp.Inventory.Contracts.ItemKind.Goods).Select(i => i.Id).ToListAsync(ct);
        var goodsSet = goods.ToHashSet();

        return sales.Select(s => new Fact(false, s.Id, $"فاکتور فروش {s.DocNo:000000}", s.DocDate, s.CustomerId, s.ItemId, goodsSet.Contains(s.ItemId),
                    s.Quantity, s.Amount + s.Discount, s.Discount, s.Amount, s.VatAmount, s.IssueLineId is long i ? costs.GetValueOrDefault(i) : null))
            .Concat(rets.Select(r => new Fact(true, r.Id, $"برگشت از فروش {r.DocNo:000000}", r.DocDate, r.CustomerId, r.ItemId, goodsSet.Contains(r.ItemId),
                    r.Quantity, r.GrossAmount, r.Discount, r.Amount, r.VatAmount, r.ReceiptLineId is long i ? costs.GetValueOrDefault(i) : null)))
            .ToList();
    }

    private async Task<List<string>> SubtitleAsync(SalesReportFilter f, Func<int, string> item, CancellationToken ct)
    {
        var s = new List<string> { $"از {(string.IsNullOrWhiteSpace(f.From) ? "ابتدا" : f.From)} تا {(string.IsNullOrWhiteSpace(f.To) ? "امروز" : f.To)}" };
        if (f.CustomerId is int c) s.Add($"مشتری: {await db.Customers.Where(x => x.Id == c).Select(x => x.DisplayName).FirstOrDefaultAsync(ct)}");
        if (f.ItemId is int i) s.Add($"کالا: {item(i)}");
        return s;
    }
}
