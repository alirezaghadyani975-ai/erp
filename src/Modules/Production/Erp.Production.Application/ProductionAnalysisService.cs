using Erp.Production.Domain;
using Erp.SharedKernel.Calendar;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Production.Application;

/// <summary>فیلتر گزارش‌های تولید: بازه تاریخ (شمسی) و در صورت نیاز یک محصول.</summary>
public sealed record ProductionAnalysisFilter(string? From, string? To, int? ProductId = null);

/// <summary>
/// گزارش‌های تحلیلی تولید از گزارش‌های تولید ثبت‌شده (مقداری؛ بها در مرحله ۲). هر گزارش یک سطح دوم (تفکیک) دارد.
/// انحراف = واقعی − استاندارد (مثبت = مصرف یا زمان بیشتر از استاندارد)؛ درصد انحراف نسبت به استاندارد.
/// </summary>
public class ProductionAnalysisService(IProductionDbContext db, IAccessControl access)
{
    public static readonly (string Key, string Title, string Help)[] Types =
    [
        ("daily", "تولید روزانه", "هر روز: تعداد گزارش و ساعت کار؛ سطح دوم مقدار تولید هر محصول در آن روز."),
        ("products", "تولید تجمیعی محصولات", "هر محصول: مقدار کل، تعداد گزارش و زمان واقعی؛ سطح دوم تک‌تک گزارش‌های تولید."),
        ("consumption", "مصرف مواد", "هر ماده: مقدار مصرف واقعی کل؛ سطح دوم مصرف آن در هر محصول و سهم هر محصول."),
        ("material-variance", "انحراف مصرف مواد", "استاندارد فرمول در برابر مصرف واقعی هر ماده؛ سطح دوم به تفکیک محصول."),
        ("time-variance", "انحراف زمان", "زمان استاندارد در برابر واقعی هر مرکز تولید (نفر و ماشین، ساعت)؛ سطح دوم به تفکیک محصول."),
        ("scrap", "ضایعات", "ضایعات استاندارد و واقعی هر کالای ضایعات و نسبت آن؛ سطح دوم به تفکیک محصول."),
        ("operators", "کارکرد اپراتورها", "ساعت کار هر اپراتور؛ سطح دوم هر گزارش تولید با محصول و مرکز."),
        ("centers", "کارکرد مراکز تولید", "ساعت نفر و ماشین هر مرکز و ساعت اپراتورهای ثبت‌شده؛ مبنای جذب سربار. سطح دوم به تفکیک محصول."),
        ("efficiency", "عملکرد محصولات", "راندمان زمان (استاندارد ÷ واقعی) و ردیف‌های مصرف بیش از استاندارد هر محصول؛ سطح دوم هر گزارش."),
        ("substitutes", "مصرف جایگزین‌ها", "مواد جایگزینی که به‌جای ماده اصلی فرمول مصرف شده؛ سطح دوم به تفکیک محصول."),
        ("off-bom", "مواد خارج از فرمول", "موادی که در فرمول نبوده و در گزارش تولید اضافه شده؛ سطح دوم هر گزارش."),
    ];

    public async Task<ReportTable> RunAsync(string type, ProductionAnalysisFilter f, CancellationToken ct)
    {
        access.Demand(ProductionPermissions.AnalysisView);
        var data = await LoadAsync(f, ct);
        return type switch
        {
            "daily" => Daily(data),
            "products" => Products(data),
            "consumption" => Consumption(data),
            "material-variance" => MaterialVariance(data),
            "time-variance" => TimeVariance(data),
            "scrap" => Scrap(data),
            "operators" => Operators(data),
            "centers" => Centers(data),
            "efficiency" => Efficiency(data),
            "substitutes" => Substitutes(data),
            "off-bom" => OffBom(data),
            _ => throw new DomainException("نوع گزارش تولید نامعتبر است.")
        };
    }

    // ---------------- داده ----------------

    private sealed record Rep(long Id, int DocNo, DateOnly Date, int ProductId, decimal Quantity)
    {
        public string Label => $"{PersianDate.ToPersian(Date)} — گزارش {DocNo:000000}";
    }

