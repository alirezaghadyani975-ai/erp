namespace Erp.Api.Components.Shared;

/// <summary>فرم صادرکننده سند خودکار: عنوان و نشانی فرم از روی نوع سند منبع.</summary>
public static class SourceLinks
{
    public static string Title(string? docType) => docType switch
    {
        Erp.Expenses.Domain.ExpenseInvoice.DocType => "شناسایی هزینه",
        null => "",
        _ => docType
    };

    public static string? Url(string? docType, long? id) => (docType, id) switch
    {
        (Erp.Expenses.Domain.ExpenseInvoice.DocType, long i) => $"/expenses/{i}",
        _ => null
    };
}
