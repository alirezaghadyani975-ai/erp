using Erp.Accounting.Application.ChartOfAccounts;
using Erp.Accounting.Application.Details;
using Erp.Accounting.Domain.Details;
using Erp.BaseData.Application.Banks;
using Erp.BaseData.Application.Lookups;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Lookups;
using Erp.BaseData.Domain.Persons;
using Erp.SharedKernel.Domain;

namespace Erp.Accounting.Tests.Sql;

/// <summary>اشخاص (ماژول اطلاعات پایه)، نقش‌ها، تفصیلی مشترک (حسابداری) و تراکنش مشترک دو ماژول.</summary>
[Collection(SqlCollection.Name)]
public class PersonTests(SqlFixture db)
{
    private Task<List<PersonRoleTypeDto>> Roles() => db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(true, default));

    private async Task<short> Role(string code) => (await Roles()).Single(r => r.Code == code).Id;

    private Task<List<DetailTypeDto>> Types() => db.Run<DetailService, List<DetailTypeDto>>(s => s.GetTypesAsync(default));

    private Task<int> Create(SavePersonRequest r) => db.Run<PersonService, int>(s => s.CreateAsync(r, default));

    private Task<PersonDetailsDto> Get(int id) => db.Run<PersonService, PersonDetailsDto>(s => s.GetAsync(id, default));

    private Task<List<LookupItemDto>> Items(string category, int? parent = null) =>
        db.Run<LookupService, List<LookupItemDto>>(s => s.GetItemsAsync(category, parent, false, default));

    private static int _seq;

    /// <summary>کد ملی معتبر و یکتا برای هر تست.</summary>
    internal static string NewNationalCode()
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
        var tehran = (await Items(LookupCodes.Province)).Single(p => p.Title == "تهران");
        var tehranCity = (await Items(LookupCodes.City, tehran.Id)).Single(c => c.Title == "تهران");
        var factory = (await Items(LookupCodes.AddressType)).Single(t => t.Title == "کارخانه");

        var id = await Create(new SavePersonRequest
        {
            FirstName = "علي", LastName = "رضايي", NationalId = NewNationalCode(), Mobile = "۰۹۱۲۱۲۳۴۵۶۷",
            BirthCityId = tehranCity.Id, RoleIds = [customer, supplier],
            Addresses = [new AddressDto { AddressTypeId = factory.Id, ProvinceId = tehran.Id, CityId = tehranCity.Id,
                Address = "خیابان آزادی", PostalCode = "1981735861" }],
            BankAccounts = [new BankAccountDto { Iban = "IR110170000000123456789001" }]
        });

        var p = await Get(id);
        Assert.Equal("علی رضایی", p.DisplayName);       // ي/ك عربی یکسان شد
        Assert.Equal("09121234567", p.Mobile);
        Assert.Equal(tehranCity.Id, p.BirthCityId);
        var shared = Assert.Single(p.Links);
        Assert.Equal("party", shared.Kind);
        Assert.True(p.Addresses[0].IsDefault);           // تنها نشانی پیش‌فرض می‌شود
        var banks = await db.Run<BankService, List<BankDto>>(s => s.GetAllAsync(false, default));
        Assert.Equal(banks.Single(b => b.IbanCode == "017").Id, p.BankAccounts[0].BankId); // بانک از کد شبا

        // افزودن نقش کارمند: تفصیلی جدا در گروه کارکنان؛ تغییر نام به تفصیلی‌ها می‌رسد
        p.RoleIds.Add(employee);
        p.LastName = "رضایی‌نژاد";
        await db.Run<PersonService>(s => s.UpdateAsync(id, p, default));

        p = await Get(id);
        Assert.Equal(2, p.Links.Count);
        Assert.Contains(p.Links, l => l.Kind == "employee");
        Assert.All(p.Links, l => Assert.Equal("علی رضایی‌نژاد", l.Title));
        Assert.Equal(shared.Code, p.Links.Single(l => l.Kind == "party").Code); // کد تفصیلی مشترک عوض نشد
    }

    [Fact]
    public async Task Legal_person_with_contacts_gets_legal_group_detail()
    {
        if (!db.Enabled) return;
        var ceo = (await Items(LookupCodes.ContactRole)).Single(r => r.Title == "مدیرعامل");
        var id = await Create(new SavePersonRequest
        {
            Kind = PersonKind.Legal, LegalName = "شرکت نمونه", NationalId = "10862264127", EconomicCode = "10862264127",
            LegalType = LegalType.PrivateJointStock, RoleIds = [await Role(RoleCodes.Supplier)],
            Contacts = [new ContactDto { FullName = "مدیر شرکت", ContactRoleId = ceo.Id, Mobile = "09121112233" }]
        });

        var p = await Get(id);
        var types = await Types();
        Assert.Equal(PersonKind.Legal, types.Single(t => t.Title == Assert.Single(p.Links).GroupTitle).PersonKind);
        Assert.Equal(TaxPayerType.Legal, p.TaxPayerType);
        Assert.Equal(ceo.Id, Assert.Single(p.Contacts).ContactRoleId);
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
    public async Task Lookup_from_wrong_list_is_rejected()
    {
        if (!db.Enabled) return;
        var customer = await Role(RoleCodes.Customer);
        var province = (await Items(LookupCodes.Province)).First();
        var ex = await Assert.ThrowsAsync<DomainException>(() => Create(new SavePersonRequest
        {
            FirstName = "محل", LastName = "اشتباه", BirthCityId = province.Id, RoleIds = [customer]
        }));
        Assert.Contains("محل تولد", ex.Message);

        var isfahan = (await Items(LookupCodes.Province)).Single(p => p.Title == "اصفهان");
        var tabriz = (await Items(LookupCodes.City)).Single(c => c.Title == "تبریز");
        var mismatch = await Assert.ThrowsAsync<DomainException>(() => Create(new SavePersonRequest
        {
            FirstName = "شهر", LastName = "ناجور", RoleIds = [customer],
            Addresses = [new AddressDto { ProvinceId = isfahan.Id, CityId = tabriz.Id, Address = "x" }]
        }));
        Assert.Contains("در استان", mismatch.Message);
    }

    [Fact]
    public async Task Duplicates_and_bad_iban()
    {
        if (!db.Enabled) return;
        var role = await Role(RoleCodes.Customer);
        var code = NewNationalCode();
        var first = await Create(new SavePersonRequest { FirstName = "یک", LastName = "تکراری", NationalId = code, Mobile = "09350001122", RoleIds = [role] });

        var dupRequest = new SavePersonRequest { FirstName = "یک", LastName = "تکراری", Mobile = "09350001122", RoleIds = [role] };
        var dups = await db.Run<PersonService, List<DuplicateDto>>(s => s.FindDuplicatesAsync(dupRequest, null, default));
        Assert.Equal(first, Assert.Single(dups).Id);
        Assert.Empty(await db.Run<PersonService, List<DuplicateDto>>(s => s.FindDuplicatesAsync(dupRequest, first, default)));

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
    public async Task Person_and_detail_are_one_transaction()
    {
        if (!db.Enabled) return;
        var types = await Types();
        var legalGroup = types.Single(t => t.OwnerModule == OwnerModules.Persons && t.PersonKind == PersonKind.Legal && t.IsActive);
        await db.Run<DetailService>(s => s.UpdateTypeAsync(legalGroup.Id, new UpdateDetailTypeRequest(legalGroup.Title,
            legalGroup.OwnerModule, legalGroup.PersonKind, legalGroup.CodeFrom, legalGroup.CodeTo, false), default));
        try
        {
            var customer = await Role(RoleCodes.Customer);
            // گروه اشخاص حقوقی غیرفعال است، پس ساخت تفصیلی در حسابداری خطا می‌دهد و شخص هم نباید ثبت شود
            var ex = await Assert.ThrowsAsync<DomainException>(() => Create(new SavePersonRequest
            {
                Kind = PersonKind.Legal, LegalName = "شرکت بی‌تفصیلی", RoleIds = [customer]
            }));
            Assert.Contains("گروه تفصیلی اشخاص حقوقی", ex.Message);
            var found = await db.Run<PersonService, List<PersonListItemDto>>(s => s.SearchAsync("بی‌تفصیلی", null, null, true, default));
            Assert.Empty(found);
        }
        finally
        {
            await db.Run<DetailService>(s => s.UpdateTypeAsync(legalGroup.Id, new UpdateDetailTypeRequest(legalGroup.Title,
                legalGroup.OwnerModule, legalGroup.PersonKind, legalGroup.CodeFrom, legalGroup.CodeTo, true), default));
        }
    }

    [Fact]
    public async Task Delete_person_without_turnover_removes_details_too()
    {
        if (!db.Enabled) return;
        var id = await Create(new SavePersonRequest { FirstName = "حذف", LastName = "شدنی", RoleIds = [await Role(RoleCodes.Customer)] });
        var code = (await Get(id)).Links.Single().Code;
        await db.Run<PersonService, int>(s => s.AddAttachmentAsync(id, "کارت ملی", "card.pdf", "application/pdf", [1, 2, 3], default));

        await db.Run<PersonService>(s => s.DeleteAsync(id, default));

        await Assert.ThrowsAsync<NotFoundException>(() => Get(id));
        var details = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync((short?)null, code, default));
        Assert.DoesNotContain(details, d => d.Code == code);
    }

    [Fact]
    public async Task Role_filter_narrows_person_details_but_not_other_details()
    {
        if (!db.Enabled) return;
        var customer = await Role(RoleCodes.Customer);
        var supplier = await Role(RoleCodes.Supplier);
        var onlySupplier = await Create(new SavePersonRequest { FirstName = "فقط", LastName = "تأمین‌کننده", RoleIds = [supplier] });
        var onlyCustomer = await Create(new SavePersonRequest { FirstName = "فقط", LastName = "مشتری", RoleIds = [customer] });
        var supplierDetail = (await Get(onlySupplier)).Links.Single().Code;
        var customerDetail = (await Get(onlyCustomer)).Links.Single().Code;

        var types = await Types();
        var allowed = types.Where(t => t.OwnerModule is OwnerModules.Persons or OwnerModules.Treasury).Select(t => t.Id).ToList();
        var cashType = types.First(t => t.OwnerModule == OwnerModules.Treasury).Id;
        await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(cashType, "صندوق فیلتر", null), default));

        var filtered = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(allowed, [customer], "فقط", default));
        Assert.Contains(filtered, d => d.Code == customerDetail);
        Assert.DoesNotContain(filtered, d => d.Code == supplierDetail);

        var cash = await db.Run<DetailService, List<DetailAccountDto>>(s => s.SearchAsync(allowed, [customer], "صندوق فیلتر", default));
        Assert.Single(cash); // تفصیلی غیرشخصی از فیلتر نقش اثر نمی‌گیرد

        var people = await db.Run<PersonService, List<PersonListItemDto>>(s => s.SearchAsync("فقط", customer, null, false, default));
        Assert.Equal(onlyCustomer, Assert.Single(people).Id);
        Assert.Equal(customerDetail, people[0].DetailCode);

        // جستجوی شخص با کد تفصیلی
        var byCode = await db.Run<PersonService, List<PersonListItemDto>>(s => s.SearchAsync(supplierDetail, null, null, false, default));
        Assert.Contains(byCode, x => x.Id == onlySupplier);
    }

    [Fact]
    public async Task Detail_type_range_rules_and_delete()
    {
        if (!db.Enabled) return;
        var id = await db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, "1001", "1099", null), default));
        var unit1 = await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(id, "واحد ۱", null), default));
        await db.Run<DetailService, int>(s => s.CreateAccountAsync(new CreateDetailAccountRequest(id, "واحد ۲", null), default));

        var t = (await Types()).Single(x => x.Id == id);
        Assert.Equal((2, "1003", 4, 99L), (t.UsedCount, t.NextCode, t.CodeLength, t.Capacity));

        await db.Run<DetailService>(s => s.UpdateTypeAsync(id, new UpdateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, null, "1001", "1199", true), default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService>(s =>
            s.UpdateTypeAsync(id, new UpdateDetailTypeRequest("واحدهای تجاری", OwnerModules.Manual, null, "1002", "1199", true), default)));
        var overlap = await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("هم‌پوشان", OwnerModules.Manual, "1150", "1300", null), default)));
        Assert.Contains("واحدهای تجاری", overlap.Message);
        var twin = await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService, short>(s => s.CreateTypeAsync(
            new CreateDetailTypeRequest("اشخاص حقیقی ۲", OwnerModules.Persons, "50001", "50999", PersonKind.Natural), default)));
        Assert.Contains("فقط یک گروه فعال", twin.Message);

        // حذف: گروه با تفصیلی حذف نمی‌شود؛ تفصیلی بی‌گردش حذف می‌شود
        await Assert.ThrowsAsync<DomainException>(() => db.Run<DetailService>(s => s.DeleteTypeAsync(id, default)));
        await db.Run<DetailService>(s => s.UpdateAccountAsync(unit1, "واحد یک", false, default));
        var rows = await db.Run<DetailService, List<DetailAccountRowDto>>(s => s.ListAccountsAsync(id, null, default));
        Assert.Contains(rows, r => r.Title == "واحد یک" && !r.IsActive);
        foreach (var r in rows) await db.Run<DetailService>(s => s.DeleteAccountAsync(r.Id, default));
        await db.Run<DetailService>(s => s.DeleteTypeAsync(id, default));
        Assert.DoesNotContain(await Types(), x => x.Id == id);
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
