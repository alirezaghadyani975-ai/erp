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
        new("مالی", Icons.Material.Outlined.AccountBalanceWallet, Children:
        [
            new("حسابداری", Icons.Material.Outlined.Calculate, Children:
            [
                new("اسناد حسابداری", Icons.Material.Outlined.ReceiptLong, "/vouchers", P(A.VoucherView), NavLinkMatch.All),
                new("گزارش‌ها", Icons.Material.Outlined.Assessment, Children:
                [
                    new("تراز آزمایشی", Icons.Material.Outlined.TableChart, "/reports/trial-balance", P(A.TrialBalance)),
                    new("دفتر معین و تفصیلی", Icons.Material.Outlined.MenuBook, "/reports/ledger", P(A.Ledger)),
                ]),
                new("تعاریف", Icons.Material.Outlined.Tune, Expanded: false, Children:
                [
                    new("کدینگ حساب‌ها", Icons.Material.Outlined.AccountTree, "/accounts", P(A.AccountView)),
                    new("گروه‌ها و حساب‌های تفصیلی", Icons.Material.Outlined.Category, "/detail-types", P(A.DetailView)),
                    new("سال و دوره مالی", Icons.Material.Outlined.CalendarMonth, "/fiscal-years", P(A.FiscalView)),
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
            new("بهای تمام‌شده", Icons.Material.Outlined.Factory, Expanded: false, Children:
            [
                new("مراکز هزینه", Icons.Material.Outlined.Hub, "/costing/centers", P(C.CenterView)),
                new("عوامل هزینه", Icons.Material.Outlined.Category, "/costing/factors", P(C.FactorView)),
                new("انواع مرکز هزینه", Icons.Material.Outlined.Lan, "/costing/center-types", P(C.CenterTypes)),
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
        ("/vouchers", P(A.VoucherView)),
        ("/reports/trial-balance", P(A.TrialBalance)),
        ("/reports/ledger", P(A.Ledger)),
        ("/accounts", P(A.AccountView)),
        ("/detail-types", P(A.DetailView)),
        ("/fiscal-years", P(A.FiscalView)),
        ("/posting-templates", P(A.TemplateView)),
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
        ("/persons/new", P(B.PersonCreate)),
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
