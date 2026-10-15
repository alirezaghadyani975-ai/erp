using Erp.BaseData.Application.Banks;
using Erp.BaseData.Application.Lookups;
using Erp.BaseData.Application.Persons;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Lookups;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Tests.Sql;

/// <summary>جدول‌های پایه (کشور، استان، شهر، ...)، بانک‌ها و نقش‌ها.</summary>
[Collection(SqlCollection.Name)]
public class BaseDataTests(SqlFixture db)
{
    private Task<List<LookupCategoryDto>> Categories() => db.Run<LookupService, List<LookupCategoryDto>>(s => s.GetCategoriesAsync(default));

    private Task<List<LookupItemDto>> Items(string category, int? parent = null) =>
        db.Run<LookupService, List<LookupItemDto>>(s => s.GetItemsAsync(category, parent, true, default));

    [Fact]
    public async Task Seeded_lists_exist()
    {
        if (!db.Enabled) return;
        var cats = await Categories();
        Assert.Contains(cats, c => c.Code == LookupCodes.City && c.ParentCategoryId == cats.Single(p => p.Code == LookupCodes.Province).Id);
        Assert.True(cats.Single(c => c.Code == LookupCodes.PersonGroup).IsTree);

        Assert.Equal(31, (await Items(LookupCodes.Province)).Count);
        var fars = (await Items(LookupCodes.Province)).Single(p => p.Title == "فارس");
        Assert.Contains(await Items(LookupCodes.City, fars.Id), c => c.Title == "شیراز");
        Assert.Contains(await Items(LookupCodes.Country), c => c.Title == "ایران");

        var banks = await db.Run<BankService, List<BankDto>>(s => s.GetAllAsync(false, default));
        Assert.True(banks.Count >= 30);
        Assert.Contains(banks, b => b.IbanCode == "017" && b.CardPrefixes!.Contains("603799"));
    }

    [Fact]
    public async Task City_parent_must_be_a_province_and_tree_rules_hold()
    {
        if (!db.Enabled) return;
        var cats = await Categories();
        var city = cats.Single(c => c.Code == LookupCodes.City).Id;
        var group = cats.Single(c => c.Code == LookupCodes.PersonGroup).Id;
        var country = (await Items(LookupCodes.Country)).First();

        var wrongParent = await Assert.ThrowsAsync<DomainException>(() => db.Run<LookupService, int>(s =>
            s.CreateItemAsync(new SaveLookupItemRequest(city, "شهر بی‌استان", null, country.Id, 0, true), default)));
        Assert.Contains("فهرست درستی نیست", wrongParent.Message);

        // شهر بدون استان (مثلاً شهر خارجی) مجاز است
        await db.Run<LookupService, int>(s => s.CreateItemAsync(new SaveLookupItemRequest(city, "استانبول", null, null, 0, true), default));

        // درخت گروه اشخاص: والد و فرزند؛ والد با فرزند حذف نمی‌شود؛ حلقه ممنوع
        var root = await db.Run<LookupService, int>(s => s.CreateItemAsync(new SaveLookupItemRequest(group, "مشتریان عمده", null, null, 0, true), default));
        var child = await db.Run<LookupService, int>(s => s.CreateItemAsync(new SaveLookupItemRequest(group, "پخش تهران", null, root, 0, true), default));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<LookupService>(s => s.DeleteItemAsync(root, default)));
        await Assert.ThrowsAsync<DomainException>(() => db.Run<LookupService>(s =>
            s.UpdateItemAsync(root, new SaveLookupItemRequest(group, "مشتریان عمده", null, child, 0, true), default)));
        var dup = await Assert.ThrowsAsync<DomainException>(() => db.Run<LookupService, int>(s =>
            s.CreateItemAsync(new SaveLookupItemRequest(group, "پخش تهران", null, root, 0, true), default)));
        Assert.Contains("پخش تهران", dup.Message);

