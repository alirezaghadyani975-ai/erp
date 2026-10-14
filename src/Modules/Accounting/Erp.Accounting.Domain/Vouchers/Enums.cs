namespace Erp.Accounting.Domain.Vouchers;

/// <summary>وضعیت سند (fin.Vouchers.Status).</summary>
public enum VoucherStatus : byte
{
    Draft = 1,      // پیش‌نویس: آزادانه قابل ویرایش، لازم نیست تراز باشد
    Temporary = 2,  // موقت: تراز و بررسی‌شده، قابل برگشت به پیش‌نویس
    Permanent = 3,  // دائم: قطعی، شماره قطعی دارد و هیچ‌وقت تغییر نمی‌کند
    Void = 9        // باطل
}

/// <summary>نوع سند (fin.Vouchers.VoucherType).</summary>
public enum VoucherType : byte
{
    Opening = 1,          // افتتاحیه
    Normal = 2,           // عادی
    ClosingTemporary = 3, // بستن حساب‌های موقت
    Closing = 4           // اختتامیه
}
