using System.Globalization;
using Erp.Accounting.Domain.ChartOfAccounts;
using Erp.Accounting.Domain.FiscalYears;
using Erp.Accounting.Domain.Vouchers;
using MudBlazor;

namespace Erp.Api.Components.Shared;

/// <summary>متن فارسی مقادیر و قالب‌بندی مبالغ.</summary>
public static class Format
{
    public static string Amount(decimal value) => value.ToString("#,0.####", CultureInfo.InvariantCulture);

    public static string Status(VoucherStatus s) => s switch
    {
        VoucherStatus.Draft => "پیش‌نویس",
        VoucherStatus.Temporary => "موقت",
        VoucherStatus.Permanent => "دائم",
        VoucherStatus.Void => "باطل",
        _ => s.ToString()
    };

    public static Color StatusColor(VoucherStatus s) => s switch
    {
        VoucherStatus.Draft => Color.Default,
        VoucherStatus.Temporary => Color.Info,
        VoucherStatus.Permanent => Color.Success,
        VoucherStatus.Void => Color.Error,
        _ => Color.Default
    };

    public static string Type(VoucherType t) => t switch
    {
        VoucherType.Opening => "افتتاحیه",
        VoucherType.Normal => "عادی",
        VoucherType.ClosingTemporary => "بستن حساب‌های موقت",
        VoucherType.Closing => "اختتامیه",
        _ => t.ToString()
    };

    public static string Period(PeriodStatus s) => s switch
    {
        PeriodStatus.Open => "باز",
        PeriodStatus.Locked => "قفل",
        PeriodStatus.Closed => "بسته",
        _ => s.ToString()
    };

    public static string Level(AccountLevel l) => l switch
    {
        AccountLevel.Group => "گروه",
        AccountLevel.General => "کل",
        AccountLevel.Subsidiary => "معین",
        _ => l.ToString()
    };

    public static string Nature(AccountNature? n) => n switch
    {
        AccountNature.Debit => "بدهکار",
        AccountNature.Credit => "بستانکار",
        AccountNature.Both => "هر دو",
        _ => "—"
    };

    public static string AccountTypeText(AccountType? t) => t switch
    {
        AccountType.BalanceSheet => "ترازنامه‌ای",
        AccountType.ProfitAndLoss => "سود و زیانی",
        AccountType.Memorandum => "انتظامی",
        _ => "—"
    };

    public static string Today() => PersianDate.ToPersian(DateOnly.FromDateTime(DateTime.Now));

    private static class PersianDate
    {
        public static string ToPersian(DateOnly d) => Erp.SharedKernel.Calendar.PersianDate.ToPersian(d);
    }
}
