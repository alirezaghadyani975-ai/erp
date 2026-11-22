using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Purchases.Application;

/// <summary>فیلتر گزارش‌های خرید: بازه تاریخ (شمسی)، و در صورت نیاز یک تأمین‌کننده یا یک کالا.</summary>
public sealed record PurchaseReportFilter(string? From, string? To, int? SupplierId = null, int? ItemId = null);

/// <summary>گزارش‌های خرید با سطح دوم. «خالص» = مبلغ پس از تخفیف منهای برگشت از خرید (برگشت به تاریخ خودش).</summary>
public class PurchaseReportService(IPurchasesDbContext db, IAccessControl access)
{
    public static readonly (string Key, string Title, string Help)[] Types =
    [
        ("items", "خرید کالاها", "هر کالا: مقدار و مبلغ خرید، تخفیف، برگشت و خرید خالص؛ سطح دوم به تفکیک تأمین‌کننده."),
        ("suppliers", "خرید از تأمین‌کنندگان", "هر تأمین‌کننده: تعداد فاکتور، خرید، برگشت و خالص؛ سطح دوم به تفکیک کالا."),
        ("monthly", "خرید ماهانه", "هر ماه: خرید، تخفیف، برگشت، خالص و ارزش افزوده؛ سطح دوم به تفکیک روز."),
        ("prices", "فی خرید کالاها", "کمترین، میانگین موزون، بیشترین و آخرین فی خرید (پس از تخفیف) هر کالا؛ سطح دوم به تفکیک تأمین‌کننده."),
        ("vat", "ارزش افزوده خرید", "ارزش افزوده فاکتورهای خرید و برگشت‌ها به تفکیک ماه؛ سطح دوم هر سند."),
    ];

