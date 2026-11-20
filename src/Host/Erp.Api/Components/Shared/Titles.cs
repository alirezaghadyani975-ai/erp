using Erp.Accounting.Domain.Details;
using Erp.BaseData.Contracts;
using Erp.BaseData.Domain.Persons;
using Erp.Costing.Contracts;

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
        [CostNature.Payroll] = "حقوق و دستمزد", [CostNature.Depreciation] = "استهلاک",
        [CostNature.Personnel] = "هزینه کارکنان",
        [CostNature.OtherOperating] = "سایر هزینه‌های عملیاتی", [CostNature.Finance] = "هزینه‌های مالی (تأمین مالی)",
        [CostFunction.Production] = "تولیدی", [CostFunction.Service] = "خدماتی (پشتیبانی تولید)",
        [CostFunction.Admin] = "اداری", [CostFunction.Selling] = "فروش و توزیع",
        [CostBehavior.Fixed] = "ثابت", [CostBehavior.Variable] = "متغیر", [CostBehavior.Mixed] = "نیمه‌متغیر",
        [Erp.Inventory.Contracts.ItemKind.Goods] = "کالا", [Erp.Inventory.Contracts.ItemKind.Service] = "خدمت",
        [Erp.Sales.Domain.SettlementMethod.Cash] = "نقد", [Erp.Sales.Domain.SettlementMethod.Credit] = "نسیه",
        [Erp.Sales.Domain.SettlementMethod.Mixed] = "نقد و نسیه",
        [Erp.Sales.Domain.TaxInvoiceType.WithBuyer] = "نوع اول (با مشخصات خریدار)",
        [Erp.Sales.Domain.TaxInvoiceType.WithoutBuyer] = "نوع دوم (بدون مشخصات خریدار)",
        [Erp.Sales.Domain.TaxStatus.NotSent] = "ارسال‌نشده به مودیان",
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
        OwnerModules.Manual => "دستی (پروژه، قرارداد، ...)",
        OwnerModules.CostFactors => "عوامل هزینه (بهای تمام‌شده)",
        OwnerModules.Inventory => "انبارها (انبار و کالا)",
        OwnerModules.CostCenters => "مراکز هزینه (بهای تمام‌شده)",
        _ => ownerModule
    };

    public static string OwnerHint(string ownerModule) => ownerModule switch
    {
        OwnerModules.Persons => "تفصیلی خودکار از فرم اشخاص ساخته می‌شود؛ هر شخص یک کد مشترک برای همه نقش‌ها می‌گیرد. برای هر نوع شخص فقط یک گروه فعال.",
        OwnerModules.Payroll => "تفصیلی کارمند خودکار با افزودن نقش «کارمند» به شخص ساخته می‌شود.",
        OwnerModules.Treasury => "صندوق‌ها، حساب‌های بانکی، اعتبارات اسنادی و تسهیلات؛ بعداً از ماژول خزانه ساخته می‌شوند.",
        OwnerModules.Assets => "دارایی‌هایی که جداگانه پیگیری می‌شوند؛ بعداً از ماژول دارایی ثابت ساخته می‌شوند.",
        OwnerModules.CostFactors => "تفصیلی خودکار از تعریف عامل هزینه ساخته می‌شود (بهای تمام‌شده ← عوامل هزینه).",
        OwnerModules.Inventory => "انبارها؛ فعلاً دستی، بعداً از ماژول انبار ساخته می‌شوند.",
        OwnerModules.CostCenters => "تفصیلی خودکار از تعریف مرکز هزینه ساخته می‌شود (بهای تمام‌شده ← مراکز هزینه).",
        _ => "تفصیلی‌ها دستی در همین برنامه تعریف می‌شوند (پروژه، قرارداد، ...)."
    };
}
