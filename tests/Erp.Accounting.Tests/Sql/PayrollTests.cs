using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.Costing.Application;
using Erp.Costing.Application.Centers;
using Erp.Payroll.Application;
using Erp.Payroll.Domain;
using Erp.Personnel.Application;
using Erp.Personnel.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Reports;

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
    public async Task Template_and_group_rules_drive_monthly_calculation()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(x => x.GetRoleTypesAsync(false, default));
        var person = await db.Run<PersonService, int>(x => x.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "کارمند", LastName = "الگوی محاسبه", NationalId = PersonTests.NewNationalCode(),
            RoleIds = [roles.Single(r => r.Code == RoleCodes.Employee).Id]
        }, default));
        var workshop = await db.Run<WorkshopService, int>(x => x.CreateAsync(new SaveWorkshopRequest("9900887766", "کارگاه تست الگو"), default));
        var employee = await db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(person, null, workshop, null,
            ContractType.Temporary, "1405/01/15", "1234567892", InsuranceStatus.Insured, 0, s.Center), default));
        var draft = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(employee, default));
        var wage = draft.Items.Single(i => i.Code == DecreeService.DailyWageCode).PayItemId;
        await db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(employee, "1405/01/15", DecreeType.Hire, null, [new DecreeItemInput(wage, 6_000_000)]), default));

        // گروه: مسکن ۱۰ میلیون به‌جای پارامتر سال، بیمه سهم کارگر صفر؛ الگو: بدون بن، مزد پایه ۱۰٪ بیشتر
        var code = $"T{Guid.NewGuid():N}"[..10];
        var group = await db.Run<PayTemplateService, int>(x => x.CreateGroupAsync(new SaveGroupRequest(code, "گروه تست", 0, null, null, true, null,
            [new GroupParamDto("HOUSING", 10_000_000)]), default));
        var items = await db.Run<PayItemService, List<PayItemDto>>(x => x.GetAllAsync(false, default));
        int Id(string c) => items.Single(i => i.Code == c).Id;
        var rows = items.Where(i => i.Code != "FOOD_PAY")
            .Select(i => new TemplateItemDto(i.Id, i.Code == "BASE" ? "ROUND(DAILY_WAGE * DAYS * 1.1, 0)" : null)).ToList();
        var template = await db.Run<PayTemplateService, int>(x => x.CreateTemplateAsync(new SaveTemplateRequest(code, "الگوی تست", group, true, null, rows), default));
        Assert.Equal(1, await db.Run<PayTemplateService, int>(x => x.AssignAsync(new AssignTemplateRequest([employee], template, "1405/01/15"), default)));
        Assert.Equal(template, Assert.Single(await db.Run<PayTemplateService, List<EmployeeTemplateDto>>(x => x.HistoryAsync(employee, default))).TemplateId);

        var runId = await db.Run<PayRunService, int>(x => x.CreateAsync(new CreatePayRunRequest(1405, 2, workshop), default));
        await db.Run<PayRunService, CalcResultDto>(x => x.CalculateAsync(runId, default));
        var slip = await db.Run<PayRunService, PayslipDto>(x => x.PayslipAsync(runId, employee, default));
        Assert.Equal(198_000_000, slip.Earnings.Single(l => l.Code == "BASE").Amount);   // ۶ میلیون × ۳۰ × ۱٫۱
        Assert.Equal(10_000_000, slip.Earnings.Single(l => l.Code == "HOUSING_PAY").Amount);
        Assert.DoesNotContain(slip.Earnings, l => l.Code == "FOOD_PAY");
        Assert.DoesNotContain(slip.Deductions, l => l.Code == "INS_WORKER" && l.Amount != 0);

        // الگوی به‌کاررفته حذف نمی‌شود؛ گروه دارای الگو هم
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PayTemplateService>(x => x.DeleteTemplateAsync(template, default)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PayTemplateService>(x => x.DeleteGroupAsync(group, default)));
        // فرمول نامعتبر در الگو رد می‌شود
        var bad = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayTemplateService>(x => x.UpdateTemplateAsync(template,
            new SaveTemplateRequest(code, "الگوی تست", group, true, null, [new TemplateItemDto(Id("BASE"), "ROUND(DAILY_WAGE *")]), default)));
        Assert.Contains("پایه", bad.Message);
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
    public async Task Monthly_run_calculates_insurance_and_takes_tax_after_calculation()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        // کارگاه و کارمند جدا تا اجرای ماه فقط همین کارمند را داشته باشد
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(x => x.GetRoleTypesAsync(false, default));
        var nationalId = PersonTests.NewNationalCode();
        var person = await db.Run<PersonService, int>(x => x.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "کارمند", LastName = "اجرای ماه", NationalId = nationalId,
            RoleIds = [roles.Single(r => r.Code == RoleCodes.Employee).Id]
        }, default));
        var workshop = await db.Run<WorkshopService, int>(x => x.CreateAsync(new SaveWorkshopRequest("9900445566", "کارگاه اجرای ماه"), default));
        var employee = await db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(person, null, workshop, null,
            ContractType.Temporary, "1405/01/15", "1234567891", InsuranceStatus.Insured, 2, s.Center), default));
        var draft = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(employee, default));
        var wage = draft.Items.Single(i => i.Code == DecreeService.DailyWageCode).PayItemId;
        await db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(employee, "1405/01/15", DecreeType.Hire, null, [new DecreeItemInput(wage, 6_000_000)]), default));

        // اردیبهشت ۳۱ روز: کارکرد ۳۰ (همیشه ۳۰ روز)، روز بیمه ۳۱
        var runId = await db.Run<PayRunService, int>(x => x.CreateAsync(new CreatePayRunRequest(1405, 2, workshop), default));
        var run = await db.Run<PayRunService, PayRunDto>(x => x.GetAsync(runId, default));
        var row = Assert.Single(run.Rows);
        Assert.Equal(30, row.WorkDays);
        Assert.Equal(31, row.InsuranceDays);
        Assert.Equal(s.Center, Assert.Single(row.Centers).CostCenterId);

        var duplicate = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayRunService, int>(x =>
            x.CreateAsync(new CreatePayRunRequest(1405, 2, workshop), default)));
        Assert.Contains("قبلاً", duplicate.Message);

        var calc = await db.Run<PayRunService, CalcResultDto>(x => x.CalculateAsync(runId, default));
        Assert.Contains(calc.Warnings, w => w.Message.Contains("مالیات"));
        var sum = Assert.Single(await db.Run<PayRunService, List<RunSummaryRowDto>>(x => x.SummaryAsync(runId, default)));
        const decimal child = 5_541_850m * 3 * 2;          // حق اولاد دو فرزند (مشمول بیمه نیست)
        const decimal paid = 180_000_000m + 30_000_000 + 22_000_000;   // مزد ۳۰ روز + مسکن + بن
        const decimal insSubject = 6_000_000m * 31 + 30_000_000 + 22_000_000; // بیمه: مزد × ۳۱ روز بیمه + مزایا
        Assert.Equal(paid + child, sum.Earnings);
        Assert.Equal(insSubject, sum.InsuranceSubject);
        Assert.Equal(16_660_000, sum.InsuranceWorker);      // ۷٪ سهم کارگر (کسور)
        Assert.Equal(47_600_000 + 7_140_000, sum.EmployerShare); // ۲۰٪ کارفرما + ۳٪ بیکاری (هزینه)
        Assert.Equal(0, sum.Tax);

        // نتیجه سامانه مالیاتی با کد ملی؛ اجرا خودکار دوباره محاسبه می‌شود
        var tax = await db.Run<PayRunService, TaxImportResultDto>(x => x.SetTaxAsync(runId,
            [new TaxRowInput(nationalId, 5_000_000), new TaxRowInput("0000000000", 1)], default));
        Assert.Equal(1, tax.Matched);
        Assert.Equal(["0000000000"], tax.Unmatched);
        sum = Assert.Single(await db.Run<PayRunService, List<RunSummaryRowDto>>(x => x.SummaryAsync(runId, default)));
        Assert.Equal(5_000_000, sum.Tax);
        Assert.Equal(paid + child - 16_660_000 - 5_000_000, sum.Net);

        // فایل‌های قانونی
        var insurance = await db.Run<Erp.Payroll.Application.Files.PayrollFilesService, Erp.Payroll.Application.Files.PayrollFileDto>(x =>
            x.InsuranceListAsync(runId, default));
        var files = Unzip(insurance.Content);
        var wor = files["DSKWOR00.DBF"];
        Assert.Equal(400, BitConverter.ToUInt16(wor, 10));
        Assert.Equal(1, BitConverter.ToInt32(wor, 4));
        var record = System.Text.Encoding.Latin1.GetString(wor, BitConverter.ToUInt16(wor, 8), 400);
        Assert.Contains("   238000000", record);                 // DSW_MASH
        Assert.Contains("    16660000", record);                 // DSW_BIME
        Assert.Contains(nationalId, record);
        Assert.Equal(300, BitConverter.ToUInt16(files["DSKKAR00.DBF"], 10));
        Assert.Contains(insurance.Warnings, w => w.Contains("کد شغل"));

        var taxFile = await db.Run<Erp.Payroll.Application.Files.PayrollFilesService, Erp.Payroll.Application.Files.PayrollFileDto>(x =>
            x.TaxFilesAsync(runId, default));
        var taxFiles = Unzip(taxFile.Content);
        var wh = System.Text.Encoding.UTF8.GetString(taxFiles["WH140502.TXT"]).TrimEnd().Split(',');
        Assert.Equal(39, wh.Length);
        Assert.Equal(nationalId, wh[0]);
        Assert.Equal((paid + child).ToString("0"), wh[6]);           // ستون ۷: مستمر نقدی
        Assert.Equal("16660000", wh[32]);                          // ستون ۳۳: حق بیمه سهم کارگر
        Assert.Equal((paid + child - 16_660_000).ToString("0"), wh[34]); // ستون ۳۵: خالص پیش از مالیات
        Assert.Equal(23, System.Text.Encoding.UTF8.GetString(taxFiles["WP140502.TXT"]).TrimEnd().Split(',').Length);

        // تغییر کارکرد: اجرا نیازمند محاسبه، و با ۱۵ روز نصف می‌شود
        await db.Run<PayRunService>(x => x.SaveAttendanceAsync(runId, [new SaveAttendanceRequest(row.Id, 15, 15, 0, 0, 0, 0,
            [new CenterShareInput(s.Center, 100)], new Dictionary<int, decimal>())], default));
        Assert.Equal(PayRunStatus.Draft, (await db.Run<PayRunService, PayRunDto>(x => x.GetAsync(runId, default))).Status);
        await db.Run<PayRunService, CalcResultDto>(x => x.CalculateAsync(runId, default));
        var slip = await db.Run<PayRunService, PayslipDto>(x => x.PayslipAsync(runId, employee, default));
        Assert.Equal(90_000_000, slip.Earnings.Single(l => l.Code == "BASE").Amount);
        Assert.Equal(5_000_000, slip.Deductions.Single(l => l.Code == "TAX").Amount);

        var badSplit = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayRunService>(x => x.SaveAttendanceAsync(runId,
            [new SaveAttendanceRequest(row.Id, 15, 15, 0, 0, 0, 0, [new CenterShareInput(s.Center, 60)], new Dictionary<int, decimal>())], default)));
        Assert.Contains("۱۰۰", badSplit.Message);
    }

    [Fact]
    public async Task Loans_are_deducted_and_final_run_posts_balanced_voucher()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var years = await db.Run<Erp.Accounting.Application.FiscalYears.FiscalYearService, List<Erp.Accounting.Application.FiscalYears.FiscalYearDto>>(
            x => x.GetAllAsync(default));
        if (!years.Any(y => string.CompareOrdinal(y.StartDate, "1405/03/31") <= 0 && string.CompareOrdinal(y.EndDate, "1405/03/31") >= 0))
            await db.Run<Erp.Accounting.Application.FiscalYears.FiscalYearService, short>(x => x.CreateAsync(1405, 1, default));

        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(x => x.GetRoleTypesAsync(false, default));
        var person = await db.Run<PersonService, int>(x => x.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "کارمند", LastName = "وام و سند", NationalId = PersonTests.NewNationalCode(),
            RoleIds = [roles.Single(r => r.Code == RoleCodes.Employee).Id]
        }, default));
        var workshop = await db.Run<WorkshopService, int>(x => x.CreateAsync(new SaveWorkshopRequest("9900778899", "کارگاه وام و سند"), default));
        var employee = await db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(person, null, workshop, null,
            ContractType.Temporary, "1405/01/01", "1234567892", InsuranceStatus.Insured, 0, s.Center), default));
        var draft = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(employee, default));
        var wage = draft.Items.Single(i => i.Code == DecreeService.DailyWageCode).PayItemId;
        await db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(employee, "1405/01/01", DecreeType.Hire, null, [new DecreeItemInput(wage, 6_000_000)]), default));

        // وام ۱۰ م در ۴ قسط از خرداد؛ مساعده ۲ م یک‌جا در خرداد
        var loan = await db.Run<LoanService, int>(x => x.CreateAsync(
            new SaveLoanRequest(employee, LoanKind.Loan, "1405/02/20", 10_000_000, 4, null, 1405, 3), default));
        await db.Run<LoanService, int>(x => x.CreateAsync(
            new SaveLoanRequest(employee, LoanKind.Advance, "1405/03/10", 2_000_000, 1, null, 1405, 3), default));

        var runId = await db.Run<PayRunService, int>(x => x.CreateAsync(new CreatePayRunRequest(1405, 3, workshop), default));
        await db.Run<PayRunService, CalcResultDto>(x => x.CalculateAsync(runId, default));
        var slip = await db.Run<PayRunService, PayslipDto>(x => x.PayslipAsync(runId, employee, default));
        Assert.Equal(2_500_000, slip.Deductions.Single(l => l.Code == "LOAN").Amount);
        Assert.Equal(2_000_000, slip.Deductions.Single(l => l.Code == "ADVANCE").Amount);
        var loans = await db.Run<LoanService, List<LoanListItemDto>>(x => x.ListAsync(employee, false, default));
        Assert.Equal(7_500_000, loans.Single(l => l.Kind == LoanKind.Loan).Remaining);
        Assert.Equal(0, loans.Single(l => l.Kind == LoanKind.Advance).Remaining);

        var used = await Assert.ThrowsAsync<DomainException>(() => db.Run<LoanService>(x => x.DeleteAsync(loan, default)));
        Assert.Contains("قسط", used.Message);

        // قطعی: سند تراز، اجرا قفل
        var voucher = await db.Run<PayRunService, RunVoucherDto>(x => x.FinalizeAsync(runId, default));
        var v = await db.Run<Erp.Accounting.Application.Vouchers.VoucherService, Erp.Accounting.Application.Vouchers.VoucherDto>(x =>
            x.GetAsync(voucher.VoucherId, default));
        Assert.True(v.IsBalanced);
        Assert.Equal("pay_run", v.SourceDocType);
        Assert.Contains(v.Lines, l => l.AccountCode == "5125" && l.Credit == slip.Net);
        Assert.Contains(v.Lines, l => l.AccountCode == "2522" && l.Credit == 2_500_000);
        Assert.Contains(v.Lines, l => l.AccountCode == "2523" && l.Credit == 2_000_000);
        Assert.Contains(v.Lines, l => l.AccountCode.StartsWith("81") && l.Debit > 0);

        var row = (await db.Run<PayRunService, PayRunDto>(x => x.GetAsync(runId, default))).Rows.Single();
        var locked = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayRunService>(x => x.SaveAttendanceAsync(runId,
            [new SaveAttendanceRequest(row.Id, 10, 10, 0, 0, 0, 0, [new CenterShareInput(s.Center, 100)], new Dictionary<int, decimal>())], default)));
        Assert.Contains("قطعی", locked.Message);

        // پرداخت گروهی: بیش از خالص رد؛ پرداخت کامل با سند؛ اجرای پرداخت‌شده از قطعی برنمی‌گردد
        var bank = await db.Run<Erp.Treasury.Application.TreasurySetupService, int>(x => x.SaveBankAccountAsync(null,
            new Erp.Treasury.Application.SaveTreasuryBankAccountRequest("بانک حقوق تست", null, "مرکزی", "778899", null,
                Erp.Treasury.Domain.BankAccountKind.Current, true, null), default));
        var candidate = Assert.Single(await db.Run<SalaryPaymentService, List<PaymentCandidateDto>>(x => x.CandidatesAsync(runId, default)));
        Assert.Equal(slip.Net, candidate.Remaining);
        var tooMuch = await Assert.ThrowsAsync<DomainException>(() => db.Run<SalaryPaymentService, int>(x => x.CreateAsync(
            new SaveSalaryPaymentRequest(runId, "1405/04/05", bank, null, [new SalaryPaymentLineInput(employee, slip.Net + 1)]), default)));
        Assert.Contains("بیشتر", tooMuch.Message);
        var paymentId = await db.Run<SalaryPaymentService, int>(x => x.CreateAsync(
            new SaveSalaryPaymentRequest(runId, "1405/04/05", bank, null, [new SalaryPaymentLineInput(employee, slip.Net)]), default));
        var payment = Assert.Single(await db.Run<SalaryPaymentService, List<SalaryPaymentListItemDto>>(x => x.ListAsync(runId, default)));
        Assert.Equal(slip.Net, payment.Total);
        var pv = await db.Run<Erp.Accounting.Application.Vouchers.VoucherService, Erp.Accounting.Application.Vouchers.VoucherDto>(x =>
            x.GetAsync(payment.VoucherId!.Value, default));
        Assert.True(pv.IsBalanced);
        Assert.Contains(pv.Lines, l => l.AccountCode == "5125" && l.Debit == slip.Net);
        Assert.Contains(pv.Lines, l => l.AccountCode == "2811" && l.Credit == slip.Net);
        Assert.Single(await db.Run<SalaryPaymentService, List<BankListRowDto>>(x => x.BankListAsync(paymentId, default)));
        var paid = await Assert.ThrowsAsync<DomainException>(() => db.Run<PayRunService>(x => x.ReopenAsync(runId, default)));
        Assert.Contains("پرداخت", paid.Message);
        await db.Run<SalaryPaymentService>(x => x.DeleteAsync(paymentId, default));

        // برگشت از قطعی: سند حذف می‌شود
        await db.Run<PayRunService>(x => x.ReopenAsync(runId, default));
        Assert.Null(await db.Run<PayRunService, RunVoucherDto?>(x => x.VoucherAsync(runId, default)));
    }

    [Fact]
    public async Task Settlement_pays_bonus_severance_leave_and_clears_loans_from_provisions()
    {
        if (!db.Enabled) return;
        var s = await SeedAsync();
        var years = await db.Run<Erp.Accounting.Application.FiscalYears.FiscalYearService, List<Erp.Accounting.Application.FiscalYears.FiscalYearDto>>(
            x => x.GetAllAsync(default));
        if (!years.Any(y => string.CompareOrdinal(y.StartDate, "1405/03/31") <= 0 && string.CompareOrdinal(y.EndDate, "1405/03/31") >= 0))
            await db.Run<Erp.Accounting.Application.FiscalYears.FiscalYearService, short>(x => x.CreateAsync(1405, 1, default));

        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(x => x.GetRoleTypesAsync(false, default));
        var person = await db.Run<PersonService, int>(x => x.CreateAsync(new SavePersonRequest
        {
            Kind = PersonKind.Natural, FirstName = "کارمند", LastName = "تسویه", NationalId = PersonTests.NewNationalCode(),
            RoleIds = [roles.Single(r => r.Code == RoleCodes.Employee).Id]
        }, default));
        var workshop = await db.Run<WorkshopService, int>(x => x.CreateAsync(new SaveWorkshopRequest("9900665544", "کارگاه تسویه"), default));
        var employee = await db.Run<EmployeeService, int>(x => x.CreateAsync(new SaveEmployeeRequest(person, null, workshop, null,
            ContractType.Temporary, "1405/01/01", "1234567893", InsuranceStatus.Insured, 0, s.Center), default));
        var draft = await db.Run<DecreeService, DecreeDto>(x => x.DraftAsync(employee, default));
        var wage = draft.Items.Single(i => i.Code == DecreeService.DailyWageCode).PayItemId;
        await db.Run<DecreeService, int>(x => x.CreateAsync(
            new SaveDecreeRequest(employee, "1405/01/01", DecreeType.Hire, null, [new DecreeItemInput(wage, 6_000_000)]), default));
        await db.Run<LoanService, int>(x => x.CreateAsync(
            new SaveLoanRequest(employee, LoanKind.Loan, "1405/01/01", 10_000_000, 10, null, 1405, 1), default));

        // فروردین و اردیبهشت قطعی: ذخیره عیدی ۳۰ م و سنوات ۱۵ م در هر ماه
        foreach (byte month in new byte[] { 1, 2 })
        {
            var id = await db.Run<PayRunService, int>(x => x.CreateAsync(new CreatePayRunRequest(1405, month, workshop), default));
            await db.Run<PayRunService, CalcResultDto>(x => x.CalculateAsync(id, default));
            await db.Run<PayRunService, RunVoucherDto>(x => x.FinalizeAsync(id, default));
        }

        // ترک کار ۱۴۰۵/۰۳/۱۵ و تسویه
        await db.Run<EmployeeService>(x => x.LeaveAsync(employee, "1405/03/15", "استعفا", default));
        var st = await db.Run<PayRunService, SettlementDto>(x => x.SettleAsync(employee, default));
        Assert.True(st.IsSettlement);
        Assert.Equal(75, st.YearWorkDays);                       // ۳۰ + ۳۰ + ۱۵
        Assert.Equal(77, st.ServiceDays);                        // ۳۱ + ۳۱ + ۱۵ روز تقویمی
        Assert.Equal(5.42m, st.LeaveBalance);                    // ۲۶ × ۷۵ ÷ ۳۶۰
        Assert.Equal(75_000_000, st.Bonus);                      // ۳۶۰ م × ۷۵ ÷ ۳۶۰
        Assert.Equal(37_972_603, st.Severance);                  // ۶ م × ۳۰ × ۷۷ ÷ ۳۶۵
        Assert.Equal(32_520_000, st.LeaveBuyout);                // ۶ م × ۵٫۴۲
        Assert.Equal(8_000_000, st.LoansSettled);                // مانده وام یک‌جا

        // سند: ذخیره عیدی و سنوات آزاد می‌شود (بدهکار 5126 و 4201)، اختلاف سنوات هزینه
        var voucher = await db.Run<PayRunService, RunVoucherDto>(x => x.FinalizeAsync(st.RunId, default));
        var v = await db.Run<Erp.Accounting.Application.Vouchers.VoucherService, Erp.Accounting.Application.Vouchers.VoucherDto>(x =>
            x.GetAsync(voucher.VoucherId, default));
        Assert.True(v.IsBalanced);
        Assert.Contains(v.Lines, l => l.AccountCode == "5126" && l.Debit == 75_000_000);   // ۳۰ + ۳۰ + ۱۵ م
        Assert.Contains(v.Lines, l => l.AccountCode == "4201" && l.Debit == 37_500_000);   // ۱۵ + ۱۵ + ۷٫۵ م
        Assert.Contains(v.Lines, l => l.AccountCode == "2522" && l.Credit == 8_000_000);

        // گزارش‌ها روی همین داده
        var reports = (Func<Erp.Payroll.Application.Reports.PayrollReportService, Task<Erp.SharedKernel.Reports.ReportTable>> q) =>
            db.Run<Erp.Payroll.Application.Reports.PayrollReportService, Erp.SharedKernel.Reports.ReportTable>(q);
        var f = new Erp.Payroll.Application.Reports.PayrollReportFilter(1405, 3, workshop);

        var register = await reports(x => x.RegisterAsync(f, default));
        var row = Assert.Single(register.Rows);
        Assert.Contains(register.Columns, c => c.Title == "عیدی و پاداش" && (decimal)row[c.Key]! == 75_000_000);
        Assert.Equal(st.Net, (decimal)row["net"]!);

        var attendance = await reports(x => x.AttendanceAsync(f, default));
        Assert.Equal("تسویه حساب", Assert.Single(attendance.Rows)["note"]);

        var kardex = await reports(x => x.LeaveKardexAsync(f with { Month = null, EmployeeId = employee }, default));
        Assert.Equal(5.42m, kardex.Rows.Sum(r => r["bought"] is decimal d ? d : 0));
        Assert.Equal(0m, kardex.Rows[^1]["balance"]);

        var history = await reports(x => x.EmployeeHistoryAsync(f with { Month = null, EmployeeId = employee }, default));
        Assert.Equal(3, history.Rows.Count);
        Assert.Equal(10_000_000m, history.Total("loan"));

        var summary = await reports(x => x.YearSummaryAsync(f with { Month = null }, default));
        Assert.Contains(summary.Rows, r => (string?)r["item"] == "بازخرید سنوات" && (decimal)r["m3"]! == 37_972_603);

        var cost = await reports(x => x.CostByCenterAsync(f with { Month = 1, MonthTo = 3 }, default));
        Assert.Equal(75m, Assert.Single(cost.Rows)["days"]);

        var provisions = await reports(x => x.ProvisionBalancesAsync(f, default));
        Assert.Equal(0m, Assert.Single(provisions.Rows)["total"]);   // تسویه کل ذخیره را آزاد کرده است
    }

    private static Dictionary<string, byte[]> Unzip(byte[] zip)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(e => e.Name, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
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
