using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.FiscalYears;
using Erp.Accounting.Application.Vouchers;
using Erp.Accounting.Domain.Details;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Costing.Application.Factors;
using Erp.Costing.Application.Setup;
using Erp.Costing.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>ساختار هزینه‌ها، عوامل و مراکز هزینه، تسهیم و کنترل آن‌ها در ثبت سند.</summary>
[Collection(SqlCollection.Name)]
public class CostingTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(short YearId, int Production, int Service, int Admin, string ProductionCode, string ServiceCode,
        string AdminCode);

    private Task<List<CostCenterTypeDto>> CenterTypes() =>
        db.Run<CostSetupService, List<CostCenterTypeDto>>(s => s.GetCenterTypesAsync(true, default));

    private async Task<short> CenterType(CostFunction function) =>
        (await CenterTypes()).First(t => t.Function == function && t.IsSystem).Id;

    private Task<List<CostFactorDto>> Factors(string? term = null) =>
        db.Run<CostFactorService, List<CostFactorDto>>(s => s.SearchAsync(term, null, null, true, default));

    private async Task<string> FactorCode(string title) => (await Factors()).Single(f => f.Title == title).Code!;

    private Task<int> CreateCenter(SaveCostCenterRequest r) => db.Run<CostCenterService, int>(s => s.CreateAsync(r, default));

    private async Task<string> CenterCode(int id) => (await db.Run<CostCenterService, CostCenterDetailsDto>(s => s.GetAsync(id, default))).Code!;

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;
            var year = await db.Run<FiscalYearService, short>(s => s.CreateAsync(1408, 1, default));
            var production = await CreateCenter(new SaveCostCenterRequest { Title = "خط تولید یک", CenterTypeId = await CenterType(CostFunction.Production) });
            var service = await CreateCenter(new SaveCostCenterRequest { Title = "تعمیرات و نگهداری", CenterTypeId = await CenterType(CostFunction.Service) });
            var admin = await CreateCenter(new SaveCostCenterRequest { Title = "مدیریت مالی", CenterTypeId = await CenterType(CostFunction.Admin) });
            return _seed = new Seed(year, production, service, admin, await CenterCode(production), await CenterCode(service), await CenterCode(admin));
        }
        finally
        {
            Lock.Release();
        }
    }

    private Task<long> Draft(short yearId, params VoucherLineRequest[] lines) =>
        db.Run<VoucherService, long>(s => s.CreateAsync(new SaveVoucherRequest(yearId, "1408/02/01", "تست هزینه", [.. lines]), default));

    private static VoucherLineRequest Line(string account, decimal debit, decimal credit, params string?[] details) =>
        new(account, details.Length == 0 ? null : [.. details], debit, credit);

    [Fact]
    public async Task Expense_structure_and_default_detail_rules_are_seeded()
    {
        if (!db.Enabled) return;
        var tree = await db.Run<AccountService, List<AccountNodeDto>>(s => s.GetTreeAsync(default));
        var group7 = tree.Single(g => g.Code == "7");
        Assert.Equal(["7001", "7002"], group7.Children.Single(k => k.Code == "70").Children.Select(m => m.Code));
        Assert.Equal(4, group7.Children.Single(k => k.Code == "71").Children.Count);
        Assert.Single(group7.Children.Single(k => k.Code == "76").Children);

        var types = await db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default));
        var factorType = types.Single(t => t.OwnerModule == OwnerModules.CostFactors);
        var centerType = types.Single(t => t.OwnerModule == OwnerModules.CostCenters);
        Assert.Equal(("09001", "08101"), (factorType.CodeFrom, centerType.CodeFrom));
        Assert.True(factorType.UsedCount >= 59);

        var salary = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("7101", default));
        Assert.Equal((CostNature.Payroll, CostFunction.Production), (salary.CostNature!.Value, salary.CostFunction!.Value));
        Assert.Equal([factorType.Id], salary.DetailLevels[0].DetailTypeIds);
        Assert.Equal([centerType.Id], salary.DetailLevels[1].DetailTypeIds);
        Assert.All(salary.DetailLevels, l => Assert.True(l.IsRequired));

        var finance = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("7601", default));
        Assert.Null(finance.CostFunction);
        Assert.False(finance.DetailLevels[1].IsRequired);

        Assert.Empty((await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("7001", default))).DetailLevels);
        Assert.True((await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("2603", default))).DetailLevels[0].IsRequired);

        var legalAr = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("2304", default));
        var legalType = types.Single(t => t.OwnerModule == OwnerModules.Persons && t.PersonKind == Erp.BaseData.Contracts.PersonKind.Legal);
        Assert.Equal([legalType.Id], legalAr.DetailLevels[0].DetailTypeIds);
        Assert.Single(legalAr.DetailLevels[0].RoleIds);

        var factors = await Factors();
        Assert.Contains(factors, f => f.Title == "اضافه‌کاری" && f.Behavior == CostBehavior.Variable && f.Nature == CostNature.Payroll);
        Assert.All(factors, f => Assert.NotNull(f.Code));
    }

    [Fact]
    public async Task Voucher_accepts_matching_factor_and_center_only()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();
        var basePay = await FactorCode("حقوق پایه");
        var buildingDep = await FactorCode("استهلاک ساختمان");
        var interest = await FactorCode("سود تضمین‌شده و کارمزد تسهیلات");

        var ok = await Draft(seed.YearId,
            Line("7101", 1000, 0, basePay, seed.ProductionCode),
            Line("7102", 500, 0, basePay, seed.ServiceCode),
            Line("7601", 200, 0, interest),
            Line("5203", 0, 1700));
        Assert.True(ok > 0);

        var wrongNature = await Assert.ThrowsAsync<DomainException>(() =>
            Draft(seed.YearId, Line("7101", 100, 0, buildingDep, seed.ProductionCode), Line("5203", 0, 100)));
        Assert.Contains("استهلاک", wrongNature.Message);

        var wrongFunction = await Assert.ThrowsAsync<DomainException>(() =>
            Draft(seed.YearId, Line("7101", 100, 0, basePay, seed.AdminCode), Line("5203", 0, 100)));
        Assert.Contains("اداری", wrongFunction.Message);

        var missingCenter = await Assert.ThrowsAsync<DomainException>(() =>
            Draft(seed.YearId, Line("7101", 100, 0, basePay), Line("5203", 0, 100)));
        Assert.Contains("سطح 2", missingCenter.Message);

        // انتخاب در فرم سند هم همین فیلتر را دارد
        var account = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("7101", default));
        var factorsForSalary = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(
            account.DetailLevels[0].DetailTypeIds, null, null, account.CostNature, account.CostFunction, default));
        Assert.Contains(factorsForSalary, d => d.Code == basePay);
        Assert.DoesNotContain(factorsForSalary, d => d.Code == buildingDep);
        var centersForSalary = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(
            account.DetailLevels[1].DetailTypeIds, null, null, account.CostNature, account.CostFunction, default));
        Assert.Equal([seed.ProductionCode], centersForSalary.Select(d => d.Code));

        // مواد مصرفی بدون تفصیلی
        Assert.True(await Draft(seed.YearId, Line("7001", 300, 0), Line("7002", 100, 0), Line("2203", 0, 400)) > 0);
    }

    [Fact]
    public async Task Factor_rules_and_detail_sync()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();
        var types = await db.Run<CostSetupService, List<ExpenseTypeDto>>(s => s.GetExpenseTypesAsync(true, default));
        var energy = types.Single(t => t.Title == "انرژی و آب").Id;
        var repairs = types.Single(t => t.Title == "تعمیر و نگهداری").Id;
        var payroll = types.First(t => t.Nature == CostNature.Payroll).Id;

        var noPercent = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostFactorService, int>(s =>
            s.CreateAsync(new SaveCostFactorRequest("بخار", energy, CostBehavior.Mixed, null, true, null), default)));
        Assert.Contains("درصد", noPercent.Message);

        var id = await db.Run<CostFactorService, int>(s =>
            s.CreateAsync(new SaveCostFactorRequest("بخار", energy, CostBehavior.Mixed, 40, true, null), default));
        var factor = (await Factors("بخار")).Single();
        Assert.Equal((40m, CostNature.OtherOperating), (factor.VariablePercent!.Value, factor.Nature));
        Assert.StartsWith("09", factor.Code);

        // تغییر نوع به گروه دیگر وقتی در سند نیامده مجاز است و ماهیت تفصیلی هم عوض می‌شود
        await db.Run<CostFactorService>(s => s.UpdateAsync(id, new SaveCostFactorRequest("بخار", payroll, CostBehavior.Fixed, 55, true, null), default));
        var changed = (await Factors("بخار")).Single();
        Assert.Equal((CostNature.Payroll, (decimal?)null), (changed.Nature, changed.VariablePercent));
        await Draft(seed.YearId, Line("7103", 10, 0, changed.Code, seed.AdminCode), Line("5203", 0, 10));

        // در سند آمده: گروه هزینه عوض نمی‌شود، داخل همان گروه می‌شود؛ حذف هم نمی‌شود
        var used = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostFactorService>(s =>
            s.UpdateAsync(id, new SaveCostFactorRequest("بخار", repairs, CostBehavior.Fixed, null, true, null), default)));
        Assert.Contains("در سند آمده", used.Message);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<CostFactorService>(s => s.DeleteAsync(id, default)));

        // تفصیلی دستی در گروه عوامل ساخته نمی‌شود
        var dt = (await db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default))).Single(t => t.OwnerModule == OwnerModules.CostFactors);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService, int>(s =>
            s.CreateAccountAsync(new CreateDetailAccountRequest(dt.Id, "دستی", null), default)));

        // عامل بدون گردش با تفصیلی‌اش حذف می‌شود
        var temp = await db.Run<CostFactorService, int>(s =>
            s.CreateAsync(new SaveCostFactorRequest("عامل موقت", repairs, CostBehavior.Fixed, null, true, null), default));
        var tempCode = (await Factors("عامل موقت")).Single().Code;
        await db.Run<CostFactorService>(s => s.DeleteAsync(temp, default));
        var left = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync((short?)null, tempCode, default));
        Assert.DoesNotContain(left, d => d.Code == tempCode);
    }

    [Fact]
    public async Task Center_allocation_rules()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();
        var bases = await db.Run<CostSetupService, List<CostBaseDto>>(s => s.GetCostBasesAsync(default));
        var machineHours = bases.Single(b => b.Title == "ساعت کار ماشین").Id;
        var headcount = bases.Single(b => b.Title == "تعداد پرسنل (نفر)").Id;
        var line2 = await CreateCenter(new SaveCostCenterRequest
        {
            Title = "خط تولید دو", CenterTypeId = await CenterType(CostFunction.Production), AbsorptionBaseId = machineHours
        });

        // خدماتی: به تولیدی‌ها با جمع ۱۰۰
        var maintenance = await db.Run<CostCenterService, CostCenterDetailsDto>(s => s.GetAsync(seed.Service, default));
        maintenance.AllocationBaseId = machineHours;
        maintenance.AllocationOrder = 1;
        maintenance.Allocations = [new() { CenterId = seed.Production, Percent = 60 }, new() { CenterId = line2, Percent = 40 }];
        await db.Run<CostCenterService>(s => s.UpdateAsync(seed.Service, maintenance, default));
        var saved = await db.Run<CostCenterService, CostCenterDetailsDto>(s => s.GetAsync(seed.Service, default));
        Assert.Equal([60m, 40m], saved.Allocations.Select(a => a.Percent));

        // ویرایش سهم‌ها (همان گیرنده‌ها) و حذف یکی
        saved.Allocations = [new() { CenterId = seed.Production, Percent = 100 }];
        await db.Run<CostCenterService>(s => s.UpdateAsync(seed.Service, saved, default));
        Assert.Single((await db.Run<CostCenterService, CostCenterDetailsDto>(s => s.GetAsync(seed.Service, default))).Allocations);

        saved.Allocations = [new() { CenterId = seed.Production, Percent = 70 }];
        var sum = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostCenterService>(s => s.UpdateAsync(seed.Service, saved, default)));
        Assert.Contains("۱۰۰", sum.Message);

        saved.Allocations = [new() { CenterId = seed.Admin, Percent = 100 }];
        var toAdmin = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostCenterService>(s => s.UpdateAsync(seed.Service, saved, default)));
        Assert.Contains("تولیدی یا خدماتی", toAdmin.Message);

        // تولیدی تسهیم نمی‌شود؛ مبنای جذب فقط برای تولیدی
        var productionType = await CenterType(CostFunction.Production);
        var serviceType = await CenterType(CostFunction.Service);
        var prodAlloc = await Assert.ThrowsAsync<DomainException>(() => CreateCenter(new SaveCostCenterRequest
        {
            Title = "خط سه", CenterTypeId = productionType, AllocationBaseId = headcount
        }));
        Assert.Contains("مبنای جذب", prodAlloc.Message);
        await Assert.ThrowsAsync<DomainException>(() => CreateCenter(new SaveCostCenterRequest
        {
            Title = "انبار مرکزی", CenterTypeId = serviceType, AbsorptionBaseId = machineHours
        }));

        // مرکز گیرنده تسهیم حذف نمی‌شود
        var del = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostCenterService>(s => s.DeleteAsync(seed.Production, default)));
        Assert.Contains("گیرنده تسهیم", del.Message);
        var deletable = await CreateCenter(new SaveCostCenterRequest { Title = "مرکز موقت", CenterTypeId = await CenterType(CostFunction.Admin) });
        await db.Run<CostCenterService>(s => s.DeleteAsync(deletable, default));

        var list = await db.Run<CostCenterService, List<CostCenterListItemDto>>(s => s.GetAllAsync(true, default));
        Assert.Contains(list, c => c.Id == seed.Service && c.TargetCount == 1 && c.AllocationBaseTitle == "ساعت کار ماشین");
        Assert.Contains(list, c => c.Id == line2 && c.AbsorptionBaseTitle == "ساعت کار ماشین" && c.Code!.StartsWith("081"));
    }

    [Fact]
    public async Task Center_function_is_locked_after_use_and_detail_is_one_transaction()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();
        await Draft(seed.YearId, Line("7303", 50, 0, await FactorCode("ملزومات و نوشت‌افزار"), seed.AdminCode), Line("5203", 0, 50));

        var admin = await db.Run<CostCenterService, CostCenterDetailsDto>(s => s.GetAsync(seed.Admin, default));
        admin.CenterTypeId = await CenterType(CostFunction.Selling);
        var ex = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostCenterService>(s => s.UpdateAsync(seed.Admin, admin, default)));
        Assert.Contains("همان وظیفه", ex.Message);

        // نوع مرکز تعریف کاربر با همان وظیفه مجاز است
        var custom = await db.Run<CostSetupService, short>(s => s.CreateCenterTypeAsync(
            new SaveCostCenterTypeRequest("اداری ستادی", CostFunction.Admin, 10, true), default));
        admin.CenterTypeId = custom;
        await db.Run<CostCenterService>(s => s.UpdateAsync(seed.Admin, admin, default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<CostSetupService>(s => s.DeleteCenterTypeAsync(custom, default)));
        var system = await CenterType(CostFunction.Admin);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<CostSetupService>(s => s.DeleteCenterTypeAsync(system, default)));

        // گروه تفصیلی مراکز غیرفعال: مرکز هم ثبت نمی‌شود
        var types = await db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default));
        var ct = types.Single(t => t.OwnerModule == OwnerModules.CostCenters);
        await db.Run<DetailService>(s => s.UpdateTypeAsync(ct.Id, new UpdateDetailTypeRequest(ct.Title, ct.OwnerModule, null, ct.CodeFrom, ct.CodeTo, false), default));
        try
        {
            await Assert.ThrowsAsync<DomainException>(() => CreateCenter(new SaveCostCenterRequest
            {
                Title = "مرکز بی‌تفصیلی", CenterTypeId = system
            }));
            var all = await db.Run<CostCenterService, List<CostCenterListItemDto>>(s => s.GetAllAsync(true, default));
            Assert.DoesNotContain(all, c => c.Title == "مرکز بی‌تفصیلی");
        }
        finally
        {
            await db.Run<DetailService>(s => s.UpdateTypeAsync(ct.Id, new UpdateDetailTypeRequest(ct.Title, ct.OwnerModule, null, ct.CodeFrom, ct.CodeTo, true), default));
        }
    }

    [Fact]
    public async Task Expense_type_nature_is_locked_when_its_factors_are_used()
    {
        if (!db.Enabled) return;
        var seed = await SeedAsync();
        var types = await db.Run<CostSetupService, List<ExpenseTypeDto>>(s => s.GetExpenseTypesAsync(true, default));
        var cash = types.Single(t => t.Title == "حقوق و مزایای نقدی");
        await Draft(seed.YearId, Line("7101", 10, 0, await FactorCode("حق مسکن"), seed.ProductionCode), Line("5203", 0, 10));

        var ex = await Assert.ThrowsAsync<DomainException>(() => db.Run<CostSetupService>(s =>
            s.UpdateExpenseTypeAsync(cash.Id, new SaveExpenseTypeRequest(cash.Title, CostNature.OtherOperating, cash.SortOrder, true), default)));
        Assert.Contains("در سند آمده", ex.Message);

        // تغییر عنوان مجاز است؛ حذف نوعِ دارای عامل نه
        await db.Run<CostSetupService>(s => s.UpdateExpenseTypeAsync(cash.Id, new SaveExpenseTypeRequest("حقوق و مزایای نقدی کارکنان", CostNature.Payroll, 1, true), default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<CostSetupService>(s => s.DeleteExpenseTypeAsync(cash.Id, default)));
        var empty = await db.Run<CostSetupService, short>(s => s.CreateExpenseTypeAsync(new SaveExpenseTypeRequest("نوع خالی", CostNature.Finance, 9, true), default));
        await db.Run<CostSetupService>(s => s.DeleteExpenseTypeAsync(empty, default));
    }
}
