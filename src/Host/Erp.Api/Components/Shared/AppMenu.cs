using Erp.Accounting.Application;
using Erp.BaseData.Application;
using Erp.Costing.Application;
using Erp.Expenses.Application;
using Erp.Security.Application;
using Erp.SharedKernel.Security;
using Erp.Treasury.Application;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using A = Erp.Accounting.Application.AccountingPermissions;
using B = Erp.BaseData.Application.BaseDataPermissions;
using C = Erp.Costing.Application.CostingPermissions;
using S = Erp.Security.Application.SecurityPermissions;
using T = Erp.Treasury.Application.TreasuryPermissions;
using I = Erp.Inventory.Application.InventoryPermissions;
using PU = Erp.Purchases.Application.PurchasePermissions;
using SL = Erp.Sales.Application.SalesPermissions;
using H = Erp.Personnel.Application.PersonnelPermissions;
using PY = Erp.Payroll.Application.PayrollPermissions;

namespace Erp.Api.Components.Shared;

/// <summary>
/// منوی اصلی و مجوز لازم برای هر صفحه. منو فقط صفحه‌هایی را نشان می‌دهد که کاربر مجوز مشاهده‌شان را دارد
/// و گروهی که چیزی در آن نمانده پنهان می‌شود. باز کردن مستقیم آدرس صفحه بدون مجوز هم پیام «دسترسی ندارید» می‌دهد.
/// (کنترل اصلی همچنان در سرویس‌هاست.)
/// </summary>
public static class AppMenu
{
    public sealed record Node(string Title, string Icon, string? Href = null, string[]? Permissions = null,
        NavLinkMatch Match = NavLinkMatch.Prefix, bool Expanded = true, Node[]? Children = null)
    {
        public bool IsVisible(IAccessControl access) =>
            Children is { } children
                ? children.Any(c => c.IsVisible(access))
                : Permissions is null || Permissions.Any(access.Has);
    }

    private static string[] P(params string[] keys) => keys;

