using Erp.Costing.Contracts;

namespace Erp.Accounting.Domain.ChartOfAccounts;

/// <summary>عنوان فارسی ماهیت و وظیفه هزینه برای پیام‌ها.</summary>
public static class CostTitles
{
    public static string Of(CostNature nature) => nature switch
    {
        CostNature.Payroll => "حقوق و دستمزد",
        CostNature.Depreciation => "استهلاک",
        CostNature.OtherOperating => "سایر هزینه‌های عملیاتی",
        CostNature.Finance => "هزینه‌های مالی",
        _ => nature.ToString()
    };

    public static string Of(CostFunction function) => function switch
    {
        CostFunction.Production => "تولیدی",
        CostFunction.Service => "خدماتی (پشتیبانی تولید)",
        CostFunction.Admin => "اداری",
        CostFunction.Selling => "فروش و توزیع",
        _ => function.ToString()
    };
}
