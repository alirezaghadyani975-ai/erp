using Erp.SharedKernel.Security;

namespace Erp.Expenses.Application;

public static class ExpensePermissions
{
    public const string View = "exp.invoice.view";
    public const string Create = "exp.invoice.create";
    public const string Edit = "exp.invoice.edit";
    public const string Delete = "exp.invoice.delete";

    public static IPermissionSource List { get; } = new PermissionList("هزینه‌ها").Crud("exp.invoice", "شناسایی هزینه");
}