    public static readonly Node[] Tree =
    [
        new("صفحه نخست", Icons.Material.Outlined.Home, "/", Match: NavLinkMatch.All),
        // ترتیب بلندمدت: فروش و خرید (عملیات تجاری)، انبار، (تولید)، بهای تمام‌شده، مالی
        new("فروش", Icons.Material.Outlined.PointOfSale, Children:
        [
            new("فاکتور فروش", Icons.Material.Outlined.Receipt, "/sales", P(SL.InvoiceView), NavLinkMatch.All),
            new("برگشت از فروش", Icons.Material.Outlined.AssignmentReturn, "/sales/returns", P(SL.ReturnView)),
        ]),
        new("خرید", Icons.Material.Outlined.ShoppingCart, Children:
        [
            new("فاکتور خرید", Icons.Material.Outlined.ReceiptLong, "/purchases", P(PU.InvoiceView), NavLinkMatch.All),
            new("برگشت از خرید", Icons.Material.Outlined.RemoveShoppingCart, "/purchases/returns", P(PU.ReturnView)),
        ]),
        new("انبار", Icons.Material.Outlined.Warehouse, Children:
        [
            new("رسیدهای انبار", Icons.Material.Outlined.MoveToInbox, "/inventory/receipts", P(I.ReceiptView)),
            new("حواله‌های انبار", Icons.Material.Outlined.Outbox, "/inventory/issues", P(I.IssueView)),
            new("گزارش‌های انبار", Icons.Material.Outlined.Assessment, Children:
            [
                new("کاردکس کالا", Icons.Material.Outlined.ViewList, "/inventory/kardex", P(I.Kardex)),
                new("موجودی انبارها", Icons.Material.Outlined.Inventory, "/inventory/balances", P(I.Kardex)),
            ]),
            new("تعاریف انبار", Icons.Material.Outlined.Tune, Expanded: false, Children:
            [
                new("کالا و خدمات", Icons.Material.Outlined.Inventory2, "/inventory/items", P(I.ItemView)),
                new("گروه‌های کالا", Icons.Material.Outlined.Category, "/inventory/groups", P(I.Groups)),
                new("انبارها", Icons.Material.Outlined.Warehouse, "/inventory/warehouses", P(I.Warehouses)),
            ]),
        ]),
        new("بهای تمام‌شده", Icons.Material.Outlined.Factory, Children:
        [
            new("محاسبه بهای تمام‌شده", Icons.Material.Outlined.Calculate, "/inventory/costing", P(I.Costing)),
            new("تعاریف بهای تمام‌شده", Icons.Material.Outlined.Tune, Expanded: false, Children:
            [
                new("مراکز هزینه", Icons.Material.Outlined.Hub, "/costing/centers", P(C.CenterView)),
                new("عوامل هزینه", Icons.Material.Outlined.Category, "/costing/factors", P(C.FactorView)),
                new("انواع مرکز هزینه", Icons.Material.Outlined.Lan, "/costing/center-types", P(C.CenterTypes)),
            ]),
        ]),
        new("منابع انسانی", Icons.Material.Outlined.Badge, Children:
        [
            new("پرونده‌های پرسنلی", Icons.Material.Outlined.Badge, "/hr/employees", P(H.EmployeeView)),
            new("محاسبه حقوق", Icons.Material.Outlined.Calculate, "/payroll/runs", P(PY.RunView)),
            new("احکام حقوقی", Icons.Material.Outlined.HistoryEdu, "/payroll/decrees", P(PY.DecreeView)),
            new("وام و مساعده", Icons.Material.Outlined.Savings, "/payroll/loans", P(PY.LoanView)),
            new("تسویه حساب", Icons.Material.Outlined.Logout, "/payroll/settlement", P(PY.RunEdit)),
            new("گزارش‌های حقوق", Icons.Material.Outlined.Assessment, Expanded: false, Children:
            [
                new("لیست حقوق و دستمزد", Icons.Material.Outlined.TableChart, "/payroll/reports?type=register", P(PY.ReportView)),
                new("کارکرد ماهانه", Icons.Material.Outlined.EventNote, "/payroll/reports?type=attendance", P(PY.ReportView)),
                new("کاردکس مرخصی", Icons.Material.Outlined.BeachAccess, "/payroll/reports?type=leave", P(PY.ReportView)),
                new("خلاصه سالانه اقلام", Icons.Material.Outlined.CalendarViewMonth, "/payroll/reports?type=summary", P(PY.ReportView)),
                new("سوابق حقوق کارمند", Icons.Material.Outlined.PersonSearch, "/payroll/reports?type=history", P(PY.ReportView)),
                new("هزینه حقوق به تفکیک مرکز", Icons.Material.Outlined.Hub, "/payroll/reports?type=cost", P(PY.ReportView)),
                new("مانده ذخیره عیدی و سنوات", Icons.Material.Outlined.Savings, "/payroll/reports?type=provisions", P(PY.ReportView)),
            ]),
            new("تعاریف کارگزینی و حقوق", Icons.Material.Outlined.Tune, Expanded: false, Children:
            [
                new("اقلام حقوق", Icons.Material.Outlined.Functions, "/payroll/items", P(PY.Setup)),
                new("پارامترهای قانونی سال", Icons.Material.Outlined.Gavel, "/payroll/params", P(PY.Setup)),
                new("کارگاه‌های بیمه", Icons.Material.Outlined.Business, "/hr/workshops", P(H.Setup)),
                new("مشاغل", Icons.Material.Outlined.Work, "/hr/jobs", P(H.Setup)),
            ]),
        ]),
        new("مالی", Icons.Material.Outlined.AccountBalanceWallet, Children:
        [
            new("حسابداری", Icons.Material.Outlined.Calculate, Children:
            [
                new("اسناد حسابداری", Icons.Material.Outlined.ReceiptLong, "/vouchers", P(A.VoucherView), NavLinkMatch.All),
                new("ورود سند از اکسل", Icons.Material.Outlined.UploadFile, "/vouchers/import", P(A.VoucherCreate)),
                new("ترتیب و دائم کردن اسناد", Icons.Material.Outlined.FormatListNumbered, "/vouchers/numbering", P(A.VoucherPermanent, A.VoucherRenumber)),
                new("گزارش‌ها", Icons.Material.Outlined.Assessment, Children:
                [
                    new("تراز آزمایشی", Icons.Material.Outlined.TableChart, "/reports/trial-balance", P(A.TrialBalance)),
                    new("مرور حساب‌ها", Icons.Material.Outlined.ManageSearch, "/reports/review", P(A.Review)),
                    new("ترازنامه و سود و زیان", Icons.Material.Outlined.Summarize, "/reports/statements", P(A.Statements)),
                    new("دفاتر قانونی", Icons.Material.Outlined.LibraryBooks, "/reports/books", P(A.Books)),
                    new("دفتر معین و تفصیلی", Icons.Material.Outlined.MenuBook, "/reports/ledger", P(A.Ledger)),
                ]),
                new("تعاریف", Icons.Material.Outlined.Tune, Expanded: false, Children:
                [
                    new("کدینگ حساب‌ها", Icons.Material.Outlined.AccountTree, "/accounts", P(A.AccountView)),
                    new("گروه‌ها و حساب‌های تفصیلی", Icons.Material.Outlined.Category, "/detail-types", P(A.DetailView)),
                    new("سال و دوره مالی", Icons.Material.Outlined.CalendarMonth, "/fiscal-years", P(A.FiscalView)),
                    new("بستن سال مالی", Icons.Material.Outlined.EventBusy, "/fiscal-years/closing", P(A.FiscalClose)),
                    new("الگوهای صدور سند", Icons.Material.Outlined.AutoMode, "/posting-templates", P(A.TemplateView)),
                ]),
            ]),
            new("هزینه‌ها", Icons.Material.Outlined.RequestQuote, Children:
            [
                new("شناسایی هزینه", Icons.Material.Outlined.Receipt, "/expenses", P(ExpensePermissions.View), NavLinkMatch.All),
            ]),
            new("خزانه‌داری", Icons.Material.Outlined.Savings, Children:
            [
                new("دریافت", Icons.Material.Outlined.CallReceived, "/treasury/receipts", P(T.ReceiptView)),
                new("پرداخت", Icons.Material.Outlined.CallMade, "/treasury/payments", P(T.PaymentView)),
                new("انتقال وجه", Icons.Material.Outlined.SwapHoriz, "/treasury/transfers", P(T.TransferView)),
                new("صورت‌هزینه تنخواه", Icons.Material.Outlined.Wallet, "/treasury/imprest", P(T.ImprestView)),
                new("عملیات اسناد دریافتنی", Icons.Material.Outlined.MoveDown, "/treasury/cheque-ops/in", P(T.ChequeInView)),
                new("عملیات اسناد پرداختنی", Icons.Material.Outlined.MoveUp, "/treasury/cheque-ops/out", P(T.ChequeOutView)),
                new("اسناد تضمینی", Icons.Material.Outlined.VerifiedUser, "/treasury/guarantees", P(T.GuaranteeView)),
                new("گزارش‌های خزانه", Icons.Material.Outlined.Assessment, Expanded: false, Children:
                [
                    new("چک‌های دریافتنی", Icons.Material.Outlined.FactCheck, "/treasury/reports/cheques-in", P(T.Reports)),
                    new("چک‌های پرداختنی", Icons.Material.Outlined.FactCheck, "/treasury/reports/cheques-out", P(T.Reports)),
                    new("سررسید ماهانه چک‌ها", Icons.Material.Outlined.EventNote, "/treasury/reports/due", P(T.Reports)),
                    new("موجودی صندوق و بانک", Icons.Material.Outlined.AccountBalance, "/treasury/reports/balances", P(T.Reports)),
                ]),
                new("دسته‌چک، سفته و ضمانت‌نامه", Icons.Material.Outlined.MenuBook, "/treasury/instruments", P(T.InstrumentView)),
                new("تعاریف خزانه‌داری", Icons.Material.Outlined.Tune, "/treasury/setup", P(T.Setup)),
            ]),
        ]),
        new("اطلاعات پایه", Icons.Material.Outlined.Storage, Children:
        [
            new("اشخاص", Icons.Material.Outlined.People, "/persons", P(B.PersonView)),
            new("نقش‌های اشخاص", Icons.Material.Outlined.Label, "/base/roles", P(B.PersonRoles)),
            new("جدول‌های پایه", Icons.Material.Outlined.ListAlt, "/base/lookups", P(B.Lookups)),
            new("بانک‌ها", Icons.Material.Outlined.AccountBalance, "/base/banks", P(B.Banks)),
            new("نرخ مالیات و عوارض", Icons.Material.Outlined.Percent, "/base/vat-rates", P(B.VatRates)),
        ]),
        new("مدیریت سیستم", Icons.Material.Outlined.AdminPanelSettings, Expanded: false, Children:
        [
            new("کاربران", Icons.Material.Outlined.ManageAccounts, "/admin/users", P(S.Users)),
            new("نقش‌ها و مجوزها", Icons.Material.Outlined.Key, "/admin/roles", P(S.Roles)),
            new("شرکت‌ها", Icons.Material.Outlined.Business, "/admin/companies", P(S.Companies)),
            new("لاگ تغییرات", Icons.Material.Outlined.History, "/admin/audit", P(S.AuditReport)),
            new("لاگ امنیتی", Icons.Material.Outlined.Shield, "/admin/security-log", P(S.Log)),
            new("تنظیمات امنیتی", Icons.Material.Outlined.Settings, "/admin/settings", P(S.Settings)),
        ]),
    ];

