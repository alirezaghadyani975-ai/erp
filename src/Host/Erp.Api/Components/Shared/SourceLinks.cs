namespace Erp.Api.Components.Shared;

/// <summary>فرم صادرکننده سند خودکار: عنوان و نشانی فرم از روی نوع سند منبع.</summary>
public static class SourceLinks
{
    public static string Title(string? docType) => docType switch
    {
        Erp.Expenses.Domain.ExpenseInvoice.DocType => "شناسایی هزینه",
        Erp.Treasury.Domain.Receipt.DocType => "دریافت",
        Erp.Treasury.Domain.Payment.DocType => "پرداخت",
        Erp.Treasury.Domain.Transfer.DocType => "انتقال وجه",
        Erp.Treasury.Domain.ImprestSettlement.DocType => "صورت‌هزینه تنخواه",
        Erp.Treasury.Domain.ChequeOperation.ReceivableDocType => "عملیات اسناد دریافتنی",
        Erp.Treasury.Domain.ChequeOperation.PayableDocType => "عملیات اسناد پرداختنی",
        Erp.Treasury.Domain.Guarantee.DocType => "سند تضمینی",
        Erp.Treasury.Domain.Guarantee.ReturnDocType => "استرداد سند تضمینی",
        null => "",
        _ => docType
    };

    public static string? Url(string? docType, long? id) => (docType, id) switch
    {
        (Erp.Expenses.Domain.ExpenseInvoice.DocType, long i) => $"/expenses/{i}",
        (Erp.Treasury.Domain.Receipt.DocType, long i) => $"/treasury/receipts/{i}",
        (Erp.Treasury.Domain.Payment.DocType, long i) => $"/treasury/payments/{i}",
        (Erp.Treasury.Domain.Transfer.DocType, long i) => $"/treasury/transfers/{i}",
        (Erp.Treasury.Domain.ImprestSettlement.DocType, long i) => $"/treasury/imprest/{i}",
        (Erp.Treasury.Domain.ChequeOperation.ReceivableDocType or Erp.Treasury.Domain.ChequeOperation.PayableDocType, long i) => $"/treasury/cheque-ops/{i}",
        (Erp.Treasury.Domain.Guarantee.DocType or Erp.Treasury.Domain.Guarantee.ReturnDocType, long i) => $"/treasury/guarantees/{i}",
        _ => null
    };
}
