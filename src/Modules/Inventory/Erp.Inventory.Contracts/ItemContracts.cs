namespace Erp.Inventory.Contracts;

/// <summary>نوع کالا/خدمت (inv.Items.Kind و inv.ItemGroups.Kind).</summary>
public enum ItemKind : byte
{
    Goods = 1,
    Service = 2
}

/// <summary>
/// محاسبه مبلغ ردیف فاکتور خرید و فروش و سهم برگشتی. مبالغ به ریال گرد می‌شوند (سامانه مودیان مبلغ اعشاری نمی‌پذیرد).
/// </summary>
public static class TradeMath
{
    public static decimal Rial(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>مبلغ ناخالص ردیف (مقدار × فی).</summary>
    public static decimal Gross(decimal quantity, decimal unitPrice) => Rial(quantity * unitPrice);

    /// <summary>
    /// سهم مقدار برگشتی از یک مبلغ ردیف. برگشت آخرین مقدار مانده، باقی مبلغ را می‌گیرد تا جمع برگشت‌ها
    /// دقیقاً برابر ردیف فاکتور شود (بدون اختلاف گرد کردن).
    /// </summary>
    /// <param name="lineAmount">مبلغ ردیف فاکتور.</param>
    /// <param name="lineQuantity">مقدار ردیف فاکتور.</param>
    /// <param name="previousQuantity">مقدار برگشت‌شده در برگشت‌های دیگر.</param>
    /// <param name="previousAmount">مبلغ برگشت‌شده در برگشت‌های دیگر.</param>
    /// <param name="quantity">مقدار این برگشت.</param>
    public static decimal Share(decimal lineAmount, decimal lineQuantity, decimal previousQuantity, decimal previousAmount, decimal quantity) =>
        previousQuantity + quantity >= lineQuantity
            ? lineAmount - previousAmount
            : Rial(lineAmount * quantity / lineQuantity);
}