    private sealed record Item(string Code, string Title, string Unit)
    {
        public string Text => $"{Code} {Title}";
    }

    private sealed class Data
    {
        public required List<string> Subtitle { get; init; }
        public required Dictionary<long, Rep> Reports { get; init; }
        public required List<ProductionMaterial> Materials { get; init; }
        public required List<ProductionScrap> Scraps { get; init; }
        public required List<ProductionOperation> Operations { get; init; }
        public required List<ProductionOperator> Operators { get; init; }
        public required Dictionary<int, Item> Items { get; init; }
        public required Dictionary<int, string> Centers { get; init; }
        public required Dictionary<int, string> Employees { get; init; }
        public required Dictionary<int, int> BomMainItem { get; init; }

        public Item ItemOf(int id) => Items.GetValueOrDefault(id) ?? new Item("?", "?", "");
        public Rep Of(long reportId) => Reports[reportId];
        public string Center(int? id) => id is int c ? Centers.GetValueOrDefault(c, "?") : "";
    }

    private async Task<Data> LoadAsync(ProductionAnalysisFilter f, CancellationToken ct)
    {
        var query = db.Reports.AsNoTracking();
        var subtitle = new List<string>();
        if (PersianDate.TryParse(f.From, out var from)) query = query.Where(r => r.ReportDate >= from);
        if (PersianDate.TryParse(f.To, out var to)) query = query.Where(r => r.ReportDate <= to);
        subtitle.Add($"از {(f.From is { Length: > 0 } ? f.From : "ابتدا")} تا {(f.To is { Length: > 0 } ? f.To : "امروز")}");
        if (f.ProductId is int p) query = query.Where(r => r.ProductId == p);

        var reports = await query.Select(r => new Rep(r.Id, r.DocNo, r.ReportDate, r.ProductId, r.Quantity)).ToDictionaryAsync(r => r.Id, ct);
        var materials = await query.SelectMany(r => r.Materials).ToListAsync(ct);
        var scraps = await query.SelectMany(r => r.Scraps).ToListAsync(ct);
        var operations = await query.SelectMany(r => r.Operations).ToListAsync(ct);
        var operators = await query.SelectMany(r => r.Operators).ToListAsync(ct);

        var itemIds = reports.Values.Select(r => r.ProductId).Concat(materials.Select(m => m.ItemId)).Concat(scraps.Select(s => s.ItemId)).Distinct().ToList();
        var bomMaterialIds = materials.Where(m => m.OptionNo is not null).Select(m => m.BomMaterialId!.Value).Distinct().ToList();
        var bomMain = await db.Boms.SelectMany(b => b.Materials).Where(m => bomMaterialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ItemId, ct);
        itemIds.AddRange(bomMain.Values);
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => new Item(i.Code, i.Title, i.Unit), ct);
        if (f.ProductId is int pid && items.TryGetValue(pid, out var prod)) subtitle.Add($"محصول: {prod.Text}");
        else if (f.ProductId is int pid2)
        {
            var one = await db.Items.Where(i => i.Id == pid2).Select(i => i.Code + " " + i.Title).FirstOrDefaultAsync(ct);
            subtitle.Add($"محصول: {one}");
        }

        var centerIds = operations.Select(o => o.CostCenterId).Concat(operators.Select(o => o.CostCenterId).OfType<int>()).Distinct().ToList();
        var centers = await db.Centers.Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var employeeIds = operators.Select(o => o.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.PersonnelNo + " — " + e.DisplayName, ct);

