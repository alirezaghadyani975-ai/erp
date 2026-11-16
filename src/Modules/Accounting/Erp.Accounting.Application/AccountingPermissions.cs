using Erp.SharedKernel.Security;

namespace Erp.Accounting.Application;

/// <summary>مجوزهای ماژول حسابداری (کلیدها در دیتابیس مرکزی ذخیره می‌شوند؛ تغییرشان ندهید).</summary>
public static class AccountingPermissions
{
    public const string VoucherView = "acc.voucher.view";
    public const string VoucherCreate = "acc.voucher.create";
    public const string VoucherEdit = "acc.voucher.edit";
    public const string VoucherDelete = "acc.voucher.delete";
    public const string VoucherTemporary = "acc.voucher.temporary";
    public const string VoucherPermanent = "acc.voucher.permanent";
    public const string VoucherDraft = "acc.voucher.draft";
    public const string VoucherVoid = "acc.voucher.void";

    public const string AccountView = "acc.account.view";
    public const string AccountCreate = "acc.account.create";
    public const string AccountEdit = "acc.account.edit";
    public const string AccountDelete = "acc.account.delete";

    public const string DetailView = "acc.detail.view";
    public const string DetailCreate = "acc.detail.create";
    public const string DetailEdit = "acc.detail.edit";
    public const string DetailDelete = "acc.detail.delete";

    public const string FiscalView = "acc.fiscal.view";
    public const string FiscalCreate = "acc.fiscal.create";
    public const string FiscalDelete = "acc.fiscal.delete";
    public const string FiscalPeriod = "acc.fiscal.period";

    public const string TemplateView = "acc.template.view";
    public const string TemplateEdit = "acc.template.edit";

    public const string TrialBalance = "acc.report.trial";
    public const string Ledger = "acc.report.ledger";
    public const string Dashboard = "acc.dashboard.view";

    public static IPermissionSource List { get; } = new PermissionList("حسابداری")
        .Add("acc.voucher", "اسناد حسابداری", ("view", "مشاهده"), ("create", "ایجاد"), ("edit", "اصلاح"), ("delete", "حذف"),
            ("temporary", "موقت کردن"), ("permanent", "دائم کردن"), ("draft", "برگشت به پیش‌نویس"), ("void", "ابطال"))
        .Crud("acc.account", "کدینگ حساب‌ها")
        .Crud("acc.detail", "گروه‌ها و حساب‌های تفصیلی")
        .Add("acc.fiscal", "سال و دوره مالی", ("view", "مشاهده"), ("create", "ایجاد"), ("delete", "حذف"), ("period", "باز و بسته کردن دوره"))
        .Add("acc.template", "الگوهای صدور سند", ("view", "مشاهده"), ("edit", "اصلاح"))
        .Add("acc.report", "گزارش‌های حسابداری", ("trial", "تراز آزمایشی"), ("ledger", "دفتر معین و تفصیلی"))
        .Add("acc.dashboard", "صفحه نخست", ("view", "خلاصه مالی"));
}
