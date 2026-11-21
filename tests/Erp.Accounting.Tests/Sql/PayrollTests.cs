using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Payroll.Application;
using Erp.Payroll.Domain;
using Erp.Personnel.Application;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>کارگزینی پایه و پایه‌های حقوق: پرونده پرسنلی، اقلام با فرمول، پارامترهای سال، حکم.</summary>
[Collection(SqlCollection.Name)]
public class PayrollTests(SqlFixture db)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Seed? _seed;

    private sealed record Seed(int Workshop, int Job, int Center, int Employee, int Customer);

    private async Task<Seed> SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seed is not null) return _seed;
            var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
            short Role(string code) => roles.Single(r => r.Code == code).Id;
            var person = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "کارمند", LastName = "حقوق تست", NationalId = PersonTests.NewNationalCode(), RoleIds = [Role(RoleCodes.Employee)] }, default));
            var customer = await db.Run<PersonService, int>(s => s.CreateAsync(
                new SavePersonRequest { Kind = PersonKind.Natural, FirstName = "مشتری", LastName = "بی‌پرونده", RoleIds = [Role(RoleCodes.Customer)] }, default));
            var workshop = await db.Run<WorkshopService, int>(s => s.CreateAsync(new SaveWorkshopRequest("9900112233", "کارگاه تست حقوق"), default));
            var job = await db.Run<JobService, int>(s => s.CreateAsync(new SaveJobRequest("کارشناس تست حقوق", "123456"), default));
            var type = (await db.Run<Erp.Costing.Application.Setup.CostSetupService, List<CostCenterTypeDto>>(
                s => s.GetCenterTypesAsync(true, default))).First(t => t.Function == Erp.Costing.Contracts.CostFunction.Admin && t.IsSystem);
            var center = await db.Run<CostCenterService, int>(s => s.CreateAsync(
                new SaveCostCenterRequest { Title = "اداری تست حقوق", CenterTypeId = type.Id }, default));
            var employee = await db.Run<EmployeeService, int>(s => s.CreateAsync(new SaveEmployeeRequest(person, null, workshop, job,
                ContractType.Temporary, "1405/01/15", "1234567890", InsuranceStatus.Insured, 2, center), default));
            return _seed = new Seed(workshop, job, center, employee, customer);
        }
        finally
        {
            Lock.Release();
        }
    }

    [Fact]
    public async Task Employee_needs_employee_role_and_gets_personnel_number()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var dto = await db.Run<EmployeeService, EmployeeDto>(x => x.GetAsync(s.Employee, default));
        Assert.True(long.Parse(dto.PersonnelNo) > 1000);
        Assert.Equal(s.Center, dto.CostCenterId);

        var noRole = await Assert.ThrowsAsync<DomainException>(() => db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(
            s.Customer, null, s.Workshop, null, ContractType.Temporary, "1405/01/15", null, InsuranceStatus.NotInsured, 0, s.Center), default)));
        Assert.Contains("کارمند", noRole.Message);

        var twice = await Assert.ThrowsAsync<DomainException>(() => db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(
            dto.PersonId, null, s.Workshop, null, ContractType.Temporary, "1405/01/15", "1234567890", InsuranceStatus.Insured, 0, s.Center), default)));
        Assert.Contains("پرونده پرسنلی دارد", twice.Message);

        var used = await Assert.ThrowsAsync<DomainException>(() => db.Run<WorkshopService>(x => x.DeleteAsync(s.Workshop, default)));
        Assert.Contains("کارمند دارد", used.Message);
    }

    [Fact]
    public async Task Pay_item_formulas_are_validated()
    {
        if (!db.Enabled) return;
        await SeedAsync();
        var factor = (await db.Run<PayItemService, List<PayItemDto>>(x => x.GetAllAsync(false, default))).First(i => i.Code == "BASE").CostFactorId;
        Assert.NotNull(factor);

        SavePayItemRequest Earning(string code, string formula) =>
            new(code, $"قلم تست {code}", PayItemKind.Earning, PayItemSource.Formula, formula, true, true, factor, null);

        var unknown = await db.Run<PayItemService, FormulaCheckDto>(x => x.CheckFormulaAsync(null, Earning("T_UNKNOWN", "NOPE * 2"), default));
        Assert.False(unknown.Ok);
        Assert.Contains("NOPE", unknown.Error);

        var aggregate = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayItemService, int>(x =>
            x.CreateAsync(Earning("T_AGG", "INS_SUBJECT / 10"), default)));
        Assert.Contains("وابسته", aggregate.Message);

        var a = await db.Run<PayItemService, int>(x => x.CreateAsync(Earning("T_A", "ROUND(DAILY_WAGE * DAYS / 10, 0)"), default));
        var b = await db.Run<PayItemService, int>(x => x.CreateAsync(Earning("T_B", "T_A * 2"), default));
        var cycle = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayItemService>(x => x.UpdateAsync(a, Earning("T_A", "T_B + 1"), default)));
        Assert.Contains("دور", cycle.Message);

        var inUse = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayItemService>(x => x.DeleteAsync(a, default)));
        Assert.Contains("فرمول", inUse.Message);
        await db.Run<PayItemService>(x => x.DeleteAsync(b, default));
        await db.Run<PayItemService>(x => x.DeleteAsync(a, default));

        var system = (await db.Run<PayItemService, List<PayItemDto>>(x => x.GetAllAsync(false, default))).First(i => i.Code == "INS_WORKER");
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PayItemService>(x => x.DeleteAsync(system.Id, default)));
    }

    [Fact]
    public async Task Year_params_copy_to_next_year()
    {
        if (!db.Enabled) return;
        var copied = await db.Run<YearParamService, int>(x => x.CopyAsync(1405, 1416, default));
        Assert.True(copied >= 10 || copied == 0);
        var list = await db.Run<YearParamService, List<YearParamDto>>(x => x.ListAsync(1416, default));
        Assert.Contains(list, p => p.Code == "MIN_WAGE" && p.Value == 5_541_850);
        await db.Run<YearParamService>(x => x.SaveAsync(1416, new SaveYearParamRequest("MIN_WAGE", "حداقل مزد روزانه", 6_000_000), default));
        Assert.Equal(6_000_000, (await db.Run<YearParamService, List<YearParamDto>>(x => x.ListAsync(1416, default))).Single(p => p.Code == "MIN_WAGE").Value);
    }

    [Fact]
    public async Task Decree_requires_daily_wage_and_valid_date()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var draft = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(s.Employee, default));
        Assert.Equal(DecreeType.Hire, draft.DecreeType);
        Assert.Equal("1405/01/15", draft.EffectiveDate);
        var wage = draft.Items.Single(i => i.Code == DecreeService.DailyWageCode).PayItemId;

        var noWage = await Assert.ThrowsAsync<DomainException>(() => db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(s.Employee, "1405/01/15", DecreeType.Hire, null, [new DecreeItemInput(wage, 0)]), default)));
        Assert.Contains("مزد روزانه", noWage.Message);

        var early = await Assert.ThrowsAsync<DomainException>(() => db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(s.Employee, "1405/01/01", DecreeType.Hire, null, [new DecreeItemInput(wage, 6_000_000)]), default)));
        Assert.Contains("استخدام", early.Message);

        var id = await db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(s.Employee, "1405/01/15", DecreeType.Hire, "حکم استخدام", [new DecreeItemInput(wage, 6_000_000)]), default));
        var list = await db.Run<DecreeService, List<DecreeListItemDto>>(x => x.ListAsync(s.Employee, default));
        Assert.Equal(6_000_000, list.Single(d => d.Id == id).DailyWage);

        var next = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(s.Employee, default));
        Assert.Equal(6_000_000, next.Items.Single(i => i.PayItemId == wage).Amount);

        var used = await Assert.ThrowsAsync<DomainException>(() => db.Run<EmployeeService>(x => x.DeleteAsync(s.Employee, default)));
        Assert.Contains("ترک کار", used.Message);
        var sameDay = await Assert.ThrowsAsync<DomainException>(() => db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(s.Employee, "1405/01/15", DecreeType.Other, null, [new DecreeItemInput(wage, 7_000_000)]), default)));
        Assert.Contains("حکم دیگری", sameDay.Message);
    }
}