        return new Data
        {
            Subtitle = subtitle, Reports = reports, Materials = materials, Scraps = scraps, Operations = operations, Operators = operators,
            Items = items, Centers = centers, Employees = employees, BomMainItem = bomMain
        };
    }

    // ---------------- ابزار ----------------

    private static ReportRow Row(List<ReportRow>? children, params (string Key, object? Value)[] cells) =>
        new(cells.ToDictionary(c => c.Key, c => c.Value), children);

    private static decimal H(decimal seconds) => Math.Round(seconds / 3600m, 2);

    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1);

    private static ReportTable Table(string title, Data d, List<ReportColumn> columns, IEnumerable<ReportRow> rows, string? childTitle, bool landscape = false) =>
        new(title, d.Subtitle, columns, rows.ToList(), landscape, childTitle);

    private static ReportColumn Text(string key, string title) => new(key, title);
    private static ReportColumn Num(string key, string title, string? group = null, bool sum = true) => new(key, title, ReportColumnKind.Number, group, sum);
    private static ReportColumn Pc(string key, string title, string? group = null) => new(key, title, ReportColumnKind.Percent, group, false);

    // ---------------- گزارش‌ها ----------------

    private static ReportTable Daily(Data d)
    {
        var manByReport = d.Operations.GroupBy(o => o.ReportId).ToDictionary(g => g.Key, g => g.Sum(o => o.ActualManSeconds));
        var machByReport = d.Operations.GroupBy(o => o.ReportId).ToDictionary(g => g.Key, g => g.Sum(o => o.ActualMachineSeconds));
        var opByReport = d.Operators.GroupBy(o => o.ReportId).ToDictionary(g => g.Key, g => g.Sum(o => o.Hours));
        var rows = d.Reports.Values.GroupBy(r => r.Date).OrderBy(g => g.Key).Select(day => Row(
            day.GroupBy(r => r.ProductId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g => Row(null,
                ("title", d.ItemOf(g.Key).Text), ("unit", d.ItemOf(g.Key).Unit), ("qty", g.Sum(r => r.Quantity)), ("reports", (decimal)g.Count()),
                ("man", H(g.Sum(r => manByReport.GetValueOrDefault(r.Id)))), ("machine", H(g.Sum(r => machByReport.GetValueOrDefault(r.Id)))),
                ("operators", g.Sum(r => opByReport.GetValueOrDefault(r.Id))))).ToList(),
            ("title", PersianDate.ToPersian(day.Key)), ("unit", $"{day.Select(r => r.ProductId).Distinct().Count()} محصول"), ("qty", null),
            ("reports", (decimal)day.Count()), ("man", H(day.Sum(r => manByReport.GetValueOrDefault(r.Id)))),
            ("machine", H(day.Sum(r => machByReport.GetValueOrDefault(r.Id)))), ("operators", day.Sum(r => opByReport.GetValueOrDefault(r.Id)))));
        return Table("تولید روزانه", d,
            [Text("title", "تاریخ / محصول"), Text("unit", "واحد"), Num("qty", "مقدار تولید", sum: false), Num("reports", "تعداد گزارش"),
             Num("man", "نفر-ساعت واقعی"), Num("machine", "ماشین-ساعت واقعی"), Num("operators", "ساعت اپراتورها")],
            rows, "به تفکیک محصول");
    }

    private static ReportTable Products(Data d)
    {
        var ops = d.Operations.GroupBy(o => o.ReportId).ToDictionary(g => g.Key, g => (Man: g.Sum(o => o.ActualManSeconds), Mach: g.Sum(o => o.ActualMachineSeconds)));
        var rows = d.Reports.Values.GroupBy(r => r.ProductId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            var qty = g.Sum(r => r.Quantity);
            var man = H(g.Sum(r => ops.GetValueOrDefault(r.Id).Man));
            return Row(g.OrderBy(r => r.Date).ThenBy(r => r.DocNo).Select(r => Row(null,
                    ("title", r.Label), ("unit", item.Unit), ("qty", r.Quantity), ("reports", 1m),
                    ("man", H(ops.GetValueOrDefault(r.Id).Man)), ("machine", H(ops.GetValueOrDefault(r.Id).Mach)),
                    ("manPerUnit", r.Quantity == 0 ? null : Math.Round(H(ops.GetValueOrDefault(r.Id).Man) / r.Quantity, 4)))).ToList(),
                ("title", item.Text), ("unit", item.Unit), ("qty", qty), ("reports", (decimal)g.Count()), ("man", man),
                ("machine", H(g.Sum(r => ops.GetValueOrDefault(r.Id).Mach))), ("manPerUnit", qty == 0 ? null : Math.Round(man / qty, 4)));
        });
        return Table("تولید تجمیعی محصولات", d,
            [Text("title", "محصول / گزارش تولید"), Text("unit", "واحد"), Num("qty", "مقدار تولید", sum: false), Num("reports", "تعداد گزارش"),
             Num("man", "نفر-ساعت واقعی"), Num("machine", "ماشین-ساعت واقعی"), Num("manPerUnit", "نفر-ساعت هر واحد", sum: false)],
            rows, "گزارش‌های تولید");
    }

    private static ReportTable Consumption(Data d)
    {
        var rows = d.Materials.Where(m => m.ActualQty != 0).GroupBy(m => m.ItemId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            var total = g.Sum(m => m.ActualQty);
            var byProduct = g.GroupBy(m => d.Of(m.ReportId).ProductId).OrderByDescending(p => p.Sum(m => m.ActualQty)).ToList();
            return Row(byProduct.Select(p => Row(null,
                    ("title", d.ItemOf(p.Key).Text), ("unit", item.Unit), ("qty", p.Sum(m => m.ActualQty)),
                    ("produced", p.Select(m => m.ReportId).Distinct().Sum(id => d.Of(id).Quantity)), ("productUnit", d.ItemOf(p.Key).Unit),
                    ("reports", (decimal)p.Select(m => m.ReportId).Distinct().Count()), ("share", Pct(p.Sum(m => m.ActualQty), total)))).ToList(),
                ("title", item.Text), ("unit", item.Unit), ("qty", total), ("produced", null), ("productUnit", $"{byProduct.Count} محصول"),
                ("reports", (decimal)g.Select(m => m.ReportId).Distinct().Count()), ("share", 100m));
        });
        return Table("مصرف مواد", d,
            [Text("title", "ماده / محصول"), Text("unit", "واحد"), Num("qty", "مقدار مصرف", sum: false), Num("produced", "مقدار تولید محصول", sum: false),
             Text("productUnit", "واحد محصول"), Num("reports", "تعداد گزارش"), Pc("share", "سهم از مصرف ماده")],
            rows, "به تفکیک محصول");
    }

    private static ReportTable MaterialVariance(Data d)
    {
        (string, object?)[] Cells(string title, string unit, IEnumerable<ProductionMaterial> ms)
        {
            var std = ms.Sum(m => m.StandardQty);
            var act = ms.Sum(m => m.ActualQty);
            return [("title", title), ("unit", unit), ("std", std), ("actual", act), ("variance", act - std), ("pct", Pct(act - std, std))];
        }
        var rows = d.Materials.GroupBy(m => m.ItemId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            return Row(g.GroupBy(m => d.Of(m.ReportId).ProductId).OrderBy(p => d.ItemOf(p.Key).Code)
                    .Select(p => Row(null, Cells(d.ItemOf(p.Key).Text, item.Unit, p))).ToList(),
                Cells(item.Text, item.Unit, g));
        });
        return Table("انحراف مصرف مواد", d,
            [Text("title", "ماده / محصول"), Text("unit", "واحد"), Num("std", "استاندارد", sum: false), Num("actual", "واقعی", sum: false),
             Num("variance", "انحراف", sum: false), Pc("pct", "درصد انحراف")],
            rows, "به تفکیک محصول");
    }

    private static ReportTable TimeVariance(Data d)
    {
        (string, object?)[] Cells(string title, IEnumerable<ProductionOperation> os)
        {
            var (sm, am) = (H(os.Sum(o => o.StdManSeconds)), H(os.Sum(o => o.ActualManSeconds)));
            var (sc, ac) = (H(os.Sum(o => o.StdMachineSeconds)), H(os.Sum(o => o.ActualMachineSeconds)));
            return [("title", title), ("stdMan", sm), ("actMan", am), ("varMan", am - sm), ("pctMan", Pct(am - sm, sm)),
                    ("stdMach", sc), ("actMach", ac), ("varMach", ac - sc), ("pctMach", Pct(ac - sc, sc))];
        }
        var rows = d.Operations.GroupBy(o => o.CostCenterId).OrderBy(g => d.Center(g.Key)).Select(g => Row(
            g.GroupBy(o => d.Of(o.ReportId).ProductId).OrderBy(p => d.ItemOf(p.Key).Code).Select(p => Row(null, Cells(d.ItemOf(p.Key).Text, p))).ToList(),
            Cells(d.Center(g.Key), g)));
        return Table("انحراف زمان تولید (ساعت)", d,
            [Text("title", "مرکز / محصول"),
             Num("stdMan", "استاندارد", "نفر-ساعت"), Num("actMan", "واقعی", "نفر-ساعت"), Num("varMan", "انحراف", "نفر-ساعت"), Pc("pctMan", "درصد", "نفر-ساعت"),
             Num("stdMach", "استاندارد", "ماشین-ساعت"), Num("actMach", "واقعی", "ماشین-ساعت"), Num("varMach", "انحراف", "ماشین-ساعت"), Pc("pctMach", "درصد", "ماشین-ساعت")],
            rows, "به تفکیک محصول", landscape: true);
    }

    private static ReportTable Scrap(Data d)
    {
        (string, object?)[] Cells(string title, string unit, IEnumerable<ProductionScrap> ss, decimal? produced, string? productUnit)
        {
            var std = ss.Sum(s => s.StandardQty);
            var act = ss.Sum(s => s.ActualQty);
            var types = ss.Select(s => s.ScrapType).Distinct().ToList();
            return [("title", title), ("unit", unit), ("type", types.Count > 1 ? "هر دو" : types[0] == ScrapType.Full ? "مواد + دستمزد و سربار" : "فقط مواد"),
                    ("std", std), ("actual", act), ("variance", act - std), ("pct", Pct(act - std, std)), ("produced", produced), ("productUnit", productUnit),
                    ("perUnit", produced is > 0 ? Math.Round(act / produced.Value, 4) : null)];
        }
        var rows = d.Scraps.GroupBy(s => s.ItemId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            return Row(g.GroupBy(s => d.Of(s.ReportId).ProductId).OrderBy(p => d.ItemOf(p.Key).Code).Select(p => Row(null,
                    Cells(d.ItemOf(p.Key).Text, item.Unit, p, p.Select(s => s.ReportId).Distinct().Sum(id => d.Of(id).Quantity), d.ItemOf(p.Key).Unit))).ToList(),
                Cells(item.Text, item.Unit, g, null, null));
        });
        return Table("گزارش ضایعات", d,
            [Text("title", "ضایعات / محصول"), Text("unit", "واحد"), Text("type", "نوع"), Num("std", "استاندارد", sum: false), Num("actual", "واقعی", sum: false),
             Num("variance", "انحراف", sum: false), Pc("pct", "درصد انحراف"), Num("produced", "تولید محصول", sum: false), Text("productUnit", "واحد محصول"),
             Num("perUnit", "ضایعات هر واحد محصول", sum: false)],
            rows, "به تفکیک محصول", landscape: true);
    }

    private static ReportTable Operators(Data d)
    {
        var rows = d.Operators.GroupBy(o => o.EmployeeId).OrderBy(g => d.Employees.GetValueOrDefault(g.Key, "?")).Select(g => Row(
            g.OrderBy(o => d.Of(o.ReportId).Date).Select(o =>
            {
                var r = d.Of(o.ReportId);
                return Row(null, ("title", r.Label), ("product", d.ItemOf(r.ProductId).Text), ("center", d.Center(o.CostCenterId)),
                    ("qty", r.Quantity), ("unit", d.ItemOf(r.ProductId).Unit), ("reports", 1m), ("hours", o.Hours));
            }).ToList(),
            ("title", d.Employees.GetValueOrDefault(g.Key, "?")), ("product", $"{g.Select(o => d.Of(o.ReportId).ProductId).Distinct().Count()} محصول"),
            ("center", string.Join("، ", g.Select(o => d.Center(o.CostCenterId)).Where(c => c != "").Distinct())), ("qty", null), ("unit", null),
            ("reports", (decimal)g.Select(o => o.ReportId).Distinct().Count()), ("hours", g.Sum(o => o.Hours))));
        return Table("کارکرد اپراتورها", d,
            [Text("title", "اپراتور / گزارش تولید"), Text("product", "محصول"), Text("center", "مرکز"), Num("qty", "مقدار تولید", sum: false),
             Text("unit", "واحد"), Num("reports", "تعداد گزارش"), Num("hours", "ساعت کار")],
            rows, "گزارش‌های تولید", landscape: true);
    }

    private static ReportTable Centers(Data d)
    {
        var centerIds = d.Operations.Select(o => o.CostCenterId).Concat(d.Operators.Select(o => o.CostCenterId).OfType<int>()).Distinct();
        (string, object?)[] Cells(string title, IEnumerable<ProductionOperation> os, IEnumerable<ProductionOperator> ps)
        {
            var man = H(os.Sum(o => o.ActualManSeconds));
            var hours = ps.Sum(p => p.Hours);
            return [("title", title), ("reports", (decimal)os.Select(o => o.ReportId).Concat(ps.Select(p => p.ReportId)).Distinct().Count()),
                    ("man", man), ("machine", H(os.Sum(o => o.ActualMachineSeconds))), ("operators", hours), ("diff", hours - man)];
        }
        var rows = centerIds.OrderBy(c => d.Center(c)).Select(c =>
        {
            var os = d.Operations.Where(o => o.CostCenterId == c).ToList();
            var ps = d.Operators.Where(o => o.CostCenterId == c).ToList();
            var products = os.Select(o => d.Of(o.ReportId).ProductId).Concat(ps.Select(p => d.Of(p.ReportId).ProductId)).Distinct()
                .OrderBy(p => d.ItemOf(p).Code);
            return Row(products.Select(p => Row(null, Cells(d.ItemOf(p).Text, os.Where(o => d.Of(o.ReportId).ProductId == p),
                    ps.Where(x => d.Of(x.ReportId).ProductId == p)))).ToList(),
                Cells(d.Center(c), os, ps));
        });
        return Table("کارکرد مراکز تولید", d,
            [Text("title", "مرکز / محصول"), Num("reports", "تعداد گزارش"), Num("man", "نفر-ساعت واقعی"), Num("machine", "ماشین-ساعت واقعی"),
             Num("operators", "ساعت اپراتورها"), Num("diff", "اختلاف ساعت اپراتور با نفر-ساعت")],
            rows, "به تفکیک محصول");
    }

    private static ReportTable Efficiency(Data d)
    {
        (string, object?)[] Cells(string title, string unit, IEnumerable<Rep> reps)
        {
            var ids = reps.Select(r => r.Id).ToHashSet();
            var os = d.Operations.Where(o => ids.Contains(o.ReportId)).ToList();
            var ms = d.Materials.Where(m => ids.Contains(m.ReportId)).ToList();
            var (sm, am) = (H(os.Sum(o => o.StdManSeconds)), H(os.Sum(o => o.ActualManSeconds)));
            var (sc, ac) = (H(os.Sum(o => o.StdMachineSeconds)), H(os.Sum(o => o.ActualMachineSeconds)));
            return [("title", title), ("unit", unit), ("qty", reps.Sum(r => r.Quantity)), ("reports", (decimal)ids.Count),
                    ("effMan", Pct(sm, am)), ("effMach", Pct(sc, ac)),
                    ("over", (decimal)ms.Count(m => m.ActualQty > m.StandardQty)), ("under", (decimal)ms.Count(m => m.ActualQty < m.StandardQty)),
                    ("subs", (decimal)ms.Count(m => m.OptionNo is not null)), ("offBom", (decimal)ms.Count(m => m.BomMaterialId is null))];
        }
        var rows = d.Reports.Values.GroupBy(r => r.ProductId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g => Row(
            g.OrderBy(r => r.Date).ThenBy(r => r.DocNo).Select(r => Row(null, Cells(r.Label, d.ItemOf(g.Key).Unit, [r]))).ToList(),
            Cells(d.ItemOf(g.Key).Text, d.ItemOf(g.Key).Unit, g)));
        return Table("عملکرد محصولات", d,
            [Text("title", "محصول / گزارش تولید"), Text("unit", "واحد"), Num("qty", "مقدار تولید", sum: false), Num("reports", "تعداد گزارش"),
             Pc("effMan", "راندمان نفر", "راندمان زمان"), Pc("effMach", "راندمان ماشین", "راندمان زمان"),
             Num("over", "بیش از استاندارد", "ردیف‌های مواد"), Num("under", "کمتر از استاندارد", "ردیف‌های مواد"),
             Num("subs", "جایگزین", "ردیف‌های مواد"), Num("offBom", "خارج از فرمول", "ردیف‌های مواد")],
            rows, "گزارش‌های تولید", landscape: true);
    }

    private static ReportTable Substitutes(Data d)
    {
        var used = d.Materials.Where(m => m.OptionNo is not null && m.BomMaterialId is not null).ToList();
        var rows = used.GroupBy(m => m.ItemId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            string Main(IEnumerable<ProductionMaterial> ms) => string.Join("، ", ms.Select(m => d.BomMainItem.TryGetValue(m.BomMaterialId!.Value, out var i)
                ? d.ItemOf(i).Text : "?").Distinct());
            return Row(g.GroupBy(m => d.Of(m.ReportId).ProductId).OrderBy(p => d.ItemOf(p.Key).Code).Select(p => Row(null,
                    ("title", d.ItemOf(p.Key).Text), ("unit", item.Unit), ("instead", Main(p)), ("reports", (decimal)p.Select(m => m.ReportId).Distinct().Count()),
                    ("std", p.Sum(m => m.StandardQty)), ("actual", p.Sum(m => m.ActualQty)))).ToList(),
                ("title", item.Text), ("unit", item.Unit), ("instead", Main(g)), ("reports", (decimal)g.Select(m => m.ReportId).Distinct().Count()),
                ("std", g.Sum(m => m.StandardQty)), ("actual", g.Sum(m => m.ActualQty)));
        });
        return Table("مصرف جایگزین‌ها", d,
            [Text("title", "ماده جایگزین / محصول"), Text("unit", "واحد"), Text("instead", "به‌جای ماده اصلی"), Num("reports", "تعداد گزارش"),
             Num("std", "استاندارد", sum: false), Num("actual", "واقعی", sum: false)],
            rows, "به تفکیک محصول", landscape: true);
    }

    private static ReportTable OffBom(Data d)
    {
        var rows = d.Materials.Where(m => m.BomMaterialId is null).GroupBy(m => m.ItemId).OrderBy(g => d.ItemOf(g.Key).Code).Select(g =>
        {
            var item = d.ItemOf(g.Key);
            return Row(g.OrderBy(m => d.Of(m.ReportId).Date).Select(m =>
                {
                    var r = d.Of(m.ReportId);
                    return Row(null, ("title", r.Label), ("product", d.ItemOf(r.ProductId).Text), ("unit", item.Unit), ("qty", m.ActualQty), ("reports", 1m));
                }).ToList(),
                ("title", item.Text), ("product", $"{g.Select(m => d.Of(m.ReportId).ProductId).Distinct().Count()} محصول"), ("unit", item.Unit),
                ("qty", g.Sum(m => m.ActualQty)), ("reports", (decimal)g.Select(m => m.ReportId).Distinct().Count()));
        });
        return Table("مواد خارج از فرمول", d,
            [Text("title", "ماده / گزارش تولید"), Text("product", "محصول"), Text("unit", "واحد"), Num("qty", "مقدار مصرف", sum: false), Num("reports", "تعداد گزارش")],
            rows, "گزارش‌های تولید");
    }
}
