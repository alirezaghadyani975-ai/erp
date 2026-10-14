using Erp.Accounting.Application.Abstractions;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Accounting.Application.Common;

/// <summary>تبدیل خطا به پیام فارسی قابل نمایش به کاربر (هم برای API و هم رابط کاربری).</summary>
public static class ErrorMessages
{
    public static string? ForUser(Exception ex)
    {
        var sqlNumber = SqlErrorNumber(ex);
        return ex switch
        {
            DomainException or NotFoundException => ex.Message,
            FormatException => ex.Message,
            DbUpdateConcurrencyException => "این رکورد همزمان توسط کاربر دیگری تغییر کرده است؛ دوباره بارگذاری کنید.",
            _ when sqlNumber >= 50000 => Innermost(ex).Message,
            _ when sqlNumber is 2601 or 2627 => "رکوردی با همین کد یا شماره قبلاً ثبت شده است.",
            _ when sqlNumber is 547 => "داده با قواعد دیتابیس سازگار نیست یا به رکورد دیگری وابسته است.",
            _ => null
        };
    }

    /// <summary>شماره خطای SQL Server بدون وابستگی مستقیم به SqlClient.</summary>
    public static int? SqlErrorNumber(Exception ex)
    {
        var inner = Innermost(ex);
        return inner.GetType().Name == "SqlException"
            ? inner.GetType().GetProperty("Number")?.GetValue(inner) as int?
            : null;
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
