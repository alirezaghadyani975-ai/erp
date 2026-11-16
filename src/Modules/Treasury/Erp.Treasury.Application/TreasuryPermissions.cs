using Erp.SharedKernel.Security;
using Erp.Treasury.Domain;

namespace Erp.Treasury.Application;

public static class TreasuryPermissions
{
    public const string ReceiptView = "trs.receipt.view";
    public const string ReceiptCreate = "trs.receipt.create";
    public const string ReceiptEdit = "trs.receipt.edit";
    public const string ReceiptDelete = "trs.receipt.delete";
    public const string PaymentView = "trs.payment.view";
    public const string PaymentCreate = "trs.payment.create";
    public const string PaymentEdit = "trs.payment.edit";
    public const string PaymentDelete = "trs.payment.delete";
    public const string TransferView = "trs.transfer.view";
    public const string TransferCreate = "trs.transfer.create";
    public const string TransferEdit = "trs.transfer.edit";
    public const string TransferDelete = "trs.transfer.delete";
    public const string ImprestView = "trs.imprest.view";
    public const string ImprestCreate = "trs.imprest.create";
    public const string ImprestEdit = "trs.imprest.edit";
    public const string ImprestDelete = "trs.imprest.delete";
    public const string ChequeInView = "trs.chequein.view";
    public const string ChequeInDelete = "trs.chequein.delete";
    public const string ChequeOutView = "trs.chequeout.view";
    public const string ChequeOutDelete = "trs.chequeout.delete";
    public const string GuaranteeView = "trs.guarantee.view";
    public const string GuaranteeCreate = "trs.guarantee.create";
    public const string GuaranteeEdit = "trs.guarantee.edit";
    public const string GuaranteeDelete = "trs.guarantee.delete";
    public const string GuaranteeReturn = "trs.guarantee.return";
    public const string InstrumentView = "trs.instrument.view";
    public const string InstrumentEdit = "trs.instrument.edit";
    public const string InstrumentVoid = "trs.instrument.void";
    public const string Setup = "trs.setup.edit";
    public const string Reports = "trs.report.view";

    /// <summary>مجوز هر عملیات چک (مثلاً trs.chequein.deposit).</summary>
    public static string Operation(ChequeOperationType op) => op switch
    {
        ChequeOperationType.Deposit => "trs.chequein.deposit",
        ChequeOperationType.Collect => "trs.chequein.collect",
        ChequeOperationType.Bounce => "trs.chequein.bounce",
        ChequeOperationType.ReturnToDrawer => "trs.chequein.return",
        ChequeOperationType.Legal => "trs.chequein.legal",
        ChequeOperationType.Withdraw => "trs.chequein.withdraw",
        ChequeOperationType.Clear => "trs.chequeout.clear",
        _ => "trs.chequeout.cancel",
    };

    public static string View(ChequeSide side) => side == ChequeSide.Receivable ? ChequeInView : ChequeOutView;

    public static string Delete(ChequeSide side) => side == ChequeSide.Receivable ? ChequeInDelete : ChequeOutDelete;

    public static IPermissionSource List { get; } = new PermissionList("خزانه‌داری")
        .Crud("trs.receipt", "دریافت")
        .Crud("trs.payment", "پرداخت")
        .Crud("trs.transfer", "انتقال وجه")
        .Crud("trs.imprest", "صورت‌هزینه تنخواه")
        .Add("trs.chequein", "عملیات اسناد دریافتنی", ("view", "مشاهده"), ("deposit", "واگذاری به بانک"), ("collect", "وصول"),
            ("bounce", "برگشت"), ("return", "عودت به صاحب چک"), ("legal", "حقوقی"), ("withdraw", "برگشت از واگذاری"), ("delete", "حذف عملیات"))
        .Add("trs.chequeout", "عملیات اسناد پرداختنی", ("view", "مشاهده"), ("clear", "پاس"), ("cancel", "ابطال/عودت"), ("delete", "حذف عملیات"))
        .Add("trs.guarantee", "اسناد تضمینی", ("view", "مشاهده"), ("create", "ایجاد"), ("edit", "اصلاح"), ("delete", "حذف"), ("return", "عودت/برگشت"))
        .Add("trs.instrument", "دسته‌چک، سفته و ضمانت‌نامه", ("view", "مشاهده"), ("edit", "تعریف و اصلاح"), ("void", "ابطال برگ"))
        .Add("trs.setup", "تعاریف خزانه‌داری", ("edit", "تعریف و اصلاح"))
        .Add("trs.report", "گزارش‌های خزانه", ("view", "مشاهده"));
}
