using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Application.Persons;
using Erp.Accounting.Domain.BaseData;
using Erp.Accounting.Domain.Details;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>اشخاص، نقش‌ها و تفصیلی مشترک روی دیتابیس واقعی.</summary>
[Collection(SqlCollection.Name)]
public class PersonTests(SqlFixture db)
{
    private Task<List<PersonRoleTypeDto>> Roles() => db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(true, default));

    private async Task<short> Role(string code) => (await Roles()).Single(r => r.Code == code).Id;

    private Task<List<DetailTypeDto>> Types() => db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default));

    private Task<int> Create(SavePersonRequest r) => db.Run<PersonService, int>(s => s.CreateAsync(r, default));

    private Task<PersonDetailsDto> Get(int id) => db.Run<PersonService, PersonDetailsDto>(s => s.GetAsync(id, default));

    private static int _seq;

    /// <summary>کد ملی معتبر و یکتا برای هر تست.</summary>
    private static string NewNationalCode()
    {
        var body = (100000000 + Interlocked.Increment(ref _seq) * 7919 % 800000000).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (body[i] - '0') * (10 - i);
        var r = sum % 11;
        return body + (r < 2 ? r : 11 - r);
    }

    [Fact]
    public async Task Customer_and_supplier_share_one_detail_and_employee_gets_its_own()
    {
        if (!db.Enabled) return;
        var customer = await Role(RoleCodes.Customer);
        var supplier = await Role(RoleCodes.Supplier);
        var employee = await Role(RoleCodes.Employee);

        var id = await Create(new SavePersonRequest
        {
            FirstName = "علي", LastName = "رضايي", NationalId = NewNationalCode(), Mobile = "۰۹۱۲۱۲۳۴۵۶۷",
            RoleIds = [customer, supplier],
            Addresses = [new AddressDto { Address = "تهران، خیابان آزادی", PostalCode = "1981735861" }],
            BankAccounts = [new BankAccountDto { Iban = "IR110170000000123456789001" }]
        });

        var p = await Get(id);
        Assert.Equal("علی رضایی", p.DisplayName);       // ي/ك عربی یکسان شد
        Assert.Equal("09121234567", p.Mobile);
        var shared = Assert.Single(p.Details);
        var types = await Types();
        Assert.Equal(OwnerModules.Persons, types.Single(t => t.Id == shared.DetailTypeId).OwnerModule);
        Assert.True(p.Addresses[0].IsDefault);           // تنها نشانی پیش‌فرض می‌شود
        Assert.Equal("ملی ایران", p.BankAccounts[0].BankName); // بانک از شبا

        // افزودن نقش کارمند: تفصیلی جدا در گروه کارکنان + اطلاعات پرسنلی
        p.RoleIds.Add(employee);
        p.LastName = "رضایی‌نژاد";
        p.Employee = new EmployeeDto { EmployeeNo = $"E{id}", HireDate = "1404/01/15", InsuranceNo = "1234567890", ChildrenCount = 2 };
        await db.Run<PersonService>(s => s.UpdateAsync(id, p, default));

        p = await Get(id);
        Assert.Equal(2, p.Details.Count);
        Assert.Contains(p.Details, d => types.Single(t => t.Id == d.DetailTypeId).OwnerModule == OwnerModules.Payroll);
        Assert.All(p.Details, d => Assert.Equal("علی رضایی‌نژاد", d.Title)); // تفصیلی‌ها هم‌نام شدند
        Assert.Equal(shared.Code, p.Details.Single(d => d.Id == shared.Id).Code); // کد تفصیلی مشترک عوض نشد
        Assert.Equal("1404/01/15", p.Employee!.HireDate);
        Assert.Equal(2, p.Employee.ChildrenCount);
    }

    [Fact]
    public async Task Legal_person_gets_legal_group_detail()
    {
        if (!db.Enabled) return;
        var id = await Create(new SavePersonRequest
        {
            Kind = PersonKind.Legal, LegalName = "شرکت نمونه", NationalId = "10862264127", EconomicCode = "10862264127",
            LegalType = LegalType.PrivateJointStock, RoleIds = [await Role(RoleCodes.Supplier)]
        });

        var p = await Get(id);
        var types = await Types();
        var type = types.Single(t => t.Id == Assert.Single(p.Details).DetailTypeId);
        Assert.Equal(PersonKind.Legal, type.PersonKind);
        Assert.Equal(TaxPayerType.Legal, p.TaxPayerType);
    }

    [Theory]
    [InlineData("0010532128", "رقم کنترل")]
    [InlineData("12345", "۱۰ رقم")]
    public async Task Invalid_national_code_is_rejected_before_save(string code, string message)
    {
        if (!db.Enabled) return;
        var role = await Role(RoleCodes.Customer);
        var ex = await Assert.ThrowsAsync<DomainException>(() => Create(new SavePersonRequest
        {
            FirstName = "الف", LastName = "ب", NationalId = code, RoleIds = [role]
        }));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task Duplicate_national_code_and_bad_iban_are_rejected()
    {
        if (!db.Enabled) return;
        var role = await Role(RoleCodes.Customer);
        var code = NewNationalCode();
        await Create(new SavePersonRequest { FirstName = "یک", LastName = "نفر", NationalId = code, RoleIds = [role] });

        var dup = await Assert.ThrowsAsync<DomainException>(() =>
            Create(new SavePersonRequest { FirstName = "دو", LastName = "نفر", NationalId = code, RoleIds = [role] }));
        Assert.Contains(code, dup.Message);

        var iban = await Assert.ThrowsAsync<DomainException>(() => Create(new SavePersonRequest
        {
            FirstName = "سه", LastName = "نفر", RoleIds = [role],
            BankAccounts = [new BankAccountDto { Iban = "IR120170000000123456789001" }]
        }));
        Assert.Contains("حساب بانکی 1", iban.Message);
    }

    [Fact]
    public async Task Employee_needs_national_code_and_must_be_natural()
    {
        if (!db.Enabled) return;
        var employee = await Role(RoleCodes.Employee);
        var noCode = await Assert.ThrowsAsync<DomainException>(() =>
            Create(new SavePersonRequest { FirstName = "بی", LastName = "کد", RoleIds = [employee] }));
        Assert.Contains("کد ملی", noCode.Message);

        var legal = await Assert.ThrowsAsync<DomainException>(() =>
            Create(new SavePersonRequest { Kind = PersonKind.Legal, LegalName = "شرکت", RoleIds = [employee] }));
        Assert.Contains("حقیقی", legal.Message);
    }

    [Fact]
    public async Task Role_filter_narrows_person_details_but_not_other_details()
    {
        if (!db.Enabled) return;
        var customer = await Role(RoleCodes.Customer);
        var supplier = await Role(RoleCodes.Supplier);
        var onlySupplier = await Create(new SavePersonRequest { FirstName = "فقط", LastName = "تأمین‌کننده", RoleIds = [supplier] });
        var onlyCustomer = await Create(new SavePersonRequest { FirstName = "فقط", LastName = "مشتری", RoleIds = [customer] });

        var types = await Types();
        var allowed = types.Where(t => t.OwnerModule is OwnerModules.Persons or OwnerModules.Treasury).Select(t => t.Id).ToList();
        var cashType = types.First(t => t.OwnerModule == OwnerModules.Treasury).Id;
        await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(cashType, "صندوق فیلتر", null), default));

        var filtered = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(allowed, [customer], "فقط", default));
        Assert.Contains(filtered, d => d.PersonId == onlyCustomer);
        Assert.DoesNotContain(filtered, d => d.PersonId == onlySupplier);

        var cash = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(allowed, [customer], "صندوق فیلتر", default));
        Assert.Single(cash); // تفصیلی غیرشخصی از فیلتر نقش اثر نمی‌گیرد

        var all = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(allowed, null, "فقط", default));
        Assert.Contains(all, d => d.PersonId == onlySupplier);

        var people = await db.Run<PersonService, List<PersonListItemDto>>(s => s.SearchAsync("فقط", customer, false, default));
        Assert.Equal(onlyCustomer, Assert.Single(people).Id);
        Assert.Contains("مشتری", people[0].Roles);
    }

    [Fact]
    public async Task Detail_type_range_rules()
    {
        if (!db.Enabled) return;
        // گروه جدید با طول کد ۴
        var id = await db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, "1001", "1099", null), default));
        await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(id, "واحد ۱", null), default));
        await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(id, "واحد ۲", null), default));

        var t = (await Types()).Single(x => x.Id == id);
        Assert.Equal((2, "1003", 4, 99L), (t.UsedCount, t.NextCode, t.CodeLength, t.Capacity));

        // بزرگ کردن بازه مجاز است
        await db.Run<DetailService>(s => s.UpdateTypeAsync(id, new UpdateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, null, "1001", "1199", true), default));

        // کوچک کردن تا جایی که کد موجود بیرون بماند، یا تغییر طول کد، مجاز نیست
        await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService>(s =>
            s.UpdateTypeAsync(id, new UpdateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, null, "1002", "1199", true), default)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService>(s =>
            s.UpdateTypeAsync(id, new UpdateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, null, "10001", "10999", true), default)));

        // هم‌پوشانی با گروه دیگر
        var overlap = await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("هم‌پوشان", OwnerModules.Manual, "1150", "1300", null), default)));
        Assert.Contains("واحدهای تجاری", overlap.Message);

        // دومین گروه فعال اشخاص حقیقی مجاز نیست
        var twin = await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("اشخاص حقیقی ۲", OwnerModules.Persons, "50001", "50999", PersonKind.Natural), default)));
        Assert.Contains("فقط یک گروه فعال", twin.Message);
    }

    [Fact]
    public async Task Account_level_keeps_role_filter()
    {
        if (!db.Enabled) return;
        var supplier = await Role(RoleCodes.Supplier);
        var types = await Types();
        var legal = types.Single(t => t.OwnerModule == OwnerModules.Persons && t.PersonKind == PersonKind.Legal).Id;
        var natural = types.Single(t => t.OwnerModule == OwnerModules.Persons && t.PersonKind == PersonKind.Natural).Id;

        var account = await db.Run<AccountService, AccountDto>(s => s.GetByCodeAsync("2402", default));
        await db.Run<AccountService>(s => s.SetDetailLevelAsync(account.Id, 1,
            new SetDetailLevelRequest(false, [natural, legal], [supplier]), default));

        var level = Assert.Single((await db.Run<AccountService, AccountDto>(s => s.GetAsync(account.Id, default))).DetailLevels);
        Assert.Equal([supplier], level.RoleIds);
        Assert.Equal(2, level.DetailTypeIds.Count);
    }
}
