using Erp.Accounting.Application.Abstractions;
using Erp.Accounting.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Infrastructure;

/// <summary>
/// تبدیل خطاها به پاسخ استاندارد با پیام فارسی.
/// خطاهای تریگرهای دیتابیس (شماره 50000 به بالا) پیام فارسی خودشان را دارند و مستقیم نمایش داده می‌شوند.
/// </summary>
public class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var message = ErrorMessages.ForUser(exception);
        if (message is null) return false;

        var status = exception switch
        {
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
                Title = status == 404 ? "پیدا نشد" : status == 409 ? "تداخل" : "خطای قواعد کسب‌وکار",
                Detail = message
            }
        });
    }
}
