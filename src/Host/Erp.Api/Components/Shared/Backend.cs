using Erp.Api.Infrastructure;

namespace Erp.Api.Components.Shared;

/// <summary>
/// اجرای هر عملیات رابط کاربری در یک scope جدید DI.
/// در Blazor Server یک scope برای کل اتصال کاربر باقی می‌ماند؛ اگر DbContext در آن بماند،
/// خطای یک عملیات روی عملیات بعدی اثر می‌گذارد. با scope جدا، هر عملیات DbContext تمیز خودش را دارد.
/// </summary>
public sealed class Backend(IServiceScopeFactory scopes, ILogger<Backend> logger)
{
    public async Task<Result<T>> Run<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return Result<T>.Ok(await operation(scope.ServiceProvider.GetRequiredService<TService>()));
        }
        catch (Exception ex)
        {
            var message = ErrorMessages.ForUser(ex);
            if (message is null)
            {
                logger.LogError(ex, "خطای پیش‌بینی‌نشده در رابط کاربری");
                message = "خطای پیش‌بینی‌نشده رخ داد. جزئیات در لاگ برنامه ثبت شد.";
            }
            return Result<T>.Fail(message);
        }
    }

    public Task<Result<bool>> Run<TService>(Func<TService, Task> operation) where TService : notnull =>
        Run<TService, bool>(async s => { await operation(s); return true; });
}

public readonly record struct Result<T>(bool Success, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
