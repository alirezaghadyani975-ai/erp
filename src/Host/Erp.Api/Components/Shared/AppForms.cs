using MudBlazor;

namespace Erp.Api.Components.Shared;

/// <summary>فهرست فرم‌ها برای جستجوی سراسری و دسترسی سریع.</summary>
public static class AppForms
{
    public sealed record Item(string Title, string Group, string Url, string Icon, string Keywords = "");

    public static readonly IReadOnlyList<Item> All =
    [
        new("سند جدید", "حسابداری", "/vouchers/new", Icons.Material.Outlined.NoteAdd, "ثبت سند"),
        new("اسناد حسابداری", "حسابداری", "/vouchers", Icons.Material.Outlined.ReceiptLong, "فهرست اسناد"),
        new("شناسایی هزینه جدید", "هزینه‌ها", "/expenses/new", Icons.Material.Outlined.RequestQuote, "فاکتور خدماتی هزینه"),
        new("شناسایی هزینه", "هزینه‌ها", "/expenses", Icons.Material.Outlined.Receipt, "فاکتور خدماتی هزینه"),
        new("دریافت جدید", "خزانه‌داری", "/treasury/receipts/new", Icons.Material.Outlined.CallReceived, "دریافت وجه چک نقد"),
        new("دریافت‌ها", "خزانه‌داری", "/treasury/receipts", Icons.Material.Outlined.CallReceived, "دریافت وجه"),
        new("پرداخت جدید", "خزانه‌داری", "/treasury/payments/new", Icons.Material.Outlined.CallMade, "پرداخت وجه چک نقد"),
        new("پرداخت‌ها", "خزانه‌داری", "/treasury/payments", Icons.Material.Outlined.CallMade, "پرداخت وجه"),
        new("انتقال وجه", "خزانه‌داری", "/treasury/transfers", Icons.Material.Outlined.SwapHoriz, "انتقال بانک صندوق شارژ تنخواه"),
        new("صورت‌هزینه تنخواه", "خزانه‌داری", "/treasury/imprest", Icons.Material.Outlined.Wallet, "تنخواه"),
        new("عملیات اسناد دریافتنی", "خزانه‌داری", "/treasury/cheque-ops/in", Icons.Material.Outlined.MoveDown, "چک واگذاری وصول برگشت"),
        new("عملیات اسناد پرداختنی", "خزانه‌داری", "/treasury/cheque-ops/out", Icons.Material.Outlined.MoveUp, "چک پاس ابطال"),
        new("اسناد تضمینی", "خزانه‌داری", "/treasury/guarantees", Icons.Material.Outlined.VerifiedUser, "ضمانت‌نامه سفته چک تضمینی"),
        new("گزارش چک‌های دریافتنی", "خزانه‌داری", "/treasury/reports/cheques-in", Icons.Material.Outlined.FactCheck, "چک"),
        new("گزارش چک‌های پرداختنی", "خزانه‌داری", "/treasury/reports/cheques-out", Icons.Material.Outlined.FactCheck, "چک صادره"),
        new("سررسید ماهانه چک‌ها", "خزانه‌داری", "/treasury/reports/due", Icons.Material.Outlined.EventNote, "نقدینگی"),
        new("موجودی صندوق و بانک", "خزانه‌داری", "/treasury/reports/balances", Icons.Material.Outlined.AccountBalance, "مانده بانک صندوق"),
        new("دسته‌چک، سفته و ضمانت‌نامه", "خزانه‌داری", "/treasury/instruments", Icons.Material.Outlined.MenuBook, "دسته چک برگ سفته ضمانت نامه بانکی"),
        new("تعاریف خزانه‌داری", "خزانه‌داری", "/treasury/setup", Icons.Material.Outlined.Tune, "صندوق حساب بانکی تنخواه‌گردان نوع دریافت پرداخت"),
        new("تراز آزمایشی", "گزارش‌ها", "/reports/trial-balance", Icons.Material.Outlined.TableChart, "تراز"),
        new("دفتر معین و تفصیلی", "گزارش‌ها", "/reports/ledger", Icons.Material.Outlined.MenuBook, "دفتر ریز گردش"),
        new("شخص جدید", "اطلاعات پایه", "/persons/new", Icons.Material.Outlined.PersonAdd, "مشتری تأمین‌کننده"),
        new("اشخاص", "اطلاعات پایه", "/persons", Icons.Material.Outlined.People, "مشتری تأمین‌کننده طرف حساب"),
        new("کدینگ حساب‌ها", "تعاریف", "/accounts", Icons.Material.Outlined.AccountTree, "کل معین گروه"),
        new("گروه‌ها و حساب‌های تفصیلی", "تعاریف", "/detail-types", Icons.Material.Outlined.Category, "تفصیلی"),
        new("الگوهای صدور سند", "تعاریف", "/posting-templates", Icons.Material.Outlined.AutoMode, "سند خودکار"),
        new("سال و دوره مالی", "تعاریف", "/fiscal-years", Icons.Material.Outlined.CalendarMonth, "دوره"),
        new("مراکز هزینه", "بهای تمام‌شده", "/costing/centers", Icons.Material.Outlined.Hub, "مرکز"),
        new("عوامل هزینه", "بهای تمام‌شده", "/costing/factors", Icons.Material.Outlined.Category, "عامل"),
        new("انواع مرکز هزینه", "بهای تمام‌شده", "/costing/center-types", Icons.Material.Outlined.Lan, "نوع مرکز"),
        new("نقش‌های اشخاص", "اطلاعات پایه", "/base/roles", Icons.Material.Outlined.Label, "نقش"),
        new("جدول‌های پایه", "اطلاعات پایه", "/base/lookups", Icons.Material.Outlined.ListAlt, "شهر استان"),
        new("بانک‌ها", "اطلاعات پایه", "/base/banks", Icons.Material.Outlined.AccountBalance, "بانک"),
        new("نرخ مالیات و عوارض", "اطلاعات پایه", "/base/vat-rates", Icons.Material.Outlined.Percent, "ارزش افزوده"),
    ];
}
