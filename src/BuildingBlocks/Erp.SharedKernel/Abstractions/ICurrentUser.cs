namespace Erp.SharedKernel.Abstractions;

/// <summary>کاربر جاری. کاربران در دیتابیس مرکزی‌اند؛ در دیتابیس شرکت فقط شناسه‌شان ثبت می‌شود.</summary>
public interface ICurrentUser
{
    int UserId { get; }
    string? UserName { get; }
}
