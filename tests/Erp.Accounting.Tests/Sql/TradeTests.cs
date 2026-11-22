using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Purchases.Application;
using Erp.Purchases.Domain;
using Erp.Sales.Application;
using Erp.Sales.Domain;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>کالا، فاکتور خرید و فروش و برگشت‌ها با سند خودکار.</summary>
[Collection(SqlCollection.Name)]
public class TradeTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(int Supplier, int Customer, int Goods, int Service, int Exempt);

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;
            await db.Run<FiscalYearService, short>(s => s.CreateAsync(1417, 1, default));
            var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
            short Role(string code) => roles.Single(r => r.Code == code).Id;
            var supplier = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "تأمین", LastName = "کالا", RoleIds = [Role(RoleCodes.Supplier)] }, default));
            var customer = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "مشتری", LastName = "فروش", RoleIds = [Role(RoleCodes.Customer)] }, default));

            var groups = await db.Run<ItemGroupService, List<ItemGroupDto>>(s => s.GetAllAsync(false, default));
            var unit = (await db.Run<ItemService, List<ItemUnitDto>>(s => s.GetUnitsAsync(default)))[0].Id;
            async Task<int> Item(string title, string group, bool exempt = false) => await db.Run<ItemService, int>(s => s.CreateAsync(
                new SaveItemRequest(null, title, groups.Single(g => g.Title == group).Id, unit, VatExempt: exempt, CategoryId: group == "خدمات" ? null : (byte)5), default));
            return _seed = new Seed(supplier, customer, await Item("کالای تست تجاری", "کالای بازرگانی"),
                await Item("خدمت تست", "خدمات"), await Item("کالای معاف تست", "کالای بازرگانی", exempt: true));
        }
        finally
        {
            Lock.Release();
        }
    }

    private Task<VoucherDto> Voucher(long id) => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default));

    private async Task<int> WarehouseAsync() =>
        (await db.Run<WarehouseService, List<WarehouseDto>>(s => s.GetAllAsync(false, default))).First(w => w.Code == "01").Id;

    /// <summary>ثبت سند انبار و برگرداندن شناسه ردیف‌هایش به ترتیب.</summary>
    private async Task<List<long>> StockAsync(StockDocType type, string date, int? party, params StockLineRequest[] lines)
    {
        var warehouse = await WarehouseAsync();
        var id = await db.Run<StockDocService, long>(s => s.CreateAsync(
            new SaveStockDocRequest(type, date, warehouse, party, null, [.. lines]), default));
        return (await db.Run<StockDocService, StockDocDto>(s => s.GetAsync(id, default))).Lines.Select(l => l.Id).ToList();
    }

    private Task<long> StockDocIdAsync(long lineId) =>
        db.Run<StockDocService, long>(async s =>
        {
            foreach (var receipts in new[] { true, false })
                foreach (var d in await s.ListAsync(receipts, null, null, null, null, default))
                    if ((await s.GetAsync(d.Id, default)).Lines.Any(l => l.Id == lineId)) return d.Id;
            throw new InvalidOperationException();
        });

    [Fact]
    public async Task Item_category_is_required_and_controls_purchase_and_sale()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var groups = await db.Run<ItemGroupService, List<ItemGroupDto>>(x => x.GetAllAsync(false, default));
        var goodsGroup = groups.Single(g => g.Title == "کالای بازرگانی").Id;
        var unit = (await db.Run<ItemService, List<ItemUnitDto>>(x => x.GetUnitsAsync(default)))[0].Id;

        var missing = await Assert.ThrowsAsync<DomainException>(() => db.Run<ItemService, int>(x => x.CreateAsync(
            new SaveItemRequest(null, "کالای بی‌نوع", goodsGroup, unit), default)));
        Assert.Contains("نوع کالا", missing.Message);

        var product = await db.Run<ItemService, int>(x => x.CreateAsync(
            new SaveItemRequest(null, "محصول تست نوع", goodsGroup, unit, CategoryId: Erp.Inventory.Domain.ItemCategory.Product), default));
        var notBuyable = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.PurchaseReceipt, "1417/02/10", s.Supplier, new StockLineRequest(product, 1)));
        Assert.Contains("خریدنی نیست", notBuyable.Message);

        var consumable = await db.Run<ItemService, int>(x => x.CreateAsync(
            new SaveItemRequest(null, "اقلام مصرفی تست", goodsGroup, unit, CategoryId: Erp.Inventory.Domain.ItemCategory.Consumable), default));
        await StockAsync(StockDocType.PurchaseReceipt, "1417/02/10", s.Supplier, new StockLineRequest(consumable, 5));
        var notSellable = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.SalesIssue, "1417/02/11", s.Customer, new StockLineRequest(consumable, 1)));
        Assert.Contains("فروختنی نیست", notSellable.Message);

        var categories = await db.Run<ItemCategoryService, List<ItemCategoryDto>>(x => x.GetAllAsync(default));
        Assert.Equal(6, categories.Count);
        Assert.All(categories, c => Assert.NotNull(c.InventoryAccountId));
    }

    [Fact]
    public async Task Item_code_is_generated_and_kind_follows_group()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var goods = await db.Run<ItemService, ItemDto>(x => x.GetAsync(s.Goods, default));
        var service = await db.Run<ItemService, ItemDto>(x => x.GetAsync(s.Service, default));
        Assert.Matches("^[0-9]{6}$", goods.Code);
        Assert.Equal((ItemKind.Goods, ItemKind.Service), (goods.Kind, service.Kind));

        var bad = await Assert.ThrowsAsync<DomainException>(() => db.Run<ItemService>(x => x.UpdateAsync(s.Goods,
            new SaveItemRequest(goods.Code, goods.Title, goods.GroupId, goods.UnitId, TaxItemId: "12345", CategoryId: goods.CategoryId), default)));
        Assert.Contains("۱۳ رقم", bad.Message);
    }

    [Fact]
    public async Task Purchase_posts_inventory_vat_and_payable_and_return_reverses_share()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var receipt = await StockAsync(StockDocType.PurchaseReceipt, "1417/03/01", s.Supplier,
            new StockLineRequest(s.Goods, 3, EnteredPrice: 900), new StockLineRequest(s.Exempt, 2));

        // بدون رسید یا با مقدار دیگر پذیرفته نمی‌شود
        var noReceipt = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(
            new SavePurchaseInvoiceRequest("1417/03/01", s.Supplier, PayableKind.Trade, null, null, null, null,
                [new PurchaseLineRequest(s.Goods, 3, 1000)]), default)));
        Assert.Contains("رسید خرید", noReceipt.Message);
        var otherQty = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(
            new SavePurchaseInvoiceRequest("1417/03/01", s.Supplier, PayableKind.Trade, null, null, null, null,
                [new PurchaseLineRequest(s.Goods, 2, 1000, ReceiptLineId: receipt[0])]), default)));
        Assert.Contains("کالا و مقدار", otherQty.Message);

        Assert.Equal(2, (await db.Run<PurchaseInvoiceService, List<Erp.Inventory.Contracts.PendingStockLineDto>>(
            x => x.PendingReceiptsAsync(s.Supplier, null, default))).Count(p => receipt.Contains(p.LineId)));
        var id = await db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(new SavePurchaseInvoiceRequest("1417/03/01", s.Supplier,
            PayableKind.Trade, "P-1", "1417/03/01", null, "خرید تست",
            [new PurchaseLineRequest(s.Goods, 3, 1000, 300, 270, ReceiptLineId: receipt[0]),
             new PurchaseLineRequest(s.Exempt, 2, 500, ReceiptLineId: receipt[1])]), default));

        // رسید فاکتورشده اصلاح و حذف نمی‌شود و دوباره فاکتور نمی‌شود
        var receiptDoc = await StockDocIdAsync(receipt[0]);
        var locked = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService>(x => x.DeleteAsync(receiptDoc, default)));
        Assert.Contains("فاکتور خرید", locked.Message);
        var twice = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(
            new SavePurchaseInvoiceRequest("1417/03/02", s.Supplier, PayableKind.Trade, null, null, null, null,
                [new PurchaseLineRequest(s.Goods, 3, 1000, ReceiptLineId: receipt[0])]), default)));
        Assert.Contains("قبلاً", twice.Message);

        var invoice = await db.Run<PurchaseInvoiceService, PurchaseInvoiceDto>(x => x.GetAsync(id, default));
        Assert.Equal((4000m, 300m, 3700m, 270m, 3970m),
            (invoice.GrossAmount, invoice.DiscountAmount, invoice.NetAmount, invoice.VatAmount, invoice.Total));
        var v = await Voucher(invoice.Voucher!.VoucherId);
        Assert.True(v.IsBalanced);
        Assert.Equal(["2206", "2541", "5011"], v.Lines.Select(l => l.AccountCode)); // دو کالای یک گروه در یک ردیف
        Assert.Equal(3700m, v.Lines[0].Debit);

        // برگشت ۱ از ۳: اول حواله برگشت انبار، بعد برگشت از خرید با سهم مبلغ پس از تخفیف و ارزش افزوده
        var lineId = invoice.Lines[0].Id;
        var issue1 = await StockAsync(StockDocType.PurchaseReturnIssue, "1417/03/05", s.Supplier,
            new StockLineRequest(s.Goods, 1, SourceLineId: receipt[0]));
        var noIssue = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/05", id, null, [new ReturnLineRequest(lineId, 1)]), default)));
        Assert.Contains("حواله برگشت", noIssue.Message);
        var retId = await db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/05", id, null, [new ReturnLineRequest(lineId, 1, issue1[0])]), default));
        var ret = await db.Run<PurchaseReturnService, PurchaseReturnDto>(x => x.GetAsync(retId, default));
        Assert.Equal((900m, 90m), (ret.Amount, ret.VatAmount));
        var rv = await Voucher(ret.Voucher!.VoucherId);
        Assert.Equal(["5011", "2206", "2541"], rv.Lines.Select(l => l.AccountCode));
        Assert.Equal(990m, rv.Lines[0].Debit);

        // برگشت بقیه: باقی مبلغ دقیق (بدون اختلاف گرد کردن)
        var issue2 = await StockAsync(StockDocType.PurchaseReturnIssue, "1417/03/06", s.Supplier,
            new StockLineRequest(s.Goods, 2, SourceLineId: receipt[0]));
        var ret2 = await db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/06", id, null, [new ReturnLineRequest(lineId, 2, issue2[0])]), default));
        Assert.Equal(1800m, (await db.Run<PurchaseReturnService, PurchaseReturnDto>(x => x.GetAsync(ret2, default))).Amount);

        // گزارش خرید: ۳ خرید با فی پس از تخفیف ۹۰۰، کامل برگشت خورده
        var buyFilter = new PurchaseReportFilter("1417/03/01", "1417/03/06", s.Supplier, s.Goods);
        foreach (var (key, _, _) in PurchaseReportService.Types)
            await db.Run<PurchaseReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync(key, buyFilter, default));
        var buyRow = Assert.Single((await db.Run<PurchaseReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync("items", buyFilter, default))).Rows);
        Assert.Equal((3m, 3m, 2700m, 2700m, 0m), ((decimal)buyRow["qty"]!, (decimal)buyRow["retQty"]!, (decimal)buyRow["purchases"]!,
            (decimal)buyRow["returns"]!, (decimal)buyRow["net"]!));
        var priceRow = Assert.Single((await db.Run<PurchaseReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync("prices", buyFilter, default))).Rows);
        Assert.Equal(900m, priceRow["avg"]);

        var over = await Assert.ThrowsAsync<DomainException>(() => StockAsync(StockDocType.PurchaseReturnIssue, "1417/03/07", s.Supplier,
            new StockLineRequest(s.Goods, 1, SourceLineId: receipt[0])));
        Assert.Contains("قابل برگشت", over.Message);

        // فاکتور دارای برگشت اصلاح و حذف نمی‌شود
        var del = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService>(x => x.DeleteAsync(id, default)));
        Assert.Contains("برگشت دارد", del.Message);
    }

    [Fact]
    public async Task Purchase_rejects_services_and_vat_on_exempt_items()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var service = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(
            new SavePurchaseInvoiceRequest("1417/03/02", s.Supplier, PayableKind.Trade, null, null, null, null,
                [new PurchaseLineRequest(s.Service, 1, 100)]), default)));
        Assert.Contains("شناسایی هزینه", service.Message);

        var exempt = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(
            new SavePurchaseInvoiceRequest("1417/03/02", s.Supplier, PayableKind.Trade, null, null, null, null,
                [new PurchaseLineRequest(s.Exempt, 1, 100, 0, 10)]), default)));
        Assert.Contains("معاف", exempt.Message);
    }

    [Fact]
    public async Task Sale_posts_gross_revenue_discount_and_vat_and_return_is_proportional()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        await StockAsync(StockDocType.PurchaseReceipt, "1417/04/01", s.Supplier, new StockLineRequest(s.Goods, 10));
        var issue = await StockAsync(StockDocType.SalesIssue, "1417/04/01", s.Customer, new StockLineRequest(s.Goods, 4));
        var noIssue = await Assert.ThrowsAsync<DomainException>(() => db.Run<SalesInvoiceService, long>(x => x.CreateAsync(
            new SaveSalesInvoiceRequest("1417/04/01", s.Customer, TaxInvoiceType.WithBuyer, SettlementMethod.Credit, null, null, null,
                [new SalesLineRequest(s.Goods, 4, 2500)]), default)));
        Assert.Contains("حواله فروش", noIssue.Message);

        var id = await db.Run<SalesInvoiceService, long>(x => x.CreateAsync(new SaveSalesInvoiceRequest("1417/04/01", s.Customer,
            TaxInvoiceType.WithBuyer, SettlementMethod.Credit, null, null, "فروش تست",
            [new SalesLineRequest(s.Goods, 4, 2500, 1000, 900, IssueLineId: issue[0])]), default));

        var invoice = await db.Run<SalesInvoiceService, SalesInvoiceDto>(x => x.GetAsync(id, default));
        Assert.Equal((10000m, 1000m, 900m, 9900m), (invoice.GrossAmount, invoice.DiscountAmount, invoice.VatAmount, invoice.Total));
        var v = await Voucher(invoice.Voucher!.VoucherId);
        Assert.Equal((byte)VoucherStatus.Temporary, invoice.Voucher.Status);
        Assert.Equal(["2411", "6301", "6003", "5141"], v.Lines.Select(l => l.AccountCode));
        Assert.Equal((9900m, 1000m, 10000m, 900m), (v.Lines[0].Debit, v.Lines[1].Debit, v.Lines[2].Credit, v.Lines[3].Credit));

        // اول رسید برگشت انبار از همان حواله، بعد برگشت از فروش با سهم نسبی
        var back = await StockAsync(StockDocType.SalesReturnReceipt, "1417/04/03", s.Customer, new StockLineRequest(s.Goods, 1, SourceLineId: issue[0]));
        var retId = await db.Run<SalesReturnService, long>(x => x.CreateAsync(
            new SaveSalesReturnRequest("1417/04/03", id, "مرجوعی", [new SalesReturnLineRequest(invoice.Lines[0].Id, 1, back[0])]), default));
        var ret = await db.Run<SalesReturnService, SalesReturnDto>(x => x.GetAsync(retId, default));
        Assert.Equal((2500m, 250m, 225m, 2475m), (ret.GrossAmount, ret.DiscountAmount, ret.VatAmount, ret.Total));
        var rv = await Voucher(ret.Voucher!.VoucherId);
        Assert.True(rv.IsBalanced);
        Assert.Equal(["6202", "5141", "6301", "2411"], rv.Lines.Select(l => l.AccountCode));

        // گزارش فروش: ۴ فروش، ۱ برگشت؛ پس از تخفیف ۹۰۰۰ منهای برگشت ۲۲۵۰
        var salesFilter = new SalesReportFilter("1417/04/01", "1417/04/03", s.Customer, s.Goods);
        foreach (var (key, _, _) in SalesReportService.Types)
            await db.Run<SalesReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync(key, salesFilter, default));
        var saleRow = Assert.Single((await db.Run<SalesReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync("items", salesFilter, default))).Rows);
        Assert.Equal((4m, 1m, 9000m, 2250m, 6750m), ((decimal)saleRow["qty"]!, (decimal)saleRow["retQty"]!, (decimal)saleRow["sales"]!,
            (decimal)saleRow["returns"]!, (decimal)saleRow["net"]!));
        var vatRow = Assert.Single((await db.Run<SalesReportService, Erp.SharedKernel.Reports.ReportTable>(x => x.RunAsync("vat", salesFilter, default))).Rows);
        Assert.Equal(675m, vatRow["vat"]);                                         // ۹۰۰ − ۲۲۵

        // تحلیل سنی دریافتنی 2411: فاکتور ۹۹۰۰ منهای برگشت ۲۴۷۵ = قلم باز ۷۴۲۵ از ۱۴۱۷/۰۴/۰۱
        var year1417 = (await db.Run<FiscalYearService, List<FiscalYearDto>>(x => x.GetAllAsync(default))).First(y => y.StartDate.StartsWith("1417/")).Id;
        var aging = await db.Run<Erp.Accounting.Application.Reports.AccountAnalysisService, Erp.SharedKernel.Reports.ReportTable>(x => x.AgingAsync(
            new Erp.Accounting.Application.Reports.AgingRequest(year1417, "2411", "1417/04/20"), default));
        Assert.Contains(aging.Rows, row => row.Children!.Any(c => (string?)c["date"] == "1417/04/01" && (decimal?)c["balance"] == 7425m && (decimal?)c["b0"] == 7425m));

        // اصلاح برگشت همان سند را بازنویسی می‌کند؛ حذف برگشت، فاکتور را دوباره قابل اصلاح می‌کند
        await db.Run<SalesReturnService>(x => x.UpdateAsync(retId,
            new SaveSalesReturnRequest("1417/04/03", id, null, [new SalesReturnLineRequest(invoice.Lines[0].Id, 1, back[0])]), default));
        Assert.Equal(ret.Voucher.VoucherId, (await db.Run<SalesReturnService, SalesReturnDto>(x => x.GetAsync(retId, default))).Voucher!.VoucherId);
        await db.Run<SalesReturnService>(x => x.DeleteAsync(retId, default));
        await db.Run<StockDocService>(async x => await x.DeleteAsync(await StockDocIdAsync(back[0]), default));
        await db.Run<SalesInvoiceService>(x => x.DeleteAsync(id, default));
        await Assert.ThrowsAsync<Erp.SharedKernel.Domain.NotFoundException>(() => Voucher(invoice.Voucher.VoucherId));
    }

    [Fact]
    public async Task Service_sale_needs_project_detail_and_mixed_settlement_needs_cash_share()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        // درآمد خدمات (6101) تفصیلی پروژه می‌خواهد
        var project = await Assert.ThrowsAsync<DomainException>(() => db.Run<SalesInvoiceService, long>(x => x.CreateAsync(
            new SaveSalesInvoiceRequest("1417/04/02", s.Customer, TaxInvoiceType.WithBuyer, SettlementMethod.Credit, null, null, null,
                [new SalesLineRequest(s.Service, 1, 5000)]), default)));
        Assert.Contains("6101", project.Message);

        await StockAsync(StockDocType.PurchaseReceipt, "1417/04/02", s.Supplier, new StockLineRequest(s.Goods, 1));
        var issue = await StockAsync(StockDocType.SalesIssue, "1417/04/02", s.Customer, new StockLineRequest(s.Goods, 1));
        var mixed = await Assert.ThrowsAsync<DomainException>(() => db.Run<SalesInvoiceService, long>(x => x.CreateAsync(
            new SaveSalesInvoiceRequest("1417/04/02", s.Customer, TaxInvoiceType.WithBuyer, SettlementMethod.Mixed, 9000, null, null,
                [new SalesLineRequest(s.Goods, 1, 5000, IssueLineId: issue[0])]), default)));
        Assert.Contains("نقد و نسیه", mixed.Message);
    }

    // ------------------------------------------------------------------ انبار

    [Fact]
    public async Task Stock_never_goes_negative_on_any_date()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var item = await NewItemAsync("کالای تست موجودی");
        var first = await StockAsync(StockDocType.PurchaseReceipt, "1417/05/10", s.Supplier, new StockLineRequest(item, 5));

        var tooMuch = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.SalesIssue, "1417/05/11", s.Customer, new StockLineRequest(item, 6)));
        Assert.Contains("منفی", tooMuch.Message);
        // حواله با تاریخ پیش از رسید
        var early = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.SalesIssue, "1417/05/09", s.Customer, new StockLineRequest(item, 1)));
        Assert.Contains("1417/05/09", early.Message);

        // رسید و حواله در یک روز: رسید اول حساب می‌شود
        await StockAsync(StockDocType.PurchaseReceipt, "1417/05/12", s.Supplier, new StockLineRequest(item, 1));
        Assert.Single(await StockAsync(StockDocType.SalesIssue, "1417/05/12", s.Customer, new StockLineRequest(item, 6)));

        // حذف رسیدی که حواله بعدی به آن تکیه دارد
        var receiptDoc = await StockDocIdAsync(first[0]);
        var delete = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService>(x => x.DeleteAsync(receiptDoc, default)));
        Assert.Contains("منفی", delete.Message);

        // کاردکس: ورود ۶، خروج ۶، مانده صفر
        var kardex = await db.Run<StockReportService, KardexDto>(x => x.KardexAsync(item, null, null, null, default));
        Assert.Equal((6m, 6m, 0m), (kardex.TotalIn, kardex.TotalOut, kardex.Closing));
        Assert.Equal([StockDocType.PurchaseReceipt, StockDocType.PurchaseReceipt, StockDocType.SalesIssue], kardex.Rows.Select(r => r.Type));
    }

    [Fact]
    public async Task Return_receipt_needs_invoiced_issue_and_opening_is_first_doc_of_warehouse()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var item = await NewItemAsync("کالای تست برگشت انبار");
        await StockAsync(StockDocType.PurchaseReceipt, "1417/06/01", s.Supplier, new StockLineRequest(item, 3));
        var issue = await StockAsync(StockDocType.SalesIssue, "1417/06/02", s.Customer, new StockLineRequest(item, 3));
        var notInvoiced = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.SalesReturnReceipt, "1417/06/03", s.Customer, new StockLineRequest(item, 1, SourceLineId: issue[0])));
        Assert.Contains("فاکتور نشده", notInvoiced.Message);

        // موجودی اول دوره: روز اول سال مالی، یکی برای هر انبار، فی الزامی
        var wh = await db.Run<WarehouseService, int>(x => x.CreateAsync(new SaveWarehouseRequest("T9", "انبار تست اول دوره"), default));
        var notFirstDay = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService, long>(x => x.CreateAsync(
            new SaveStockDocRequest(StockDocType.Opening, "1417/02/01", wh, null, null, [new StockLineRequest(item, 1, EnteredPrice: 100)]), default)));
        Assert.Contains("روز اول", notFirstDay.Message);
        await db.Run<StockDocService, long>(x => x.CreateAsync(
            new SaveStockDocRequest(StockDocType.Opening, "1417/01/01", wh, null, null, [new StockLineRequest(item, 2, EnteredPrice: 100)]), default));
        var second = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService, long>(x => x.CreateAsync(
            new SaveStockDocRequest(StockDocType.Opening, "1417/01/01", wh, null, null, [new StockLineRequest(item, 1, EnteredPrice: 100)]), default)));
        Assert.Contains("موجودی اول دوره دارد", second.Message);

        var balances = await db.Run<StockReportService, List<StockBalanceDto>>(x => x.BalancesAsync(wh, null, null, false, default));
        Assert.Equal(2m, balances.Single(b => b.ItemId == item).Quantity);

        // انبار دارای سند حذف نمی‌شود
        var del = await Assert.ThrowsAsync<DomainException>(() => db.Run<WarehouseService>(x => x.DeleteAsync(wh, default)));
        Assert.Contains("سند انبار دارد", del.Message);
    }

    [Fact]
    public async Task Consumption_issue_needs_cost_factor_and_center()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var item = await NewItemAsync("کالای تست مصرف");
        await StockAsync(StockDocType.PurchaseReceipt, "1417/07/01", s.Supplier, new StockLineRequest(item, 4));
        var factor = (await db.Run<Erp.Costing.Application.Factors.CostFactorService, List<Erp.Costing.Application.CostFactorDto>>(
            x => x.SearchAsync(null, null, false, default))).First(f => f.Nature != Erp.Costing.Contracts.CostNature.Finance);
        var type = (await db.Run<Erp.Costing.Application.Setup.CostSetupService, List<Erp.Costing.Application.CostCenterTypeDto>>(
            x => x.GetCenterTypesAsync(true, default))).First(t => t.Function == Erp.Costing.Contracts.CostFunction.Admin && t.IsSystem);
        var centerId = await db.Run<Erp.Costing.Application.Centers.CostCenterService, int>(x => x.CreateAsync(
            new Erp.Costing.Application.SaveCostCenterRequest { Title = "اداری تست انبار", CenterTypeId = type.Id }, default));
        var center = (Id: centerId, Title: "اداری تست انبار");

        var noFactor = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.Consumption, "1417/07/02", null, new StockLineRequest(item, 1)));
        Assert.Contains("عامل هزینه", noFactor.Message);
        var noCenter = await Assert.ThrowsAsync<DomainException>(() =>
            StockAsync(StockDocType.Consumption, "1417/07/02", null, new StockLineRequest(item, 1, CostFactorId: factor.Id)));
        Assert.Contains("مرکز هزینه", noCenter.Message);

        var lines = await StockAsync(StockDocType.Consumption, "1417/07/02", null,
            new StockLineRequest(item, 1, CostFactorId: factor.Id, CostCenterId: center.Id));
        var doc = await db.Run<StockDocService, StockDocDto>(async x => await x.GetAsync(await StockDocIdAsync(lines[0]), default));
        Assert.Equal((factor.Title, center.Title), (doc.Lines[0].CostFactor, doc.Lines[0].CostCenter));
    }

    [Fact]
    public async Task Monthly_costing_uses_moving_average_and_posts_cogs_and_consumption()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var item = await NewItemAsync("کالای تست بهای تمام‌شده");

        // آبان ۱۴۱۷: رسید ۱۰ فاکتورنشده (فی واردشده نادیده؛ بها صفر و گزارش)، رسید ۱۰ فاکتورشده به ۱۲۰
        await StockAsync(StockDocType.PurchaseReceipt, "1417/08/01", s.Supplier, new StockLineRequest(item, 10, EnteredPrice: 100));
        var r2 = await StockAsync(StockDocType.PurchaseReceipt, "1417/08/05", s.Supplier, new StockLineRequest(item, 10));
        await db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(new SavePurchaseInvoiceRequest("1417/08/05", s.Supplier,
            PayableKind.Trade, null, null, null, null, [new PurchaseLineRequest(item, 10, 120, ReceiptLineId: r2[0])]), default));

        // فروش ۵ (میانگین ۶۰)، مصرف ۵، برگشت ۱ از فروش
        var issue = await StockAsync(StockDocType.SalesIssue, "1417/08/10", s.Customer, new StockLineRequest(item, 5));
        await db.Run<SalesInvoiceService, long>(x => x.CreateAsync(new SaveSalesInvoiceRequest("1417/08/10", s.Customer,
            TaxInvoiceType.WithBuyer, SettlementMethod.Credit, null, null, null, [new SalesLineRequest(item, 5, 300, IssueLineId: issue[0])]), default));
        var factor = (await db.Run<Erp.Costing.Application.Factors.CostFactorService, List<Erp.Costing.Application.CostFactorDto>>(
            x => x.SearchAsync(null, null, false, default))).First(f => f.Nature != Erp.Costing.Contracts.CostNature.Finance);
        var type = (await db.Run<Erp.Costing.Application.Setup.CostSetupService, List<Erp.Costing.Application.CostCenterTypeDto>>(
            x => x.GetCenterTypesAsync(true, default))).First(t => t.Function == Erp.Costing.Contracts.CostFunction.Selling && t.IsSystem);
        var center = await db.Run<Erp.Costing.Application.Centers.CostCenterService, int>(x => x.CreateAsync(
            new Erp.Costing.Application.SaveCostCenterRequest { Title = "فروش تست بها", CenterTypeId = type.Id }, default));
        await StockAsync(StockDocType.Consumption, "1417/08/12", null, new StockLineRequest(item, 5, CostFactorId: factor.Id, CostCenterId: center));
        await StockAsync(StockDocType.SalesReturnReceipt, "1417/08/15", s.Customer, new StockLineRequest(item, 1, SourceLineId: issue[0]));

        // انتقال ۴ عدد به انبار دیگر: بهای مقصد = میانگین مبدأ (۶۰)؛ انبار یکسان و بیش از موجودی رد
        var source = (await db.Run<WarehouseService, List<WarehouseDto>>(x => x.GetAllAsync(false, default))).First().Id;
        var target = await db.Run<WarehouseService, int>(x => x.CreateAsync(new SaveWarehouseRequest($"T{Guid.NewGuid():N}"[..8], "انبار مقصد تست"), default));
        var same = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockTransferService, long>(x => x.CreateAsync(
            new SaveStockTransferRequest("1417/08/16", source, source, null, [new StockTransferLineRequest(item, 1)]), default)));
        Assert.Contains("یکی باشد", same.Message);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<StockTransferService, long>(x => x.CreateAsync(
            new SaveStockTransferRequest("1417/08/16", source, target, null, [new StockTransferLineRequest(item, 50)]), default)));
        var transferId = await db.Run<StockTransferService, long>(x => x.CreateAsync(
            new SaveStockTransferRequest("1417/08/16", source, target, "انتقال تست", [new StockTransferLineRequest(item, 4)]), default));
        var transfer = await db.Run<StockTransferService, StockTransferDto>(x => x.GetAsync(transferId, default));
        Assert.Equal((source, target, 4m), (transfer.FromWarehouseId, transfer.ToWarehouseId, Assert.Single(transfer.Lines).Quantity));
        var locked = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService>(x => x.DeleteAsync(transfer.ReceiptId, default)));
        Assert.Contains("انتقال", locked.Message);

        var year = (await db.Run<FiscalYearService, List<FiscalYearDto>>(x => x.GetAllAsync(default))).Single(y => y.Title.Contains("1417")).Id;
        var periods = await db.Run<StockCostingService, List<CostPeriodDto>>(x => x.PeriodsAsync(year, default));
        var aban = periods.Single(p => p.PeriodNo == 8);
        var results = await db.Run<StockCostingService, List<CostRunResultDto>>(x => x.RunUpToAsync(aban.PeriodId, default));
        Assert.InRange(results.Count, 1, 8); // از اولین ماه دارای سند تا آبان، هر کدام یک بار
        Assert.Contains(results[^1].Warnings, w => w.Problem.Contains("فاکتور خرید ندارد") && w.Quantity == 10);

        var kardex = await db.Run<StockReportService, KardexDto>(x => x.KardexAsync(item, null, null, null, default));
        Assert.Equal([0m, 1200m, 300m, 300m, 60m, 240m, 240m], kardex.Rows.Select(r => r.Amount!.Value));
        Assert.Equal((11m, 660m), (kardex.Closing, kardex.ClosingAmount));
        var targetKardex = await db.Run<StockReportService, KardexDto>(x => x.KardexAsync(item, target, null, null, default));
        Assert.Equal((4m, 240m), (targetKardex.Closing, targetKardex.ClosingAmount));

        // سند ماه: بهای تمام‌شده 7001، هزینه مصرف، موجودی 2206 و برگشت؛ تراز
        var after = (await db.Run<StockCostingService, List<CostPeriodDto>>(x => x.PeriodsAsync(year, default))).Single(p => p.PeriodNo == 8);
        Assert.Equal(CostRunStatus.Computed, after.Status);
        var v = await Voucher(after.VoucherId!.Value);
        Assert.True(v.IsBalanced);
        Assert.Contains(v.Lines, l => l.AccountCode == "7001" && l.Debit >= 300);
        Assert.Contains(v.Lines, l => l.AccountCode == "2206" && l.Credit >= 600);
        Assert.Contains(v.Lines, l => l.AccountCode.StartsWith('8') && l.Debit == 300);

        // سند انبار تازه در همان ماه، اجرا را «نیازمند محاسبه دوباره» می‌کند؛ اجرای دوباره همان سند را بازنویسی می‌کند
        await StockAsync(StockDocType.PurchaseReceipt, "1417/08/20", s.Supplier, new StockLineRequest(item, 1, EnteredPrice: 100));
        Assert.Equal(CostRunStatus.Stale,
            (await db.Run<StockCostingService, List<CostPeriodDto>>(x => x.PeriodsAsync(year, default))).Single(p => p.PeriodNo == 8).Status);
        var again = await db.Run<StockCostingService, List<CostRunResultDto>>(x => x.RunUpToAsync(aban.PeriodId, default));
        Assert.Equal(after.VoucherId, again[^1].VoucherId);
    }

    private async Task<int> NewItemAsync(string title)
    {
        var groups = await db.Run<ItemGroupService, List<ItemGroupDto>>(x => x.GetAllAsync(false, default));
        var unit = (await db.Run<ItemService, List<ItemUnitDto>>(x => x.GetUnitsAsync(default)))[0].Id;
        return await db.Run<ItemService, int>(x => x.CreateAsync(
            new SaveItemRequest(null, title, groups.Single(g => g.Title == "کالای بازرگانی").Id, unit, CategoryId: 5), default));
    }
}
