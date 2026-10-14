using Erp.Accounting.Domain.Details;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Persons;

namespace Erp.Api.Components.Shared;

/// <summary>عنوان فارسی مقادیر enum و ثابت‌ها برای نمایش در فرم‌ها.</summary>
public static class Titles
{
    private static readonly Dictionary<Enum, string> Map = new()
    {
        [PersonKind.Natural] = "حقیقی", [PersonKind.Legal] = "حقوقی",
        [Gender.Male] = "مرد", [Gender.Female] = "زن",
        [MaritalStatus.Single] = "مجرد", [MaritalStatus.Married] = "متأهل",
        [LegalType.PrivateJointStock] = "سهامی خاص", [LegalType.PublicJointStock] = "سهامی عام",
        [LegalType.LimitedLiability] = "با مسئولیت محدود", [LegalType.Partnership] = "تضامنی",
        [LegalType.Cooperative] = "تعاونی", [LegalType.Government] = "دولتی",
        [LegalType.NonCommercial] = "مؤسسه غیرتجاری", [LegalType.Other] = "سایر",
        [TaxPayerType.Natural] = "حقیقی", [TaxPayerType.Legal] = "حقوقی",
        [TaxPayerType.CivilPartnership] = "مشارکت مدنی", [TaxPayerType.Foreign] = "اتباع غیرایرانی",
        [TaxPayerType.FinalConsumer] = "مصرف‌کننده نهایی",
    };

    public static string Of<TEnum>(TEnum? value) where TEnum : struct, Enum =>
        value is { } v ? Map.GetValueOrDefault(v, v.ToString()) : "—";

    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum => Map.GetValueOrDefault(value, value.ToString());

    public static string Owner(string ownerModule) => ownerModule switch
    {
        OwnerModules.Persons => "اشخاص (طرف‌حساب‌ها)",
        OwnerModules.Payroll => "کارکنان (حقوق و دستمزد)",
        OwnerModules.Treasury => "خزانه‌داری (صندوق، بانک، تسهیلات)",
        OwnerModules.Assets => "دارایی‌های ثابت",
        OwnerModules.Manual => "دستی (مرکز هزینه، پروژه، ...)",
        _ => ownerModule
    };

    public static string OwnerHint(string ownerModule) => ownerModule switch
    {
        OwnerModules.Persons => "تفصیلی خودکار از فرم اشخاص ساخته می‌شود؛ هر شخص یک کد مشترک برای همه نقش‌ها می‌گیرد. برای هر نوع شخص فقط یک گروه فعال.",
        OwnerModules.Payroll => "تفصیلی کارمند خودکار با افزودن نقش «کارمند» به شخص ساخته می‌شود.",
        OwnerModules.Treasury => "صندوق‌ها، حساب‌های بانکی، اعتبارات اسنادی و تسهیلات؛ بعداً از ماژول خزانه ساخته می‌شوند.",
        OwnerModules.Assets => "دارایی‌هایی که جداگانه پیگیری می‌شوند؛ بعداً از ماژول دارایی ثابت ساخته می‌شوند.",
        _ => "تفصیلی‌ها دستی در همین برنامه تعریف می‌شوند (مرکز هزینه، پروژه، قرارداد، ...)."
    };
}
