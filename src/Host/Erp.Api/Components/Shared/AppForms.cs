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
    ];
}