        // گروه استفاده‌شده در شخص با کلید خارجی حذف نمی‌شود
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
        await db.Run<PersonService, int>(s => s.CreateAsync(new SavePersonRequest
        {
            FirstName = "عضو", LastName = "گروه", PersonGroupId = child, RoleIds = [roles.Single(r => r.Code == RoleCodes.Customer).Id]
        }, default));
        var people = await db.Run<PersonService, List<PersonListItemDto>>(s => s.SearchAsync(null, null, root, false, default));
        Assert.Contains(people, p => p.DisplayName == "عضو گروه" && p.GroupTitle == "پخش تهران"); // فیلتر گروه زیرگروه‌ها را هم می‌آورد
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Run<LookupService>(s => s.DeleteItemAsync(child, default)));

        var leaf = await db.Run<LookupService, int>(s => s.CreateItemAsync(new SaveLookupItemRequest(group, "موقت", null, null, 0, true), default));
        await db.Run<LookupService>(s => s.DeleteItemAsync(leaf, default));

        var system = (await Items(LookupCodes.Province)).First();
        await Assert.ThrowsAsync<DomainException>(() => db.Run<LookupService>(s => s.DeleteItemAsync(system.Id, default)));
    }

    [Fact]
    public async Task Banks_validate_codes_and_detect_card()
    {
        if (!db.Enabled) return;
        var bad = await Assert.ThrowsAsync<DomainException>(() => db.Run<BankService, short>(s =>
            s.CreateAsync(new SaveBankRequest("بانک آزمایشی", "12", null, true), default)));
        Assert.Contains("سه رقم", bad.Message);

        var dupIban = await Assert.ThrowsAsync<DomainException>(() => db.Run<BankService, short>(s =>
            s.CreateAsync(new SaveBankRequest("بانک تکراری", "017", null, true), default)));
        Assert.Contains("017", dupIban.Message);

        var id = await db.Run<BankService, short>(s => s.CreateAsync(new SaveBankRequest("بانک آزمایشی", "099", "999901", true), default));
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(false, default));
        var person = await db.Run<PersonService, int>(s => s.CreateAsync(new SavePersonRequest
        {
            FirstName = "دارنده", LastName = "کارت", RoleIds = [roles.Single(r => r.Code == RoleCodes.Supplier).Id],
            BankAccounts = [new Erp.BaseData.Application.Persons.BankAccountDto { CardNo = LuhnCard("999901") }]
        }, default));
        var p = await db.Run<PersonService, PersonDetailsDto>(s => s.GetAsync(person, default));
        Assert.Equal(id, p.BankAccounts.Single().BankId);   // بانک از پیش‌شماره کارت

        await Assert.ThrowsAsync<DomainException>(() => db.Run<BankService>(s => s.DeleteAsync(id, default)));
        var unused = await db.Run<BankService, short>(s => s.CreateAsync(new SaveBankRequest("بانک حذفی", null, null, true), default));
        await db.Run<BankService>(s => s.DeleteAsync(unused, default));
    }

    [Fact]
    public async Task Role_types_crud_and_system_roles_are_protected()
    {
        if (!db.Enabled) return;
        var id = await db.Run<PersonService, short>(s => s.CreateRoleTypeAsync("نماینده فروش", default));
        var roles = await db.Run<PersonService, List<PersonRoleTypeDto>>(s => s.GetRoleTypesAsync(true, default));
        Assert.Contains(roles, r => r.Id == id && !r.IsSystem);
        await db.Run<PersonService>(s => s.DeleteRoleTypeAsync(id, default));

        var customer = roles.Single(r => r.Code == RoleCodes.Customer);
        await Assert.ThrowsAsync<DomainException>(() => db.Run<PersonService>(s => s.DeleteRoleTypeAsync(customer.Id, default)));
    }

    /// <summary>شماره کارت ۱۶ رقمی معتبر (لوهن) با پیش‌شماره داده‌شده.</summary>
    private static string LuhnCard(string prefix)
    {
        var body = prefix + "123456789";            // ۱۵ رقم
        var sum = 0;
        for (var i = 0; i < 15; i++)
        {
            var d = body[i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return body + (10 - sum % 10) % 10;
    }
}
