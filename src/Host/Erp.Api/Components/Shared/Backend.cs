using Erp.Api.Infrastructure;
using Erp.Security.Application;
using Erp.Security.Domain;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Components;

namespace Erp.Api.Components.Shared;

/// <summary>
/// اجرای هر عملیات رابط کاربری در یک scope جدید DI.
/// در Blazor Server یک scope برای کل اتصال کاربر باقی می‌ماند؛ اگر DbContext در آن بماند،
/// خطای یک عملیات روی عملیات بعدی اثر می‌گذارد. با scope جدا، هر عملیات DbContext تمیز خودش را دارد.
/// نشست کاربر (کاربر، شرکت، مجوزها) از اتصال به scope عملیات کپی می‌شود.
/// </summary>
public sealed class Backend(IServiceScopeFactory scopes, AppSession session, NavigationManager nav, ILogger<Backend> logger, IConfiguration config)
{
    /// <summary>عملیات کندتر از این (میلی‌ثانیه) در لاگ هشدار می‌گیرد؛ تنظیم: Diagnostics:SlowOperationMs.</summary>
    private int SlowMs => config.GetValue("Diagnostics:SlowOperationMs", 700);

    public async Task<Result<T>> Run<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan sessionTime = default;
        try
        {
            await session.EnsureAsync();
            sessionTime = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<AppSession>().Adopt(session);
            return Result<T>.Ok(await operation(scope.ServiceProvider.GetRequiredService<TService>()));
        }
        catch (SessionExpiredException ex)
        {
            nav.NavigateTo($"{Auth.LoginPath}?expired=1", forceLoad: true);
            return Result<T>.Fail(ex.Message);
        }
        catch (ForbiddenException ex)
        {
            await LogDeniedAsync(ex.Message);
            return Result<T>.Fail(ex.Message);
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            // لغو عمدی (مثلاً جستجوی قبلی انتخاب‌گر با تایپ حرف بعدی)؛ خطا نیست و لاگ نمی‌شود.
            // SqlClient لغو را گاهی با SqlException «Operation cancelled by user» می‌دهد، نه OperationCanceledException.
            return Result<T>.Fail("عملیات لغو شد.");
        }
        catch (Exception ex)
        {
            var message = ErrorMessages.ForUser(ex);
            if (message is null)
            {
                var code = ErrorCode.New();
                logger.LogError(ex, "خطای پیش‌بینی‌نشده {Code} در رابط کاربری ({Service}، کاربر: {User})", code,
                    typeof(TService).Name, session.UserName);
                message = ErrorCode.Message(code);
            }
            return Result<T>.Fail(message);
        }
        finally
        {
            // عملیات کند در لاگ (برای پیدا کردن گلوگاه روی داده‌های سنگین)
            var total = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (total.TotalMilliseconds >= SlowMs)
                logger.LogWarning("عملیات کند {Service}: {Total} میلی‌ثانیه (نشست {Session})", typeof(TService).Name,
                    (int)total.TotalMilliseconds, (int)sessionTime.TotalMilliseconds);
        }
    }

    private static bool IsCancellation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is OperationCanceledException
                || (e.GetType().Name == "SqlException" && e.Message.Contains("Operation cancelled by user", StringComparison.Ordinal)))
                return true;
        return false;
    }

    public Task<Result<bool>> Run<TService>(Func<TService, Task> operation) where TService : notnull =>
        Run<TService, bool>(async s => { await operation(s); return true; });

    /// <summary>ثبت تلاش بدون مجوز در لاگ امنیتی (اگر ثبت نشد، کار کاربر متوقف نمی‌شود).</summary>
    private async Task LogDeniedAsync(string message)
    {
        if (!session.IsAuthenticated) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuthService>().LogAsync(SecurityEvent.AccessDenied, session.UserId,
                session.UserName, session.Info?.CompanyKey, session.Ip, message, default);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ثبت تلاش بدون مجوز در لاگ امنیتی نشد.");
        }
    }
}

public readonly record struct Result<T>(bool Success, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