    public async Task<ReportTable> RunAsync(string type, PurchaseReportFilter f, CancellationToken ct)
    {
        access.Demand(PurchasePermissions.ReportView);
        var facts = await FactsAsync(f, ct);
        var items = await db.Items.ToDictionaryAsync(i => i.Id, i => (Text: i.Code + " " + i.Title, Unit: i.UnitTitle), ct);
        var partyIds = facts.Select(x => x.SupplierId).Distinct().ToList();
        var parties = await db.Parties.Where(c => partyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        string Item(int id) => items.TryGetValue(id, out var i) ? i.Text : "?";
        string Supplier(int id) => parties.GetValueOrDefault(id, "?");
        var subtitle = new List<string> { $"از {(string.IsNullOrWhiteSpace(f.From) ? "ابتدا" : f.From)} تا {(string.IsNullOrWhiteSpace(f.To) ? "امروز" : f.To)}" };
        if (f.SupplierId is int s) subtitle.Add($"تأمین‌کننده: {await db.Parties.Where(x => x.Id == s).Select(x => x.DisplayName).FirstOrDefaultAsync(ct)}");
        if (f.ItemId is int it) subtitle.Add($"کالا: {Item(it)}");

        List<Measure<Fact>> Amounts() =>
        [
            new("gross", r => r.Where(x => !x.IsReturn).Sum(x => x.Net + x.Discount)),
            new("discount", r => r.Where(x => !x.IsReturn).Sum(x => x.Discount)),
            new("purchases", r => r.Where(x => !x.IsReturn).Sum(x => x.Net)),
            new("returns", r => r.Where(x => x.IsReturn).Sum(x => x.Net)),
            new("net", r => r.Sum(x => x.Signed)),
        ];
        List<ReportColumn> AmountColumns() =>
        [
            new("gross", "ناخالص", ReportColumnKind.Amount), new("discount", "تخفیف", ReportColumnKind.Amount),
            new("purchases", "خرید پس از تخفیف", ReportColumnKind.Amount), new("returns", "برگشت", ReportColumnKind.Amount),
            new("net", "خرید خالص", ReportColumnKind.Amount),
        ];
        Measure<Fact> Vat() => new("vat", r => r.Sum(x => x.IsReturn ? -x.Vat : x.Vat));
        Measure<Fact> Invoices() => new("invoices", r => (decimal)r.Where(x => !x.IsReturn).Select(x => x.DocId).Distinct().Count());

        switch (type)
        {
            case "items":
                return new ReportTable("خرید کالاها", subtitle,
                    [new("title", "کالا / تأمین‌کننده"), new("unit", "واحد"), new("qty", "مقدار خرید", ReportColumnKind.Number, Sum: false),
                     new("retQty", "مقدار برگشت", ReportColumnKind.Number, Sum: false), .. AmountColumns()],
                    ReportBuilder.TwoLevel(facts, x => x.ItemId, Item, x => x.SupplierId, Supplier,
                    [
                        new("unit", r => items.TryGetValue(r[0].ItemId, out var i) ? i.Unit : ""),
                        new("qty", r => r.Where(x => !x.IsReturn).Sum(x => x.Qty)),
                        new("retQty", r => r.Where(x => x.IsReturn).Sum(x => x.Qty)),
                        .. Amounts()
                    ]), true, "به تفکیک تأمین‌کننده");
            case "suppliers":
                return new ReportTable("خرید از تأمین‌کنندگان", subtitle,
                    [new("title", "تأمین‌کننده / کالا"), new("invoices", "تعداد فاکتور", ReportColumnKind.Number, Sum: false), .. AmountColumns(),
                     new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => x.SupplierId, Supplier, x => x.ItemId, Item, [Invoices(), .. Amounts(), Vat()]), true, "به تفکیک کالا");
            case "monthly":
                return new ReportTable("خرید ماهانه", subtitle,
                    [new("title", "ماه / روز"), new("invoices", "تعداد فاکتور", ReportColumnKind.Number), .. AmountColumns(),
                     new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => Month(x.Date), m => m, x => PersianDate.ToPersian(x.Date), d => d, [Invoices(), .. Amounts(), Vat()]),
                    true, "به تفکیک روز");
            case "prices":
            {
                static decimal? Price(IReadOnlyList<Fact> r, Func<IEnumerable<decimal>, decimal> agg)
                {
                    var buys = r.Where(x => !x.IsReturn && x.Qty != 0).Select(x => x.Net / x.Qty).ToList();
                    return buys.Count == 0 ? null : Math.Round(agg(buys), 0);
                }
                return new ReportTable("فی خرید کالاها", [.. subtitle, "فی = مبلغ پس از تخفیف ÷ مقدار، بدون ارزش افزوده؛ برگشت‌ها در فی اثری ندارند."],
                    [new("title", "کالا / تأمین‌کننده"), new("unit", "واحد"), new("qty", "مقدار خرید", ReportColumnKind.Number, Sum: false),
                     new("min", "کمترین فی", ReportColumnKind.Amount, Sum: false), new("avg", "میانگین موزون", ReportColumnKind.Amount, Sum: false),
                     new("max", "بیشترین فی", ReportColumnKind.Amount, Sum: false), new("last", "آخرین فی", ReportColumnKind.Amount, Sum: false),
                     new("lastDate", "تاریخ آخرین خرید", ReportColumnKind.Code)],
                    ReportBuilder.TwoLevel(facts.Where(x => !x.IsReturn), x => x.ItemId, Item, x => x.SupplierId, Supplier,
                    [
                        new("unit", r => items.TryGetValue(r[0].ItemId, out var i) ? i.Unit : ""),
                        new("qty", r => r.Sum(x => x.Qty)),
                        new("min", r => Price(r, p => p.Min())),
                        new("avg", r => r.Sum(x => x.Qty) == 0 ? null : Math.Round(r.Sum(x => x.Net) / r.Sum(x => x.Qty), 0)),
                        new("max", r => Price(r, p => p.Max())),
                        new("last", r => r.OrderBy(x => x.Date).ThenBy(x => x.DocId).Last() is { Qty: not 0 } l ? Math.Round(l.Net / l.Qty, 0) : null),
                        new("lastDate", r => PersianDate.ToPersian(r.Max(x => x.Date))),
                    ]), true, "به تفکیک تأمین‌کننده");
            }
            case "vat":
                return new ReportTable("ارزش افزوده خرید", subtitle,
                    [new("title", "ماه / سند"), new("base", "مبلغ مشمول (خالص)", ReportColumnKind.Amount),
                     new("buyVat", "ارزش افزوده خرید", ReportColumnKind.Amount), new("returnVat", "ارزش افزوده برگشت", ReportColumnKind.Amount),
                     new("vat", "ارزش افزوده خالص", ReportColumnKind.Amount), new("exempt", "خرید معاف یا بدون مالیات", ReportColumnKind.Amount)],
                    ReportBuilder.TwoLevel(facts, x => Month(x.Date), m => m, x => x.DocTitle, d => d,
                    [
                        new("base", r => r.Where(x => x.Vat != 0).Sum(x => x.Signed)),
                        new("buyVat", r => r.Where(x => !x.IsReturn).Sum(x => x.Vat)),
                        new("returnVat", r => r.Where(x => x.IsReturn).Sum(x => x.Vat)),
                        Vat(),
                        new("exempt", r => r.Where(x => x.Vat == 0).Sum(x => x.Signed)),
                    ]), false, "به تفکیک سند");
            default: throw new DomainException("نوع گزارش خرید نامعتبر است.");
        }
    }

    private sealed record Fact(bool IsReturn, long DocId, string DocTitle, DateOnly Date, int SupplierId, int ItemId,
        decimal Qty, decimal Discount, decimal Net, decimal Vat)
    {
        public decimal Signed => IsReturn ? -Net : Net;
    }

    private static string Month(DateOnly d) => PersianDate.ToPersian(d)[..7];

    private async Task<List<Fact>> FactsAsync(PurchaseReportFilter f, CancellationToken ct)
    {
        DateOnly? from = PersianDate.TryParse(f.From, out var a) ? a : null;
        DateOnly? to = PersianDate.TryParse(f.To, out var b) ? b : null;

        var invoices = db.PurchaseInvoices.AsNoTracking();
        if (from is not null) invoices = invoices.Where(i => i.DocDate >= from);
        if (to is not null) invoices = invoices.Where(i => i.DocDate <= to);
        if (f.SupplierId is int s) invoices = invoices.Where(i => i.SupplierId == s);
        var buys = await invoices.SelectMany(i => i.Lines.Select(l => new { i.Id, i.DocNo, i.DocDate, i.SupplierId, l.ItemId, l.Quantity, l.Discount, l.Amount, l.VatAmount }))
            .Where(l => f.ItemId == null || l.ItemId == f.ItemId).ToListAsync(ct);

        var returns = db.PurchaseReturns.AsNoTracking();
        if (from is not null) returns = returns.Where(r => r.DocDate >= from);
        if (to is not null) returns = returns.Where(r => r.DocDate <= to);
        var rets = await (from r in returns
                          from l in r.Lines
                          join il in db.PurchaseInvoices.SelectMany(i => i.Lines.Select(x => new { x.Id, x.ItemId, i.SupplierId })) on l.InvoiceLineId equals il.Id
                          where (f.SupplierId == null || il.SupplierId == f.SupplierId) && (f.ItemId == null || il.ItemId == f.ItemId)
                          select new { r.Id, r.DocNo, r.DocDate, il.SupplierId, il.ItemId, l.Quantity, l.Amount, l.VatAmount })
            .ToListAsync(ct);

        return buys.Select(x => new Fact(false, x.Id, $"فاکتور خرید {x.DocNo:000000}", x.DocDate, x.SupplierId, x.ItemId, x.Quantity, x.Discount, x.Amount, x.VatAmount))
            .Concat(rets.Select(x => new Fact(true, x.Id, $"برگشت از خرید {x.DocNo:000000}", x.DocDate, x.SupplierId, x.ItemId, x.Quantity, 0, x.Amount, x.VatAmount)))
            .ToList();
    }
}
