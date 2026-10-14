namespace Erp.SharedKernel.Abstractions;

/// <summary>
/// تراکنش مشترک همه ماژول‌ها روی دیتابیس شرکت. هر ماژول DbContext خودش را دارد ولی همه روی یک اتصال‌اند؛
/// با این تراکنش، تغییرات چند ماژول (مثلاً شخص و تفصیلی‌اش) با هم ثبت یا با هم برگردانده می‌شوند.
/// اگر تراکنشی باز باشد، BeginAsync همان را ادامه می‌دهد و Commit فقط در بیرونی‌ترین سطح انجام می‌شود.
/// </summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken ct = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