    /// <summary>مجوز لازم برای باز کردن هر آدرس (طولانی‌ترین پیشوند منطبق). null = آزاد برای همه کاربران واردشده.</summary>
    private static readonly (string Prefix, string[] Permissions)[] Routes =
    [
        ("/vouchers/new", P(A.VoucherCreate)),
        ("/vouchers/print", P(A.VoucherPrint)),
        ("/vouchers/import", P(A.VoucherCreate)),
        ("/vouchers", P(A.VoucherView)),
        ("/reports/trial-balance", P(A.TrialBalance)),
        ("/reports/ledger", P(A.Ledger)),
        ("/reports/review", P(A.Review)),
        ("/reports/statements", P(A.Statements)),
        ("/reports/books", P(A.Books)),
        ("/vouchers/numbering", P(A.VoucherPermanent, A.VoucherRenumber)),
        ("/fiscal-years/closing", P(A.FiscalClose)),
        ("/accounts", P(A.AccountView)),
        ("/detail-types", P(A.DetailView)),
        ("/fiscal-years", P(A.FiscalView)),
        ("/posting-templates", P(A.TemplateView)),
        ("/sales/returns/new", P(SL.ReturnCreate)),
        ("/sales/returns", P(SL.ReturnView)),
        ("/sales/new", P(SL.InvoiceCreate)),
        ("/sales", P(SL.InvoiceView)),
        ("/purchases/returns/new", P(PU.ReturnCreate)),
        ("/purchases/returns", P(PU.ReturnView)),
        ("/purchases/new", P(PU.InvoiceCreate)),
        ("/purchases", P(PU.InvoiceView)),
        ("/inventory/items", P(I.ItemView)),
        ("/inventory/groups", P(I.Groups)),
        ("/inventory/warehouses", P(I.Warehouses)),
        ("/inventory/receipts/new", P(I.ReceiptCreate)),
        ("/inventory/receipts", P(I.ReceiptView)),
        ("/inventory/issues/new", P(I.IssueCreate)),
        ("/inventory/issues", P(I.IssueView)),
        ("/inventory/kardex", P(I.Kardex)),
        ("/inventory/balances", P(I.Kardex)),
        ("/inventory/costing", P(I.Costing)),
        ("/expenses/new", P(ExpensePermissions.Create)),
        ("/expenses", P(ExpensePermissions.View)),
        ("/treasury/receipts/new", P(T.ReceiptCreate)),
        ("/treasury/receipts", P(T.ReceiptView)),
        ("/treasury/payments/new", P(T.PaymentCreate)),
        ("/treasury/payments", P(T.PaymentView)),
        ("/treasury/transfers/new", P(T.TransferCreate)),
        ("/treasury/transfers", P(T.TransferView)),
        ("/treasury/imprest/new", P(T.ImprestCreate)),
        ("/treasury/imprest", P(T.ImprestView)),
        ("/treasury/cheque-ops/in", P(T.ChequeInView)),
        ("/treasury/cheque-ops/out", P(T.ChequeOutView)),
        ("/treasury/cheque-ops", P(T.ChequeInView, T.ChequeOutView)),
        ("/treasury/guarantees/new", P(T.GuaranteeCreate)),
        ("/treasury/guarantees", P(T.GuaranteeView)),
        ("/treasury/reports", P(T.Reports)),
        ("/treasury/instruments", P(T.InstrumentView)),
        ("/treasury/setup", P(T.Setup)),
        ("/costing/centers/new", P(C.CenterCreate)),
        ("/costing/centers", P(C.CenterView)),
        ("/costing/factors", P(C.FactorView)),
        ("/costing/center-types", P(C.CenterTypes)),
        ("/hr/employees", P(H.EmployeeView)),
        ("/hr/workshops", P(H.Setup)),
        ("/hr/jobs", P(H.Setup)),
        ("/payroll/decrees", P(PY.DecreeView)),
        ("/payroll/runs", P(PY.RunView)),
        ("/payroll/loans", P(PY.LoanView)),
        ("/payroll/settlement", P(PY.RunEdit)),
        ("/payroll/reports", P(PY.ReportView)),
        ("/payroll/items", P(PY.Setup)),
        ("/payroll/params", P(PY.Setup)),
        ("/persons/new", P(B.PersonCreate)),
        ("/persons/import", P(B.PersonCreate)),
        ("/persons", P(B.PersonView)),
        ("/base/roles", P(B.PersonRoles)),
        ("/base/lookups", P(B.Lookups)),
        ("/base/banks", P(B.Banks)),
        ("/base/vat-rates", P(B.VatRates)),
        ("/admin/users", P(S.Users)),
        ("/admin/roles", P(S.Roles)),
        ("/admin/companies", P(S.Companies)),
        ("/admin/audit", P(S.AuditReport)),
        ("/admin/security-log", P(S.Log)),
        ("/admin/settings", P(S.Settings)),
    ];

    /// <summary>مجوزهایی که یکی‌شان برای باز کردن آدرس لازم است (null = نیازی نیست).</summary>
    public static string[]? Required(string path)
    {
        path = "/" + path.Split('?', '#')[0].Trim('/');
        return Routes.Where(r => path == r.Prefix || path.StartsWith(r.Prefix + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Prefix.Length).Select(r => r.Permissions).FirstOrDefault();
    }

    public static bool CanOpen(IAccessControl access, string path) =>
        Required(path) is not { } required || required.Any(access.Has);
}
