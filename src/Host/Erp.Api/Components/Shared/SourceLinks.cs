namespace Erp.Api.Components.Shared;

/// <summary>فرم صادرکننده سند خودکار: عنوان و نشانی فرم از روی نوع سند منبع.</summary>
public static class SourceLinks
{
    public static string Title(string? docType) => docType switch
    {
        Erp.Expenses.Domain.ExpenseInvoice.DocType => "شناسایی هزینه",
        Erp.Sales.Domain.SalesInvoice.DocType => "فاکتور فروش",
        Erp.Sales.Domain.SalesReturn.DocType => "برگشت از فروش",
        Erp.Purchases.Domain.PurchaseInvoice.DocType => "فاکتور خرید",
        Erp.Purchases.Domain.PurchaseReturn.DocType => "برگشت از خرید",
        Erp.Inventory.Domain.CostRun.DocType => "بهای تمام‌شده ماه (انبار)",
        Erp.Treasury.Domain.Receipt.DocType => "دریافت",
        Erp.Treasury.Domain.Payment.DocType => "پرداخت",
        Erp.Treasury.Domain.Transfer.DocType => "انتقال وجه",
        Erp.Treasury.Domain.ImprestSettlement.DocType => "صورت‌هزینه تنخواه",
        Erp.Treasury.Domain.ChequeOperation.ReceivableDocType => "عملیات اسناد دریافتنی",
        Erp.Treasury.Domain.ChequeOperation.PayableDocType => "عملیات اسناد پرداختنی",
        Erp.Treasury.Domain.Guarantee.DocType => "سند تضمینی",
        Erp.Treasury.Domain.Guarantee.ReturnDocType => "استرداد سند تضمینی",
        Erp.Accounting.Application.Closing.ClosingService.CloseTemporaryDocType or Erp.Accounting.Application.Closing.ClosingService.CloseDocType
            or Erp.Accounting.Application.Closing.ClosingService.OpeningDocType => "بستن سال مالی",
        null => "",
        _ => docType
    };

    public static string? Url(string? docType, long? id) => (docType, id) switch
    {
        (Erp.Expenses.Domain.ExpenseInvoice.DocType, long i) => $"/expenses/{i}",
        (Erp.Sales.Domain.SalesInvoice.DocType, long i) => $"/sales/{i}",
        (Erp.Sales.Domain.SalesReturn.DocType, long i) => $"/sales/returns/{i}",
        (Erp.Purchases.Domain.PurchaseInvoice.DocType, long i) => $"/purchases/{i}",
        (Erp.Purchases.Domain.PurchaseReturn.DocType, long i) => $"/purchases/returns/{i}",
        (Erp.Inventory.Domain.CostRun.DocType, long) => "/inventory/costing",
        (Erp.Treasury.Domain.Receipt.DocType, long i) => $"/treasury/receipts/{i}",
        (Erp.Treasury.Domain.Payment.DocType, long i) => $"/treasury/payments/{i}",
        (Erp.Treasury.Domain.Transfer.DocType, long i) => $"/treasury/transfers/{i}",
        (Erp.Treasury.Domain.ImprestSettlement.DocType, long i) => $"/treasury/imprest/{i}",
        (Erp.Treasury.Domain.ChequeOperation.ReceivableDocType or Erp.Treasury.Domain.ChequeOperation.PayableDocType, long i) => $"/treasury/cheque-ops/{i}",
        (Erp.Treasury.Domain.Guarantee.DocType or Erp.Treasury.Domain.Guarantee.ReturnDocType, long i) => $"/treasury/guarantees/{i}",
        (Erp.Accounting.Application.Closing.ClosingService.CloseTemporaryDocType or Erp.Accounting.Application.Closing.ClosingService.CloseDocType
            or Erp.Accounting.Application.Closing.ClosingService.OpeningDocType, long i) => $"/fiscal-years/closing?year={i}",
        _ => null
    };
}
