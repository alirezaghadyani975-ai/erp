using Erp.SharedKernel.Security;

namespace Erp.BaseData.Application;

/// <summary>مجوزهای اطلاعات پایه. جستجوی اشخاص و جدول‌های پایه در فرم‌های دیگر مجوز جدا لازم ندارد.</summary>
public static class BaseDataPermissions
{
    public const string PersonView = "base.person.view";
    public const string PersonCreate = "base.person.create";
    public const string PersonEdit = "base.person.edit";
    public const string PersonDelete = "base.person.delete";
    public const string PersonRoles = "base.personrole.edit";
    public const string Lookups = "base.lookup.edit";
    public const string Banks = "base.bank.edit";
    public const string VatRates = "base.vat.edit";

    public static IPermissionSource List { get; } = new PermissionList("اطلاعات پایه")
        .Crud("base.person", "اشخاص")
        .Add("base.personrole", "نقش‌های اشخاص", ("edit", "تعریف و اصلاح"))
        .Add("base.lookup", "جدول‌های پایه", ("edit", "تعریف و اصلاح"))
        .Add("base.bank", "بانک‌ها", ("edit", "تعریف و اصلاح"))
        .Add("base.vat", "نرخ مالیات و عوارض", ("edit", "تعریف و اصلاح"));
}
