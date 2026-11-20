using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Vouchers;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Contracts;
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
                new SaveItemRequest(null, title, groups.Single(g => g.Title == group).Id, unit, VatExempt: exempt), default));
            return _seed = new Seed(supplier, customer, await Item("کالای تست تجاری", "کالای بازرگانی"),
                await Item("خدمت تست", "خدمات"), await Item("کالای معاف تست", "کالای بازرگانی", exempt: true));
        }
        finally
        {
            Lock.Release();
        }
    }

    private Task<VoucherDto> Voucher(long id) => db.Run<VoucherService, VoucherDto>(s => s.GetAsync(id, default));

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
            new SaveItemRequest(goods.Code, goods.Title, goods.GroupId, goods.UnitId, TaxItemId: "12345"), default)));
        Assert.Contains("۱۳ رقم", bad.Message);
    }

    [Fact]
    public async Task Purchase_posts_inventory_vat_and_payable_and_return_reverses_share()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var id = await db.Run<PurchaseInvoiceService, long>(x => x.CreateAsync(new SavePurchaseInvoiceRequest("1417/03/01", s.Supplier,
            PayableKind.Trade, "P-1", "1417/03/01", null, "خرید تست",
            [new PurchaseLineRequest(s.Goods, 3, 1000, 300, 270), new PurchaseLineRequest(s.Exempt, 2, 500)]), default));

        var invoice = await db.Run<PurchaseInvoiceService, PurchaseInvoiceDto>(x => x.GetAsync(id, default));
        Assert.Equal((4000m, 300m, 3700m, 270m, 3970m),
            (invoice.GrossAmount, invoice.DiscountAmount, invoice.NetAmount, invoice.VatAmount, invoice.Total));
        var v = await Voucher(invoice.Voucher!.VoucherId);
        Assert.True(v.IsBalanced);
        Assert.Equal(["2206", "2541", "5011"], v.Lines.Select(l => l.AccountCode)); // دو کالای یک گروه در یک ردیف
        Assert.Equal(3700m, v.Lines[0].Debit);

        // برگشت ۱ از ۳: سهم مبلغ پس از تخفیف و ارزش افزوده
        var lineId = invoice.Lines[0].Id;
        var retId = await db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/05", id, null, [new ReturnLineRequest(lineId, 1)]), default));
        var ret = await db.Run<PurchaseReturnService, PurchaseReturnDto>(x => x.GetAsync(retId, default));
        Assert.Equal((900m, 90m), (ret.Amount, ret.VatAmount));
        var rv = await Voucher(ret.Voucher!.VoucherId);
        Assert.Equal(["5011", "2206", "2541"], rv.Lines.Select(l => l.AccountCode));
        Assert.Equal(990m, rv.Lines[0].Debit);

        // برگشت بقیه: باقی مبلغ دقیق (بدون اختلاف گرد کردن)
        var ret2 = await db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/06", id, null, [new ReturnLineRequest(lineId, 2)]), default));
        Assert.Equal(1800m, (await db.Run<PurchaseReturnService, PurchaseReturnDto>(x => x.GetAsync(ret2, default))).Amount);

        var over = await Assert.ThrowsAsync<DomainException>(() => db.Run<PurchaseReturnService, long>(x => x.CreateAsync(
            new SavePurchaseReturnRequest("1417/03/07", id, null, [new ReturnLineRequest(lineId, 1)]), default)));
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
        var id = await db.Run<SalesInvoiceService, long>(x => x.CreateAsync(new SaveSalesInvoiceRequest("1417/04/01", s.Customer,
            TaxInvoiceType.WithBuyer, SettlementMethod.Credit, null, null, "فروش تست",
            [new SalesLineRequest(s.Goods, 4, 2500, 1000, 900)]), default));

        var invoice = await db.Run<SalesInvoiceService, SalesInvoiceDto>(x => x.GetAsync(id, default));
        Assert.Equal((10000m, 1000m, 900m, 9900m), (invoice.GrossAmount, invoice.DiscountAmount, invoice.VatAmount, invoice.Total));
        var v = await Voucher(invoice.Voucher!.VoucherId);
        Assert.Equal((byte)VoucherStatus.Temporary, invoice.Voucher.Status);
        Assert.Equal(["2411", "6301", "6003", "5141"], v.Lines.Select(l => l.AccountCode));
        Assert.Equal((9900m, 1000m, 10000m, 900m), (v.Lines[0].Debit, v.Lines[1].Debit, v.Lines[2].Credit, v.Lines[3].Credit));

        var retId = await db.Run<SalesReturnService, long>(x => x.CreateAsync(
            new SaveSalesReturnRequest("1417/04/03", id, "مرجوعی", [new SalesReturnLineRequest(invoice.Lines[0].Id, 1)]), default));
        var ret = await db.Run<SalesReturnService, SalesReturnDto>(x => x.GetAsync(retId, default));
        Assert.Equal((2500m, 250m, 225m, 2475m), (ret.GrossAmount, ret.DiscountAmount, ret.VatAmount, ret.Total));
        var rv = await Voucher(ret.Voucher!.VoucherId);
        Assert.True(rv.IsBalanced);
        Assert.Equal(["6202", "5141", "6301", "2411"], rv.Lines.Select(l => l.AccountCode));

        // اصلاح برگشت همان سند را بازنویسی می‌کند؛ حذف برگشت، فاکتور را دوباره قابل اصلاح می‌کند
        await db.Run<SalesReturnService>(x => x.UpdateAsync(retId,
            new SaveSalesReturnRequest("1417/04/03", id, null, [new SalesReturnLineRequest(invoice.Lines[0].Id, 2)]), default));
        Assert.Equal(ret.Voucher.VoucherId, (await db.Run<SalesReturnService, SalesReturnDto>(x => x.GetAsync(retId, default))).Voucher!.VoucherId);
        await db.Run<SalesReturnService>(x => x.DeleteAsync(retId, default));
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

        var mixed = await Assert.ThrowsAsync<DomainException>(() => db.Run<SalesInvoiceService, long>(x => x.CreateAsync(
            new SaveSalesInvoiceRequest("1417/04/02", s.Customer, TaxInvoiceType.WithBuyer, SettlementMethod.Mixed, 9000, null, null,
                [new SalesLineRequest(s.Goods, 1, 5000)]), default)));
        Assert.Contains("نقد و نسیه", mixed.Message);
    }
}
