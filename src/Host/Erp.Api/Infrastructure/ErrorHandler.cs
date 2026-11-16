using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Infrastructure;

/// <summary>
/// تبدیل خطاها به پاسخ استاندارد با پیام فارسی.
/// خطاهای تریگرهای دیتابیس (شماره 50000 به بالا) پیام فارسی خودشان را دارند و مستقیم نمایش داده می‌شوند.
/// </summary>
public class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var message = ErrorMessages.ForUser(exception);
        if (message is null)
        {
            // خطای فنی: با کد پیگیری در لاگ فایل ثبت و همان کد به کاربر داده می‌شود
            var code = ErrorCode.New();
            logger.LogError(exception, "خطای پیش‌بینی‌نشده {Code} در {Method} {Path} (کاربر: {User})", code,
                context.Request.Method, context.Request.Path, context.User.Identity?.Name);
            context.Response.StatusCode = 500;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = 500, Title = "خطای سیستم", Detail = ErrorCode.Message(code) }
            });
        }

        var status = exception switch
        {
            ForbiddenException => 403,
            SessionExpiredException => 401,
            NotFoundException => 404,
            DbUpdateConcurrencyException => 409,
            _ when ErrorMessages.SqlErrorNumber(exception) is 2601 or 2627 => 409,
            _ => 400
        };

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = status switch { 403 => "دسترسی ندارید", 401 => "ورود لازم است", 404 => "پیدا نشد", 409 => "تداخل", _ => "خطای قواعد کسب‌وکار" },
                Detail = message
            }
        });
    }
}
