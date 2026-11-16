using Erp.SharedKernel.Security;

namespace Erp.Costing.Application;

public static class CostingPermissions
{
    public const string CenterView = "cost.center.view";
    public const string CenterCreate = "cost.center.create";
    public const string CenterEdit = "cost.center.edit";
    public const string CenterDelete = "cost.center.delete";
    public const string FactorView = "cost.factor.view";
    public const string FactorCreate = "cost.factor.create";
    public const string FactorEdit = "cost.factor.edit";
    public const string FactorDelete = "cost.factor.delete";
    public const string CenterTypes = "cost.centertype.edit";

    public static IPermissionSource List { get; } = new PermissionList("بهای تمام‌شده")
        .Crud("cost.center", "مراکز هزینه")
        .Crud("cost.factor", "عوامل هزینه")
        .Add("cost.centertype", "انواع مرکز هزینه", ("edit", "تعریف و اصلاح"));
}
