using Erp.Accounting.Application.FiscalYears;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Inventory.Application;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Production.Application;
using Erp.Production.Domain;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>فرمول ساخت و گزارش تولید با سندهای انبار.</summary>
[Collection(SqlCollection.Name)]
public class ProductionTests(SqlFixture db)
{
    private async Task<int> ItemAsync(string title, byte category)
    {
        var groups = await db.Run<ItemGroupService, List<ItemGroupDto>>(x => x.GetAllAsync(false, default));
        var unit = (await db.Run<ItemService, List<ItemUnitDto>>(x => x.GetUnitsAsync(default)))[0].Id;
        return await db.Run<ItemService, int>(x => x.CreateAsync(
            new SaveItemRequest(null, title, groups.First(g => g.Kind == ItemKind.Goods).Id, unit, CategoryId: category), default));
    }

    private static SaveBomRequest Bom(string code, int product, int warehouse, List<BomMaterialInput> materials, List<BomScrapInput>? scraps = null,
        List<BomOperationInput>? ops = null, decimal baseQty = 100) =>
        new(code, $"فرمول {code}", product, baseQty, warehouse, warehouse, null, null, true, true, null, materials, scraps ?? [], ops ?? []);

    [Fact]
    public async Task Bom_validates_item_types_and_report_creates_stock_docs()
    {
        if (!db.Enabled) return;
        var years = await db.Run<FiscalYearService, List<FiscalYearDto>>(x => x.GetAllAsync(default));
        if (!years.Any(y => string.CompareOrdinal(y.StartDate, "1417/03/10") <= 0 && string.CompareOrdinal(y.EndDate, "1417/03/10") >= 0))
            await db.Run<FiscalYearService, short>(x => x.CreateAsync(1417, 1, default));
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(x => x.GetRoleTypesAsync(false, default));
        var supplier = await db.Run<PersonService, int>(x => x.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "تأمین", LastName = "مواد تولید", RoleIds = [roles.Single(r => r.Code == RoleCodes.Supplier).Id]
        }, default));
        var warehouse = (await db.Run<WarehouseService, List<WarehouseDto>>(x => x.GetAllAsync(false, default))).First().Id;
        var type = (await db.Run<Erp.Costing.Application.Setup.CostSetupService, List<CostCenterTypeDto>>(
            x => x.GetCenterTypesAsync(true, default))).First(t => t.Function == Erp.Costing.Contracts.CostFunction.Production && t.IsSystem);
        var center = await db.Run<CostCenterService, int>(x => x.CreateAsync(new SaveCostCenterRequest { Title = "خط تولید تست", CenterTypeId = type.Id }, default));

        var m1 = await ItemAsync("ماده تست تولید ۱", ItemCategory.Material);
        var m2 = await ItemAsync("ماده جایگزین تست", ItemCategory.Material);
        var product = await ItemAsync("محصول تست تولید", ItemCategory.Product);
        var scrap = await ItemAsync("ضایعات تست تولید", ItemCategory.Scrap);
        var consumable = await ItemAsync("اقلام مصرفی تست تولید", ItemCategory.Consumable);

        // نوع کالا در فرمول
        var badMaterial = await Assert.ThrowsAsync<DomainException>(() => db.Run<BomService, int>(x => x.CreateAsync(
            Bom("T-BAD1", product, warehouse, [new BomMaterialInput(consumable, 1, null, 0, null, [])]), default)));
        Assert.Contains("در تولید مصرف نمی‌شود", badMaterial.Message);
        var badScrap = await Assert.ThrowsAsync<DomainException>(() => db.Run<BomService, int>(x => x.CreateAsync(
            Bom("T-BAD2", product, warehouse, [new BomMaterialInput(m1, 1, null, 0, null, [])], [new BomScrapInput(m2, 1, ScrapType.Full)]), default)));
        Assert.Contains("ضایعات", badScrap.Message);

        // زمان استاندارد فقط روی مرکز تولیدی؛ فهرست مراکز فقط تولیدی‌ها
        var adminType = (await db.Run<Erp.Costing.Application.Setup.CostSetupService, List<CostCenterTypeDto>>(
            x => x.GetCenterTypesAsync(true, default))).First(t => t.Function == Erp.Costing.Contracts.CostFunction.Admin);
        var adminCenter = await db.Run<CostCenterService, int>(x => x.CreateAsync(new SaveCostCenterRequest { Title = "اداری تست تولید", CenterTypeId = adminType.Id }, default));
        var badCenter = await Assert.ThrowsAsync<DomainException>(() => db.Run<BomService, int>(x => x.CreateAsync(
            Bom("T-BAD3", product, warehouse, [new BomMaterialInput(m1, 1, null, 0, null, [])], [], [new BomOperationInput(adminCenter, 60, 0, null)]), default)));
        Assert.Contains("تولیدی نیست", badCenter.Message);
        var centers = await db.Run<BomService, List<LookupDto>>(x => x.CentersAsync(default));
        Assert.Contains(centers, c => c.Id == center);
        Assert.DoesNotContain(centers, c => c.Id == adminCenter);

        // فرمول: ۱۰۰ واحد محصول از ۵۰ ماده با ۲٪ افت؛ جایگزین: ۱٫۲ برابر ماده دوم؛ ۵ ضایعات؛ ۱ نفر-ساعت و نیم ماشین-ساعت
        var bomId = await db.Run<BomService, int>(x => x.CreateAsync(Bom("T-001", product, warehouse,
            [new BomMaterialInput(m1, 50, null, 2, null, [new BomSubstituteInput(1, m2, 1.2m)])],
            [new BomScrapInput(scrap, 5, ScrapType.Full)], [new BomOperationInput(center, 3600, 1800, null)]), default));

        var draft = await db.Run<ProductionReportService, ProductionDraftDto>(x => x.DraftAsync(bomId, 200, default));
        var mat = Assert.Single(draft.Materials);
        Assert.Equal(102m, mat.StandardQty);                       // ۵۰ × ۲ × ۱٫۰۲
        Assert.Equal(1.2m, Assert.Single(Assert.Single(mat.Options).Items).Factor);
        Assert.Equal(10m, Assert.Single(draft.Scraps).StandardQty);
        Assert.Equal(7200m, Assert.Single(draft.Operations).StdManSeconds);

        // مواد برای تولید
        await db.Run<StockDocService, long>(x => x.CreateAsync(new SaveStockDocRequest(StockDocType.PurchaseReceipt, "1417/03/01", warehouse, supplier, null,
            [new StockLineRequest(m1, 300)]), default));

        var request = new SaveProductionReportRequest("1417/03/10", bomId, 200, warehouse, null,
            [new ProductionMaterialInput(m1, warehouse, 102, 110, mat.BomMaterialId, null)],
            [new ProductionScrapInput(scrap, 10, 12, ScrapType.Full)],
            [new ProductionOperationInput(center, 7200, 3600, 8000, 3600)], []);
        var reportId = await db.Run<ProductionReportService, long>(x => x.CreateAsync(request, default));
        var report = await db.Run<ProductionReportService, ProductionReportDto>(x => x.GetAsync(reportId, default));
        Assert.Equal(2, report.StockDocs.Count);
        var issue = report.StockDocs.Single(d => d.Type == StockDocType.ProductionIssue);
        var receipt = report.StockDocs.Single(d => d.Type == StockDocType.ProductionReceipt);
        Assert.Equal(110m, Assert.Single((await db.Run<StockDocService, StockDocDto>(x => x.GetAsync(issue.Id, default))).Lines).Quantity);
        var receiptLines = (await db.Run<StockDocService, StockDocDto>(x => x.GetAsync(receipt.Id, default))).Lines;
        Assert.Contains(receiptLines, l => l.ItemId == product && l.Quantity == 200);
        Assert.Contains(receiptLines, l => l.ItemId == scrap && l.Quantity == 12);

        // سند تولید از فرم انبار اصلاح نمی‌شود
        var locked = await Assert.ThrowsAsync<DomainException>(() => db.Run<StockDocService>(x => x.DeleteAsync(issue.Id, default)));
        Assert.Contains("گزارش تولید", locked.Message);

        // اصلاح گزارش: همان حواله بازنویسی می‌شود؛ مصرف بیش از موجودی رد
        await db.Run<ProductionReportService>(x => x.UpdateAsync(reportId, request with
        {
            Materials = [new ProductionMaterialInput(m1, warehouse, 102, 120, mat.BomMaterialId, null)]
        }, default));
        var after = await db.Run<ProductionReportService, ProductionReportDto>(x => x.GetAsync(reportId, default));
        Assert.Equal(issue.Id, after.StockDocs.Single(d => d.Type == StockDocType.ProductionIssue).Id);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<ProductionReportService>(x => x.UpdateAsync(reportId, request with
        {
            Materials = [new ProductionMaterialInput(m1, warehouse, 102, 400, mat.BomMaterialId, null)]
        }, default)));

        // فرمول به‌کاررفته حذف نمی‌شود؛ حذف گزارش سندهای انبار را هم حذف می‌کند
        await Assert.ThrowsAsync<DomainException>(() => db.Run<BomService>(x => x.DeleteAsync(bomId, default)));
        await db.Run<ProductionReportService>(x => x.DeleteAsync(reportId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => db.Run<StockDocService, StockDocDto>(x => x.GetAsync(issue.Id, default)));

        // دور در فرمول‌های چندسطحی: نیم‌ساخته ۱ از ۲ و ۲ از ۱
        var semi1 = await ItemAsync("نیم‌ساخته تست ۱", ItemCategory.SemiFinished);
        var semi2 = await ItemAsync("نیم‌ساخته تست ۲", ItemCategory.SemiFinished);
        await db.Run<BomService, int>(x => x.CreateAsync(Bom("T-S2", semi2, warehouse, [new BomMaterialInput(semi1, 1, null, 0, null, [])]), default));
        var cycle = await Assert.ThrowsAsync<DomainException>(() => db.Run<BomService, int>(x => x.CreateAsync(
            Bom("T-S1", semi1, warehouse, [new BomMaterialInput(semi2, 1, null, 0, null, [])]), default)));
        Assert.Contains("دور", cycle.Message);
    }
}
